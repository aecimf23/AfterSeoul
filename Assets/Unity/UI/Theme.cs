using UnityEngine;

namespace AfterSeoul.Unity.UI
{
    /// <summary>
    /// 색·치수·폰트를 한곳에 모은다. GDD §26~27 의 "현장 관리 단말기" 컨셉.
    ///
    /// <para>어두운 회색 바탕 / 청회색 패널 / 군용 녹색 포인트 / 상태 4색.
    /// 화면 코드에 색 리터럴을 박지 않는다 — 나중에 톤을 한 번에 바꿔야 한다.</para>
    /// </summary>
    public static class Theme
    {
        // ── 바탕 ──
        public static Color Bg => Get(0);        // 앱 바탕
        public static Color Panel => Get(1);     // 카드/패널
        public static Color PanelAlt => Get(2);  // 목록 줄 교차색
        public static Color Line => Get(3);      // 구분선

        // ── 글자 ──
        public static Color Text => Get(4);
        public static Color TextDim => Get(5);
        public static Color TextFaint => Get(6);

        // ── 포인트 ──
        public static Color Accent => Get(7);      // 군용 녹색
        public static Color AccentDim => Get(8);

        // ── 상태 4색 (GDD §27) ──
        public static readonly Color Safe = Hex("6FAE5A");
        public static readonly Color Warn = Hex("D6B44A");
        public static readonly Color Danger = Hex("C5544A");
        public static Color Info => Get(9);

        // ── 가장자리 (GDD §26 현장 단말기) ──
        //
        // 색이 아니라 가장자리가 "장비 안에 든 화면"을 만든다. 패널 색과 거의 같되 아주 조금
        // 밝은 선 하나면 충분하고, 그보다 세면 만화가 된다.
        public static Color Edge => Get(10);

        /// <summary>지금 보고 있는 것 / 진행 중인 것의 테두리.</summary>
        public static Color EdgeLive => Get(11);

        /// <summary>진행 막대의 빈 부분. 패널보다 어두워야 "아직 안 찬 것"으로 읽힌다.</summary>
        public static Color BarTrack => Get(12);

        /// <summary>창·연출 뒤에 까는 어둠.</summary>
        public static readonly Color Scrim = new Color(0.02f, 0.03f, 0.04f, 0.86f);


