using System.Collections.Generic;
using Newtonsoft.Json;

namespace AfterSeoul.Core
{
    /// <summary>
    /// 로케일 조회. 본편 <c>EscapeFromConsole.Loc</c> 과 같은 모양이다 —
    /// <c>Get(key, args)</c>, 현재 언어에 없으면 en, 그래도 없으면 키를 그대로 돌려준다.
    /// 키 규칙도 본편 그대로 쓴다 (<c>ITEM_{Id}_NAME</c> 등, MAINLINE_REFERENCE §5).
    ///
    /// <para>본편과 다른 점: 파일을 직접 읽지 않는다. <c>Resources.Load</c> 는 Unity 레이어가 하고
    /// 여기엔 텍스트만 넘긴다 (R1). 게임 상태가 아니라 표시용 테이블이라 static 으로 둔다.</para>
    /// </summary>
    public static class Loc
    {
        public const string FallbackLanguage = "en";

        private static Dictionary<string, string> _dict = new Dictionary<string, string>();
        private static Dictionary<string, string> _fallback = new Dictionary<string, string>();

        public static string CurrentLanguage { get; private set; } = FallbackLanguage;

        /// <param name="json">현재 언어 테이블</param>
        /// <param name="fallbackJson">en 테이블. 없으면 null.</param>
        public static void Load(string language, string json, string fallbackJson)
        {
            CurrentLanguage = language;
            _dict = ParseTable(json);
            _fallback = ParseTable(fallbackJson);
        }

        public static bool Has(string key) => _dict.ContainsKey(key) || _fallback.ContainsKey(key);

        public static string Get(string key, params object[] args)
        {
            if (!_dict.TryGetValue(key, out var text) && !_fallback.TryGetValue(key, out text))
                return key;

            // 본편 로케일은 줄바꿈을 "\n" 두 글자로 저장한다.
            text = text.Replace("\n", "\n");
            return args != null && args.Length > 0 ? string.Format(text, args) : text;
        }

        public static string ItemName(string itemId) => Get("ITEM_" + itemId + "_NAME");
        public static string MapName(string mapId) => Get("MAP_" + mapId + "_NAME");
        public static string TraderName(string npcId) => Get("TRADER_" + npcId + "_NAME");

        private static Dictionary<string, string> ParseTable(string json)
        {
            if (string.IsNullOrEmpty(json)) return new Dictionary<string, string>();
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
    }
}
