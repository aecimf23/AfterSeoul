using UnityEngine;

namespace AfterSeoul.Unity.UI
{
    /// <summary>
    /// 패널·테두리·질감을 코드로 굽는다. <see cref="IconSet"/> 이 아이콘에 하는 일을 배경에 한다.
    ///
    /// <para><b>왜 그림 파일이 아닌가:</b> 9-슬라이스 스프라이트 한 장을 쓰려면 png 를 만들고
    /// 임포터에서 border 를 손으로 찍어야 한다. 그 값은 메타 파일 안에 숨고, 나중에
    /// "모서리가 왜 이렇게 뭉개지지"의 원인이 코드 어디에도 없게 된다. 여기서는 반지름과
    /// 선 두께가 그냥 상수다.</para>
    ///
    /// <para><b>왜 둥근 모서리인가 (GDD §26 현장 단말기):</b> 직각 사각형만 쌓으면 웹페이지처럼
    /// 보인다. 아주 작은 반지름과 머리카락 굵기의 테두리가 붙는 순간 "케이스 안에 든 물건"으로
    /// 읽힌다 — 화면이 아니라 장비로 보이게 하는 건 색이 아니라 가장자리다.</para>
    /// </summary>
    public static class Skin
    {
        /// <summary>패널 모서리 반지름(px, 1080 기준).</summary>
        public const float PanelRadius = 3f;

        /// <summary>테두리 굵기. 1px 는 고해상도에서 사라지고 4px 는 만화가 된다.</summary>
        public const float StrokeWidth = 2.5f;

        private static Sprite _panel;
        private static Sprite _panelEdge;
        private static Sprite _pill;
        private static Sprite _pillEdge;
        private static Sprite _corner;
        private static Texture2D _vignette;
        private static Texture2D _grid;

        /// <summary>둥근 사각형(채움). 카드·버튼 바탕.</summary>
        public static Sprite Panel
        {
            get
            {
                if (_panel == null) _panel = Build(64, 64, PanelRadius, 0f, "SkinPanel");
                return _panel;
            }
        }

        /// <summary>둥근 사각형(테두리만). <see cref="Panel"/> 위에 겹쳐 올린다.</summary>
        public static Sprite PanelEdge
        {
            get
            {
                if (_panelEdge == null) _panelEdge = Build(64, 64, PanelRadius, StrokeWidth, "SkinPanelEdge");
                return _panelEdge;
            }
        }

        /// <summary>반지름이 높이의 절반인 캡슐. 진행 막대·배지.</summary>
        public static Sprite Pill
        {
            get
            {
                if (_pill == null) _pill = Build(48, 48, 24f, 0f, "SkinPill");
                return _pill;
            }
        }

        public static Sprite PillEdge
        {
            get
            {
                if (_pillEdge == null) _pillEdge = Build(48, 48, 24f, StrokeWidth, "SkinPillEdge");
                return _pillEdge;
            }
        }

        /// <summary>
        /// ㄱ 자 꺾쇠. 네 귀퉁이에 회전시켜 붙이면 조준경·계측기의 표시틀이 된다.
        ///
        /// <para>테두리를 네 변 다 그리면 상자가 되지만, 귀퉁이만 찍으면 "지금 이걸 보고 있다"는
        /// 표시가 된다. 창과 복귀 연출처럼 <b>한 번에 하나만 뜨는 것</b>에만 쓴다 — 목록의 모든
        /// 줄에 붙이면 그냥 시끄럽다.</para>
        /// </summary>
        public static Sprite Corner
        {
            get
            {
                if (_corner == null) _corner = BuildCorner(32, 3f, 14f);
                return _corner;
            }
        }

