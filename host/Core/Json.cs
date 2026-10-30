using System;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace CapriHost.Core
{
    // JSON nhỏ gọn dựa trên JavaScriptSerializer (có sẵn trong .NET 4.x, không cần thư viện ngoài).
    // Đối tượng đọc ra là Dictionary<string, object>, mảng là object[].
    internal static class Json
    {
        private static JavaScriptSerializer Make()
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;   // audio base64 có thể rất dài
            serializer.RecursionLimit = 200;
            return serializer;
        }

        internal static string Write(object value) { return Make().Serialize(value); }

        internal static object Read(string text) { return Make().DeserializeObject(text); }

        internal static Dictionary<string, object> ReadObject(string text)
        {
            return Read(text) as Dictionary<string, object>;
        }

        // Tạo dictionary từ cặp khóa/giá trị xen kẽ: D("t", "reply", "id", 5)
        internal static Dictionary<string, object> D(params object[] pairs)
        {
            Dictionary<string, object> map = new Dictionary<string, object>();
            for (int i = 0; i + 1 < pairs.Length; i += 2) map[(string)pairs[i]] = pairs[i + 1];
            return map;
        }

        internal static string Str(Dictionary<string, object> map, string key)
        {
            object value;
            if (map != null && map.TryGetValue(key, out value) && value != null) return Convert.ToString(value);
            return null;
        }

        internal static string Str(Dictionary<string, object> map, string key, string fallback)
        {
            string value = Str(map, key);
            return value == null ? fallback : value;
        }

        internal static bool Has(Dictionary<string, object> map, string key)
        {
            return map != null && map.ContainsKey(key);
        }

        internal static bool Bool(Dictionary<string, object> map, string key, bool fallback)
        {
            object value;
            if (map == null || !map.TryGetValue(key, out value) || value == null) return fallback;
            if (value is bool) return (bool)value;
            string text = Convert.ToString(value).ToLowerInvariant();
            if (text == "true" || text == "1") return true;
            if (text == "false" || text == "0") return false;
            return fallback;
        }

        internal static int Int(Dictionary<string, object> map, string key, int fallback)
        {
            object value;
            if (map == null || !map.TryGetValue(key, out value) || value == null) return fallback;
            try { return Convert.ToInt32(value); }
            catch (FormatException) { return fallback; }
            catch (OverflowException) { return fallback; }
            catch (InvalidCastException) { return fallback; }
        }

        internal static double Num(Dictionary<string, object> map, string key, double fallback)
        {
            object value;
            if (map == null || !map.TryGetValue(key, out value) || value == null) return fallback;
            try { return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture); }
            catch (FormatException) { return fallback; }
            catch (OverflowException) { return fallback; }
            catch (InvalidCastException) { return fallback; }
        }

        internal static Dictionary<string, object> Obj(Dictionary<string, object> map, string key)
        {
            object value;
            if (map != null && map.TryGetValue(key, out value)) return value as Dictionary<string, object>;
            return null;
        }

        internal static List<object> Arr(Dictionary<string, object> map, string key)
        {
            object value;
            List<object> list = new List<object>();
            if (map == null || !map.TryGetValue(key, out value) || value == null || value is string) return list;
            IEnumerable items = value as IEnumerable;
            if (items == null) return list;
            foreach (object item in items) list.Add(item);
            return list;
        }
    }
}
