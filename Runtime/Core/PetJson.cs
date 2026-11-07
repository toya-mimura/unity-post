using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PetPost
{
    /// <summary>
    /// 日付文字列を勝手に DateTime に変換させないための JSON 読み込みヘルパー。
    /// JSON helper that keeps date strings as strings (Newtonsoft converts them by default).
    /// </summary>
    internal static class PetJson
    {
        public static JObject Parse(string json)
        {
            using var sr = new StringReader(json);
            using var reader = new JsonTextReader(sr) { DateParseHandling = DateParseHandling.None };
            return JObject.Load(reader);
        }

        public static string Str(JToken token, string key)
        {
            var v = token?[key];
            return v == null || v.Type == JTokenType.Null ? null : v.ToString();
        }
    }
}
