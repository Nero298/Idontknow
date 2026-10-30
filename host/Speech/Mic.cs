using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace CapriHost.Speech
{
    // Ghi âm micro bằng winmm waveIn (có từ Windows 95, không cần thư viện ngoài).
    // 16 kHz, mono, 16-bit: đủ cho nhận dạng giọng nói và nhẹ (~32 KB/giây).
    internal sealed class Mic
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WAVEFORMATEX
        {
            public short wFormatTag; public short nChannels; public int nSamplesPerSec; public int nAvgBytesPerSec;
            public short nBlockAlign; public short wBitsPerSample; public short cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR
        {
            public IntPtr lpData; public uint dwBufferLength; public uint dwBytesRecorded; public IntPtr dwUser;
            public uint dwFlags; public uint dwLoops; public IntPtr lpNext; public IntPtr reserved;
        }

        [DllImport("winmm.dll")] private static extern int waveInOpen(out IntPtr handle, uint device, ref WAVEFORMATEX format, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] private static extern int waveInPrepareHeader(IntPtr handle, IntPtr header, int size);
        [DllImport("winmm.dll")] private static extern int waveInUnprepareHeader(IntPtr handle, IntPtr header, int size);
        [DllImport("winmm.dll")] private static extern int waveInAddBuffer(IntPtr handle, IntPtr header, int size);
        [DllImport("winmm.dll")] private static extern int waveInStart(IntPtr handle);
        [DllImport("winmm.dll")] private static extern int waveInStop(IntPtr handle);
        [DllImport("winmm.dll")] private static extern int waveInReset(IntPtr handle);
        [DllImport("winmm.dll")] private static extern int waveInClose(IntPtr handle);

        private const uint WAVE_MAPPER = 0xFFFFFFFF;
        private const uint WHDR_DONE = 0x1;
        private const int BufferCount = 6;
        private const int BufferBytes = 3200 * 2;      // 200 ms mỗi bộ đệm
        private const int MaxSeconds = 45;

        private IntPtr handle;
        private readonly IntPtr[] headers = new IntPtr[BufferCount];
        private readonly IntPtr[] buffers = new IntPtr[BufferCount];
        private MemoryStream pcm;
        private Thread worker;
        private volatile bool stopping;
        private bool open;

        internal bool Recording { get { return open; } }

        internal void Start()
        {
            if (open) return;
            WAVEFORMATEX format = new WAVEFORMATEX();
            format.wFormatTag = 1; format.nChannels = 1; format.nSamplesPerSec = 16000;
            format.nAvgBytesPerSec = 32000; format.nBlockAlign = 2; format.wBitsPerSample = 16; format.cbSize = 0;
            if (waveInOpen(out handle, WAVE_MAPPER, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0)
                throw new InvalidOperationException("Không mở được micro. Kiểm tra micro đã cắm và được bật trong Windows.");
            pcm = new MemoryStream();
            int headerSize = Marshal.SizeOf(typeof(WAVEHDR));
            for (int i = 0; i < BufferCount; i++)
            {
                buffers[i] = Marshal.AllocHGlobal(BufferBytes);
                headers[i] = Marshal.AllocHGlobal(headerSize);
                WAVEHDR h = new WAVEHDR();
                h.lpData = buffers[i];
                h.dwBufferLength = BufferBytes;
                Marshal.StructureToPtr(h, headers[i], false);
                waveInPrepareHeader(handle, headers[i], headerSize);
                waveInAddBuffer(handle, headers[i], headerSize);
            }
            open = true;
            stopping = false;
            waveInStart(handle);
            worker = new Thread(Pump);
            worker.IsBackground = true;
            worker.Name = "Capri mic";
            worker.Start();
        }

        // Dừng ghi và trả về file WAV hoàn chỉnh (hoặc null nếu chưa có âm thanh).
        internal byte[] Stop()
        {
            if (!open) return null;
            stopping = true;
            if (worker != null) worker.Join(1500);
            int headerSize = Marshal.SizeOf(typeof(WAVEHDR));
            waveInStop(handle);
            waveInReset(handle);
            Drain();
            for (int i = 0; i < BufferCount; i++)
            {
                waveInUnprepareHeader(handle, headers[i], headerSize);
                Marshal.FreeHGlobal(headers[i]);
                Marshal.FreeHGlobal(buffers[i]);
            }
            waveInClose(handle);
            open = false;
            byte[] data = pcm.ToArray();
            pcm.Dispose();
            pcm = null;
            return data.Length < 3200 ? null : Wrap(data);
        }

        private void Pump()
        {
            DateTime started = DateTime.UtcNow;
            while (!stopping)
            {
                Drain();
                if ((DateTime.UtcNow - started).TotalSeconds > MaxSeconds) break;
                Thread.Sleep(30);
            }
        }

        private readonly object drainGate = new object();

        // Lấy các bộ đệm đã đầy và cho ghi tiếp.
        private void Drain()
        {
            lock (drainGate)
            {
                if (pcm == null) return;
                int headerSize = Marshal.SizeOf(typeof(WAVEHDR));
                for (int i = 0; i < BufferCount; i++)
                {
                    WAVEHDR h = (WAVEHDR)Marshal.PtrToStructure(headers[i], typeof(WAVEHDR));
                    if ((h.dwFlags & WHDR_DONE) == 0) continue;
                    if (h.dwBytesRecorded > 0)
                    {
                        byte[] chunk = new byte[h.dwBytesRecorded];
                        Marshal.Copy(buffers[i], chunk, 0, chunk.Length);
                        pcm.Write(chunk, 0, chunk.Length);
                    }
                    if (!stopping) waveInAddBuffer(handle, headers[i], headerSize);
                    else
                    {
                        // dừng: đánh dấu đã xử lý để không đọc lại
                        h.dwFlags &= ~WHDR_DONE;
                        h.dwBytesRecorded = 0;
                        Marshal.StructureToPtr(h, headers[i], false);
                    }
                }
            }
        }

        private static byte[] Wrap(byte[] data)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + data.Length);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(16000); w.Write(32000); w.Write((short)2); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(data.Length);
                w.Write(data);
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
