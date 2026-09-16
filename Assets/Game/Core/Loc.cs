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

        /// <param name="json">현재 언어 테이블 (본편에서 추출한 것)</param>
        /// <param name="fallbackJson">en 테이블. 없으면 null.</param>
        /// <param name="mobileJson">
        /// 모바일 전용 문구 덧칠. 본편에 없는 말(황 상사의 의뢰 대사 등)이 여기 있다.
        ///
        /// <para>파일을 나눠 두는 이유는 <c>Locales/*.json</c> 을 추출 스크립트가 통째로 덮어쓰기
        /// 때문이다. 같은 파일에 적으면 본편 데이터를 다시 뽑는 순간 조용히 사라진다.</para>
        /// </param>
        /// <param name="mobileFallbackJson">모바일 en 덧칠.</param>
        public static void Load(string language, string json, string fallbackJson,
            string mobileJson = null, string mobileFallbackJson = null)
        {
            CurrentLanguage = language;
            _dict = ParseTable(json);
            _fallback = ParseTable(fallbackJson);

            Overlay(_dict, mobileJson);
            Overlay(_fallback, mobileFallbackJson);
        }

        /// <summary>덧칠이 이긴다 — 같은 키가 있으면 모바일 쪽 문구를 쓴다.</summary>
        private static void Overlay(Dictionary<string, string> table, string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            foreach (var pair in ParseTable(json)) table[pair.Key] = pair.Value;
        }

        public static bool Has(string key) => _dict.ContainsKey(key) || _fallback.ContainsKey(key);

        public static string Get(string key, params object[] args)
        {
            if (!_dict.TryGetValue(key, out var text) && !_fallback.TryGetValue(key, out text))
                return key;

            // 본편 로케일은 줄바꿈을 역슬래시+n 두 글자로 저장한다. 지금 필터된 파일에는
            // 남아 있지 않지만 본편 문구가 바뀌면 언제든 다시 들어온다.
            // (예전에는 이 자리가 Replace("\n", "\n") 였다 — 아무 일도 하지 않는 코드였다.)
            text = text.Replace("\\n", "\n");
            return args != null && args.Length > 0 ? string.Format(text, args) : text;
        }

        /// <summary>Mobile UI uses its Korean source as a stable translation key.</summary>
        public static string Text(string source, params object[] args)
        {
            if (source == null) return "";
            string text;
            if (!_dict.TryGetValue(source, out text))
            {
                if (CurrentLanguage == "ko" || !_fallback.TryGetValue(source, out text)) text = source;
            }
            text = text.Replace("\\n", "\n");
            return args != null && args.Length > 0 ? string.Format(text, args) : text;
        }

        public static string ItemName(string itemId) => Get("ITEM_" + itemId + "_NAME");
        public static string MapName(string mapId) => Get("MAP_" + mapId + "_NAME");
        public static string TraderName(string npcId) => Get("TRADER_" + npcId + "_NAME");

        /// <summary>의뢰를 받을 때 고용주가 하는 말. 없으면 빈 문자열 — 화면이 키를 그대로 띄우면 안 된다.</summary>
        public static string QuestAccept(string questId) => Line(questId + "_ACCEPT");

        /// <summary>납품했을 때의 대꾸.</summary>
        public static string QuestComplete(string questId) => Line(questId + "_COMPLETE");

        /// <summary>대사 전용 조회. 대사는 없을 수 있고, 없을 때 키가 보이는 건 물건 이름보다 나쁘다.</summary>
        private static string Line(string key) => Has(key) ? Get(key) : "";

        private static Dictionary<string, string> ParseTable(string json)
        {
            if (string.IsNullOrEmpty(json)) return new Dictionary<string, string>();
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
    }
}
