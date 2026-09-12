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
        public static readonly Color Bg = Hex("12151A");        // 앱 바탕
        public static readonly Color Panel = Hex("1B2129");     // 카드/패널
        public static readonly Color PanelAlt = Hex("232B35");  // 목록 줄 교차색
        public static readonly Color Line = Hex("2E3945");      // 구분선

        // ── 글자 ──
        public static readonly Color Text = Hex("D6DEE7");
        public static readonly Color TextDim = Hex("8996A5");
        public static readonly Color TextFaint = Hex("5E6B7A");

        // ── 포인트 ──
        public static readonly Color Accent = Hex("7A8F5C");      // 군용 녹색
        public static readonly Color AccentDim = Hex("49543A");

        // ── 상태 4색 (GDD §27) ──
        public static readonly Color Safe = Hex("6FAE5A");
        public static readonly Color Warn = Hex("D6B44A");
        public static readonly Color Danger = Hex("C5544A");
        public static readonly Color Info = Hex("4E97B5");

        // ── 가장자리 (GDD §26 현장 단말기) ──
        //
        // 색이 아니라 가장자리가 "장비 안에 든 화면"을 만든다. 패널 색과 거의 같되 아주 조금
        // 밝은 선 하나면 충분하고, 그보다 세면 만화가 된다.
        public static readonly Color Edge = Hex("323C48");

        /// <summary>지금 보고 있는 것 / 진행 중인 것의 테두리.</summary>
        public static readonly Color EdgeLive = Hex("4C5F44");

        /// <summary>진행 막대의 빈 부분. 패널보다 어두워야 "아직 안 찬 것"으로 읽힌다.</summary>
        public static readonly Color BarTrack = Hex("151A20");

        /// <summary>창·연출 뒤에 까는 어둠.</summary>
        public static readonly Color Scrim = new Color(0.02f, 0.03f, 0.04f, 0.86f);

        // ── 치수 (1080x1920 기준) ──
        public const int FontTitle = 46;
        public const int FontHeading = 38;
        public const int FontBody = 32;
        public const int FontSmall = 27;
        public const int FontTab = 26;

        public const float TabBarHeight = 150f;
        public const float HeaderHeight = 120f;
        public const float Gutter = 28f;
        public const float RowHeight = 108f;

        public static readonly Vector2 ReferenceResolution = new Vector2(1080f, 1920f);

        private static UnityEngine.Font _font;

        /// <summary>
        /// 한글이 나오는 폰트를 구한다.
        ///
        /// <para>TextMeshPro 를 쓰지 않는 이유: TMP 는 기본 폰트 에셋을 에디터에서 한 번
        /// 임포트해야 하고(Window → TextMeshPro → Import TMP Essential Resources),
        /// 그 기본 폰트에는 한글 글리프가 없어서 한글 폰트 에셋을 따로 만들어야 한다.
        /// P1 은 뼈대를 돌려보는 단계라 에셋 작업 없이 뜨는 쪽을 택했다.
        /// <b>P6 폴리싱에서 TMP + 한글 폰트 에셋으로 교체한다.</b></para>
        ///
        /// <para>OS 폰트를 먼저 찾는다. LegacyRuntime.ttf 에는 한글이 없어서
        /// 그것만 쓰면 네모가 뜬다.</para>
        /// </summary>
        public static UnityEngine.Font Font
        {
            get
            {
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
