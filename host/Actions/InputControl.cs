using System;
using System.Collections.Generic;
using System.Threading;
using CapriHost.Core;

namespace CapriHost.Actions
{
    // Điều khiển chuột/bàn phím cơ bản qua user32 (keybd_event, mouse_event, SendInput) - có từ Windows 2000.
    internal static class InputControl
    {
        // Lệnh đang chạy trên luồng công cụ này: gõ chữ dài sẽ dừng sớm khi bị Dừng/hết giờ.
        [ThreadStatic] internal static CommandContext Stop;

        private static readonly Dictionary<string, byte> Keys = BuildKeys();

        private static Dictionary<string, byte> BuildKeys()
        {
            Dictionary<string, byte> map = new Dictionary<string, byte>();
            map["ctrl"] = 0x11; map["control"] = 0x11; map["shift"] = 0x10; map["alt"] = 0x12;
            map["win"] = 0x5B; map["windows"] = 0x5B; map["meta"] = 0x5B;
            map["enter"] = 0x0D; map["return"] = 0x0D; map["esc"] = 0x1B; map["escape"] = 0x1B;
            map["tab"] = 0x09; map["space"] = 0x20; map["backspace"] = 0x08; map["delete"] = 0x2E; map["del"] = 0x2E;
            map["insert"] = 0x2D; map["home"] = 0x24; map["end"] = 0x23; map["pageup"] = 0x21; map["pagedown"] = 0x22;
            map["left"] = 0x25; map["up"] = 0x26; map["right"] = 0x27; map["down"] = 0x28;
            map["printscreen"] = 0x2C; map["capslock"] = 0x14;
            for (int i = 1; i <= 12; i++) map["f" + i] = (byte)(0x6F + i);
            return map;
        }

        // Phím mở rộng cần cờ KEYEVENTF_EXTENDEDKEY.
        private static bool Extended(byte vk)
        {
            return (vk >= 0x21 && vk <= 0x28) || vk == 0x2D || vk == 0x2E || vk == 0x5B || vk == 0x2C;
        }

        private static bool Parse(string name, out byte vk)
        {
            vk = 0;
            name = name.Trim().ToLowerInvariant();
            if (Keys.TryGetValue(name, out vk)) return true;
            if (name.Length == 1)
            {
                char c = name[0];
                if (c >= 'a' && c <= 'z') { vk = (byte)(c - 'a' + 0x41); return true; }
                if (c >= '0' && c <= '9') { vk = (byte)c; return true; }
            }
            return false;
        }

        // Ví dụ: "ctrl+shift+t", "alt+f4", "win+d", "enter".
        internal static string Press(string combo)
        {
            List<byte> codes = new List<byte>();
            foreach (string part in (combo ?? "").Split('+'))
            {
                if (part.Trim().Length == 0) continue;
                byte vk;
                if (!Parse(part, out vk)) return "Không hiểu phím '" + part.Trim() + "'";
                codes.Add(vk);
            }
            if (codes.Count == 0) return "Chưa có phím nào";
            foreach (byte vk in codes) { Native.keybd_event(vk, 0, Extended(vk) ? Native.KEYEVENTF_EXTENDEDKEY : 0, UIntPtr.Zero); Thread.Sleep(15); }
            for (int i = codes.Count - 1; i >= 0; i--)
            {
                byte vk = codes[i];
                Native.keybd_event(vk, 0, (Extended(vk) ? Native.KEYEVENTF_EXTENDEDKEY : 0) | Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
                Thread.Sleep(15);
            }
            return null;
        }

        internal static void Tap(byte vk)
        {
            Native.keybd_event(vk, 0, Extended(vk) ? Native.KEYEVENTF_EXTENDEDKEY : 0, UIntPtr.Zero);
            Native.keybd_event(vk, 0, (Extended(vk) ? Native.KEYEVENTF_EXTENDEDKEY : 0) | Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        internal static void TypeText(string text, bool enter)
        {
            foreach (char c in text)
            {
                if (Stop != null && Stop.Cancelled) return;
                if (c == '\r') continue;
                if (c == '\n') { Tap(0x0D); continue; }
                Native.TypeChar(c);
                Thread.Sleep(8);
            }
            if (enter) Tap(0x0D);
        }

        internal static void MoveTo(int x, int y) { Native.SetCursorPos(x, y); }

        internal static void Click(int x, int y, string button, bool twice)
        {
            Native.SetCursorPos(x, y);
            Thread.Sleep(40);
            uint down = Native.MOUSEEVENTF_LEFTDOWN, up = Native.MOUSEEVENTF_LEFTUP;
            if (button == "right") { down = Native.MOUSEEVENTF_RIGHTDOWN; up = Native.MOUSEEVENTF_RIGHTUP; }
            else if (button == "middle") { down = Native.MOUSEEVENTF_MIDDLEDOWN; up = Native.MOUSEEVENTF_MIDDLEUP; }
            int times = twice ? 2 : 1;
            for (int i = 0; i < times; i++)
            {
                Native.mouse_event(down, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(30);
                Native.mouse_event(up, 0, 0, 0, UIntPtr.Zero);
                if (i + 1 < times) Thread.Sleep(70);
            }
        }

        // amount > 0: cuộn lên, < 0: cuộn xuống (mỗi bước = 1 nấc chuột).
        internal static void Scroll(int amount)
        {
            Native.mouse_event(Native.MOUSEEVENTF_WHEEL, 0, 0, amount * 120, UIntPtr.Zero);
        }

        // Phím media/âm lượng: VK_MEDIA_* và VK_VOLUME_*.
        internal static bool MediaKey(string action)
        {
            byte vk;
            switch (action)
            {
                case "play_pause": vk = 0xB3; break;
                case "next": vk = 0xB0; break;
                case "previous": vk = 0xB1; break;
                case "stop": vk = 0xB2; break;
                case "mute": vk = 0xAD; break;
                case "volume_down": vk = 0xAE; break;
                case "volume_up": vk = 0xAF; break;
                default: return false;
            }
            Native.keybd_event(vk, 0, Native.KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
            Native.keybd_event(vk, 0, Native.KEYEVENTF_EXTENDEDKEY | Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
            return true;
        }
    }
}