        /// <summary>
        /// 가장자리가 어두워지는 덮개. 화면 전체에 한 장 깐다.
        ///
        /// <para>타일링이 아니라 한 장을 늘려 쓴다 — 스캔라인처럼 반복하는 질감은 기기 해상도에
        /// 따라 물결(moiré)이 생기는데, 그건 감성이 아니라 그냥 고장처럼 보인다.</para>
        /// </summary>
        public static Texture2D Vignette
        {
            get
            {
                if (_vignette == null) _vignette = BuildVignette(64);
                return _vignette;
            }
        }

        /// <summary>헤더 띠에 까는 흐릿한 격자. 계측기 눈금 느낌.</summary>
        public static Texture2D Grid
        {
            get
            {
                if (_grid == null) _grid = BuildGrid(24);
                return _grid;
            }
        }

        // ── 굽기 ────────────────────────────────────────────────

        /// <param name="stroke">0 이면 채움, 0 보다 크면 그 굵기의 <b>안쪽</b> 테두리.</param>
        private static Sprite Build(int w, int h, float radius, float stroke, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float d = RoundedBoxDistance(x + 0.5f, y + 0.5f, w, h, radius);

                    // 경계에서 0.5px 폭으로 알파를 깎아 계단을 없앤다.
                    float a = stroke <= 0f
                        ? Mathf.Clamp01(0.5f - d)
                        : Mathf.Clamp01(stroke * 0.5f + 0.5f - Mathf.Abs(d + stroke * 0.5f));

                    pixels[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            // 두 번째 인자 = "CPU 사본을 버린다". 굽고 나면 다시 읽을 일이 없다.
            tex.Apply(false, true);

            // 9-슬라이스 경계는 모서리보다 넉넉해야 한다. 모자라면 늘릴 때 둥근 부분이 딸려 늘어난다.
            int border = Mathf.CeilToInt(radius) + 2;
            border = Mathf.Min(border, Mathf.Min(w, h) / 2 - 1);

            // FullRect 가 아니면 Sliced/Filled 가 동작하지 않는다 (Tight 는 사각형 밖을 잘라낸다).
            return Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        /// <summary>둥근 사각형까지의 부호 있는 거리. 음수면 안쪽.</summary>
        private static float RoundedBoxDistance(float x, float y, float w, float h, float r)
        {
            r = Mathf.Min(r, Mathf.Min(w, h) * 0.5f);

            float qx = Mathf.Abs(x - w * 0.5f) - (w * 0.5f - r);
            float qy = Mathf.Abs(y - h * 0.5f) - (h * 0.5f - r);

            float ox = Mathf.Max(qx, 0f);
            float oy = Mathf.Max(qy, 0f);

            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        /// <summary>왼쪽 아래를 향하는 ㄴ 자. 쓸 때 회전시켜 네 귀퉁이를 만든다.</summary>
        private static Sprite BuildCorner(int size, float stroke, float length)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SkinCorner",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f;
                    float py = y + 0.5f;

                    bool onLeft = px <= stroke && py <= length;
                    bool onBottom = py <= stroke && px <= length;

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(onLeft || onBottom ? 255 : 0));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect);
        }

        private static Texture2D BuildVignette(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SkinVignette",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[size * size];
            float half = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // 가운데에서 멀수록 어둡다. 네 귀퉁이가 가장 어둡고 한가운데는 완전히 투명.
                    float nx = (x + 0.5f - half) / half;
                    float ny = (y + 0.5f - half) / half;
                    float r = Mathf.Clamp01(Mathf.Sqrt(nx * nx + ny * ny) / 1.4142f);

                    // 4제곱. 가운데 넓은 영역은 손대지 않고 가장자리에서만 빠르게 어두워진다.
                    float a = r * r * r * r * 0.55f;
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(a * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D BuildGrid(int cell)
        {
            var tex = new Texture2D(cell, cell, TextureFormat.RGBA32, false)
            {
                name = "SkinGrid",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,   // 선이 흐려지면 격자가 얼룩으로 보인다
            };

            var pixels = new Color32[cell * cell];
            for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                    pixels[y * cell + x] = new Color32(255, 255, 255, (byte)(x == 0 || y == 0 ? 34 : 0));

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
