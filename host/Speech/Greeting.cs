using System;

namespace CapriHost.Speech
{
    // Lời chào của Capri (nguyên văn các câu trong bản gốc). Hiện chưa có giọng nói nên chỉ hiện bằng chữ.
    internal static class Greeting
    {
        internal static string ForHour(int hour)
        {
            if (hour >= 5 && hour < 12) return "Good morning, choom.";
            if (hour >= 22 || hour < 5) return "Isn't it late, choom?";
            return "Miss me, choom?";
        }
    }
}