        private const string ThemeKey = "AfterSeoul.UI.Theme";
        private static string _id;
        private static Color[] _palette;
        public static event System.Action<Color[], Color[]> Changed;
        public static string Id { get { Ensure(); return _id; } }
        public static readonly string[] Ids = { "night", "military", "shelter" };
        public static string Name(string id) => id == "military" ? "군용 단말기" : id == "shelter" ? "낡은 피난처" : "서울의 밤";
        public static string Description(string id) => id == "military" ? "검은 장비 · 녹색 작전도" : id == "shelter" ? "따뜻한 등불 · 바랜 기록" : "남색 도시 · 호박색 불빛";
        private static void Ensure() { if (_palette == null) Reload(); }
        private static Color Get(int index) { Ensure(); return _palette[index]; }
        public static Color[] Palette(string id)
        {
            string values = id == "military"
                ? "080D0F 10191C 172327 2B4044 DDE9E5 9BB0B1 778F91 82BD92 285044 56CDCC 304D50 447E6C 0B1215"
                : id == "shelter"
                ? "171411 24201B 302A23 514638 F1E6D1 C5B59B A09078 E9BA79 695034 94BCB0 665440 B78B59 100E0C"
                : "0B1220 131F30 1C2B40 34475D E9EEF5 A8B8CC 8194AD EABC78 634A30 77CADB 3C5670 B68B53 080F1B";
            return System.Array.ConvertAll(values.Split(' '), Hex);
        }
        public static void Reload()
        {
            var id = PlayerPrefs.GetString(ThemeKey, "night");
            if (System.Array.IndexOf(Ids, id) < 0) id = "night";
            var old = _palette;
            _id = id; _palette = Palette(id);
            if (old != null) Changed?.Invoke(old, _palette);
        }
        public static void Select(string id)
        {
            if (System.Array.IndexOf(Ids, id) < 0) return;
            Tween.CompleteTints();
            PlayerPrefs.SetString(ThemeKey, id);
            PlayerPrefs.Save();
            Reload();
        }
        public static void ApplyTo(Transform root, Color[] before, Color[] after)
        {
            foreach (var graphic in root.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            {
                var c = graphic.color;
                for (int i = 0; i < before.Length; i++)
                {
                    if (Mathf.Abs(c.r - before[i].r) > .001f || Mathf.Abs(c.g - before[i].g) > .001f || Mathf.Abs(c.b - before[i].b) > .001f) continue;
                    var replacement = after[i]; replacement.a = c.a; graphic.color = replacement; break;
                }
                graphic.SetVerticesDirty();
            }
        }

        // ── 치수 (1080x1920 기준) ──
        public const int FontTitle = 46;
        public const int FontHeading = 38;
        public const int FontBody = 32;
        public const int FontSmall = 27;
        public const int FontTab = 26;

        public const float TabBarHeight = 150f;
        public const float HeaderHeight = 154f;
        public const float Gutter = 28f;
        public const float RowHeight = 108f;

        public static readonly Vector2 ReferenceResolution = new Vector2(1080f, 1920f);

        private static UnityEngine.Font _font;

        /// <summary>
        /// 원작과 같은 D2Coding을 앱에 포함해 한글과 고정폭 조판을 유지한다.
        /// 포함된 리소스를 읽지 못했을 때만 OS 폰트와 Unity 기본 폰트로 대체한다.
        /// </summary>
        public static UnityEngine.Font Font
        {
            get
            {
                if (_font != null) return _font;

                // 원작의 고정폭 한글 글꼴을 포함해 기기의 OS 폰트에 의존하지 않는다.
                _font = Resources.Load<UnityEngine.Font>("Fonts/D2Coding");
                if (_font != null) return _font;

                // Windows / Android / macOS 에서 흔한 한글 폰트 순서대로.
                _font = UnityEngine.Font.CreateDynamicFontFromOSFont(
                    new[]
                    {
                        "Malgun Gothic", "맑은 고딕",
                        "Noto Sans CJK KR", "Noto Sans KR",
                        "NanumGothic", "나눔고딕",
                        "Apple SD Gothic Neo",
                        "Roboto", "Arial Unicode MS",
                    },
                    FontBody);

                if (_font == null)
                    _font = Resources.GetBuiltinResource<UnityEngine.Font>("LegacyRuntime.ttf");

                return _font;
            }
        }

        /// <summary>품질 4단계 색. 실패는 빨강, 우수는 녹색.</summary>
        public static Color QualityColor(Core.CraftQuality q)
        {
            switch (q)
            {
                case Core.CraftQuality.Failed: return Danger;
                case Core.CraftQuality.Normal: return TextDim;
                case Core.CraftQuality.Good: return Info;
                default: return Safe;
            }
        }

        public static string QualityLabel(Core.CraftQuality q)
        {
            switch (q)
            {
                case Core.CraftQuality.Failed: return "실패";
                case Core.CraftQuality.Normal: return "보통";
                case Core.CraftQuality.Good: return "양호";
                default: return "우수";
            }
        }

        /// <summary>위험도 ★ 표기. 지역 카드에 쓴다.</summary>
        public static string RiskStars(int riskLevel)
        {
            if (riskLevel < 1) riskLevel = 1;
            if (riskLevel > 5) riskLevel = 5;
            return new string('★', riskLevel) + new string('☆', 5 - riskLevel);
        }

        public static Color RiskColor(int riskLevel)
        {
            if (riskLevel <= 1) return Safe;
            if (riskLevel == 2) return Warn;
            return Danger;
        }

        /// <summary>₩1,234,000 형식. 게임 화폐 표기는 전부 이걸로 통일한다.</summary>
        public static string Won(long amount) => "₩" + amount.ToString("N0");

        private static Color Hex(string rgb)
        {
            return new Color(
                System.Convert.ToInt32(rgb.Substring(0, 2), 16) / 255f,
                System.Convert.ToInt32(rgb.Substring(2, 2), 16) / 255f,
                System.Convert.ToInt32(rgb.Substring(4, 2), 16) / 255f,
                1f);
        }
    }
}
