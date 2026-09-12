using System.Collections.Generic;
using AfterSeoul.Core;
using UnityEngine;

namespace AfterSeoul.Unity.UI
{
    /// <summary>
    /// 아이템군 아이콘 — <b>코드로 그린다.</b>
    ///
    /// <para>GDD §13 은 "개별 생성 금지, 반드시 세트 단위로 스타일 통일"을 요구한다. 그림을 열세 장
    /// 따로 그리면 선 두께와 여백이 반드시 어긋나는데, 한 함수가 같은 붓으로 다 그리면
    /// <b>통일은 지키려고 애쓰는 것이 아니라 구조상 어길 수가 없는 것</b>이 된다.
    /// 소리를 파일 없이 파형으로 만든 것(<see cref="Sfx"/>)과 같은 이유다 —
    /// 1인 개발에서 에셋 파이프라인은 그 자체로 비용이다.</para>
    ///
    /// <para>나중에 진짜 라인 아이콘 세트가 생기면 <see cref="For"/> 안만 바꾸면 된다.
    /// 화면들은 아이템군만 알지 그림이 어디서 오는지는 모른다.</para>
    ///
    /// <para>작은 화면에서 실루엣이 살아야 하므로(GDD §13) 형태를 과감하게 줄였다.
    /// 64px 안에서 알아볼 수 있는 건 굵은 선 서너 개뿐이다.</para>
    /// </summary>
    public static class IconSet
    {
        private const int Size = 64;
        private const int Stroke = 4;

        private static readonly Dictionary<ItemGroup, Sprite> _cache = new Dictionary<ItemGroup, Sprite>();

        /// <summary>아이템군 색. 목록을 색으로도 훑을 수 있게 — 모양만으로는 곁눈질이 안 된다.</summary>
        public static Color ColorOf(ItemGroup group)
        {
            switch (group)
            {
                case ItemGroup.Weapon: return Theme.Danger;
                case ItemGroup.Ammo: return Theme.Warn;
                case ItemGroup.Medical: return Theme.Safe;
                case ItemGroup.Battery: return Theme.Info;
                case ItemGroup.Electronics: return Theme.Info;
                case ItemGroup.Metal: return Theme.TextDim;
                case ItemGroup.Bolt: return Theme.TextDim;
                case ItemGroup.Food: return Theme.Accent;
                case ItemGroup.Tool: return Theme.TextDim;
                case ItemGroup.Armor: return Theme.Accent;
                case ItemGroup.Document: return Theme.Text;
                case ItemGroup.Key: return Theme.Warn;
                default: return Theme.TextFaint;
            }
        }

        public static Sprite For(ItemGroup group)
        {
            Sprite cached;
            if (_cache.TryGetValue(group, out cached) && cached != null) return cached;

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[Size * Size];   // 전부 투명으로 시작 (GDD §13: 투명 배경)
            Draw(group, pixels);

            tex.SetPixels32(pixels);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f));
            _cache[group] = sprite;
            return sprite;
        }

        public static Sprite For(ItemDef def) => For(ItemGroups.Of(def));

        // ── 탭 ───────────────────────────────────────────────────

        /// <summary>하단 탭의 그림. 다섯 개가 글자만으로 폭을 나눠 가지면 어디가 어딘지 안 보인다.</summary>
        public enum TabGlyph { Home, Factory, Expedition, People, Warehouse }

        private static readonly Dictionary<TabGlyph, Sprite> _tabCache = new Dictionary<TabGlyph, Sprite>();

        public static Sprite ForTab(TabGlyph glyph)
        {
            Sprite cached;
            if (_tabCache.TryGetValue(glyph, out cached) && cached != null) return cached;

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            var p = new Color32[Size * Size];
            DrawTab(glyph, p);

            tex.SetPixels32(p);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f));
            _tabCache[glyph] = sprite;
            return sprite;
        }

        private static void DrawTab(TabGlyph glyph, Color32[] p)
        {
            switch (glyph)
            {
                case TabGlyph.Factory:
                    // 공장 — 톱니 지붕과 굴뚝.
                    Line(p, 10, 50, 10, 34);
                    Line(p, 10, 34, 22, 42);
                    Line(p, 22, 42, 22, 34);
                    Line(p, 22, 34, 34, 42);
                    Line(p, 34, 42, 34, 34);
                    Line(p, 34, 34, 46, 42);
                    Line(p, 46, 42, 46, 50);
                    Line(p, 46, 50, 10, 50);
                    Line(p, 50, 50, 50, 18);   // 굴뚝
                    Line(p, 50, 18, 56, 18);
                    break;

                case TabGlyph.Expedition:
                    // 탐색 — 지도 핀.
                    Circle(p, 32, 26, 12);
                    Line(p, 22, 34, 32, 52);
                    Line(p, 42, 34, 32, 52);
                    break;

                case TabGlyph.People:
                    // 인원 — 머리와 어깨 둘.
                    Circle(p, 24, 22, 8);
                    Line(p, 12, 48, 14, 38);
                    Line(p, 36, 48, 34, 38);
                    Line(p, 14, 38, 34, 38);
                    Circle(p, 44, 26, 6);
                    Line(p, 36, 48, 38, 40);
                    Line(p, 52, 48, 50, 40);
                    Line(p, 38, 40, 50, 40);
                    break;

                case TabGlyph.Warehouse:
                    // 창고 — 상자. 뚜껑 띠가 있어야 '보관'으로 읽힌다.
                    Box(p, 12, 20, 52, 50);
                    Line(p, 12, 30, 52, 30);
                    Line(p, 28, 20, 28, 30);
                    Line(p, 36, 20, 36, 30);
                    break;

                default:
                    // 홈 — 지붕과 벽.
                    Line(p, 12, 32, 32, 14);
                    Line(p, 32, 14, 52, 32);
                    Line(p, 18, 30, 18, 52);
                    Line(p, 46, 30, 46, 52);
                    Line(p, 18, 52, 46, 52);
                    break;
            }
        }

        // ── 열세 장 ──────────────────────────────────────────────

        private static void Draw(ItemGroup group, Color32[] p)
        {
            switch (group)
            {
                case ItemGroup.Weapon:
                    // 총열 + 탄창 + 개머리판. 가로로 긴 실루엣이 총이라는 건 작아도 읽힌다.
                    Line(p, 10, 34, 54, 34);
                    Line(p, 26, 34, 26, 46);
                    Line(p, 26, 46, 32, 46);
                    Line(p, 10, 34, 16, 24);
                    break;

                case ItemGroup.Ammo:
                    // 탄피 셋. 하나보다 여럿이 '탄약'으로 읽힌다.
                    for (int i = 0; i < 3; i++)
                    {
                        int x = 16 + i * 16;
                        Box(p, x - 5, 26, x + 5, 50);
                        Line(p, x - 5, 26, x, 14);
                        Line(p, x + 5, 26, x, 14);
                    }
                    break;

                case ItemGroup.Medical:
                    Line(p, 32, 14, 32, 50);
                    Line(p, 14, 32, 50, 32);
                    break;

                case ItemGroup.Battery:
                    Box(p, 16, 18, 48, 52);
                    Line(p, 27, 12, 37, 12);   // 단자
                    Line(p, 27, 12, 27, 18);
                    Line(p, 37, 12, 37, 18);
                    Line(p, 24, 38, 40, 38);   // 잔량 선
                    break;

                case ItemGroup.Electronics:
                    Box(p, 20, 20, 44, 44);
                    for (int i = 0; i < 3; i++)
                    {
                        int y = 25 + i * 7;
                        Line(p, 12, y, 20, y);
                        Line(p, 44, y, 52, y);
                    }
                    break;

                case ItemGroup.Metal:
                    // 잉곳. 위가 좁은 사다리꼴.
                    Line(p, 20, 24, 44, 24);
                    Line(p, 44, 24, 52, 44);
                    Line(p, 52, 44, 12, 44);
                    Line(p, 12, 44, 20, 24);
                    break;

                case ItemGroup.Bolt:
                    Hexagon(p, 32, 22, 12);
                    Box(p, 27, 34, 37, 54);
                    Line(p, 27, 42, 37, 42);
                    Line(p, 27, 48, 37, 48);
                    break;

                case ItemGroup.Food:
                    // 통조림. 위에 타원, 몸통은 상자.
                    Ellipse(p, 32, 20, 14, 6);
                    Line(p, 18, 20, 18, 48);
                    Line(p, 46, 20, 46, 48);
                    Ellipse(p, 32, 48, 14, 6);
                    break;

                case ItemGroup.Tool:
                    // 스패너. 벌어진 머리 + 자루.
                    Line(p, 20, 16, 20, 28);
                    Line(p, 32, 16, 32, 28);
                    Line(p, 20, 28, 32, 28);
                    Line(p, 26, 28, 26, 52);
                    break;

                case ItemGroup.Armor:
                    // 방패. 위는 각지고 아래는 뾰족하게.
                    Line(p, 16, 16, 48, 16);
                    Line(p, 16, 16, 16, 34);
                    Line(p, 48, 16, 48, 34);
                    Line(p, 16, 34, 32, 52);
                    Line(p, 48, 34, 32, 52);
                    break;

                case ItemGroup.Document:
                    // 문서. 접힌 모서리 + 글줄 두 개.
                    Line(p, 18, 12, 40, 12);
                    Line(p, 18, 12, 18, 52);
                    Line(p, 18, 52, 46, 52);
                    Line(p, 46, 52, 46, 20);
                    Line(p, 40, 12, 46, 20);
                    Line(p, 24, 30, 40, 30);
                    Line(p, 24, 38, 40, 38);
                    break;

                case ItemGroup.Key:
                    Circle(p, 22, 24, 9);
                    Line(p, 28, 30, 48, 50);
                    Line(p, 42, 44, 36, 50);
                    Line(p, 48, 50, 42, 56);
                    break;

                default:
                    // 부품 — 육각 너트. 분류가 안 되는 것들의 자리라 중립적인 모양을 쓴다.
                    Hexagon(p, 32, 32, 18);
                    Circle(p, 32, 32, 8);
                    break;
            }
        }

        // ── 붓 ───────────────────────────────────────────────────
        //
        // 전부 같은 굵기(Stroke)를 쓴다. 굵기를 인자로 받으면 언젠가 한 장만 다른 값이 들어간다.

        private static void Plot(Color32[] p, int x, int y)
        {
            int half = Stroke / 2;
            for (int dy = -half; dy <= half; dy++)
                for (int dx = -half; dx <= half; dx++)
                {
                    int px = x + dx, py = y + dy;
                    if (px < 0 || px >= Size || py < 0 || py >= Size) continue;
                    p[py * Size + px] = new Color32(255, 255, 255, 255);
                }
        }

        private static void Line(Color32[] p, int x0, int y0, int x1, int y1)
        {
            // 브레젠험. 세로로 뒤집어 그린다 — 텍스처의 0 은 아래쪽인데
            // 위 좌표들은 화면처럼 위에서 아래로 읽는 게 그리기 편하다.
            y0 = Size - 1 - y0;
            y1 = Size - 1 - y1;

            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;

            while (true)
            {
                Plot(p, x0, y0);
                if (x0 == x1 && y0 == y1) break;

                int e2 = err * 2;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        private static void Box(Color32[] p, int left, int top, int right, int bottom)
        {
            Line(p, left, top, right, top);
            Line(p, right, top, right, bottom);
            Line(p, right, bottom, left, bottom);
            Line(p, left, bottom, left, top);
        }

        private static void Circle(Color32[] p, int cx, int cy, int r) => Ellipse(p, cx, cy, r, r);

        private static void Ellipse(Color32[] p, int cx, int cy, int rx, int ry)
        {
            const int Steps = 48;
            int px = cx + rx, py = cy;

            for (int i = 1; i <= Steps; i++)
            {
                double t = i * 2.0 * Mathf.PI / Steps;
                int nx = cx + (int)System.Math.Round(rx * System.Math.Cos(t));
                int ny = cy + (int)System.Math.Round(ry * System.Math.Sin(t));
                Line(p, px, py, nx, ny);
                px = nx; py = ny;
            }
        }

        private static void Hexagon(Color32[] p, int cx, int cy, int r)
        {
            int px = 0, py = 0;
            for (int i = 0; i <= 6; i++)
            {
                double t = i * System.Math.PI / 3.0 + System.Math.PI / 6.0;
                int nx = cx + (int)System.Math.Round(r * System.Math.Cos(t));
                int ny = cy + (int)System.Math.Round(r * System.Math.Sin(t));
                if (i > 0) Line(p, px, py, nx, ny);
                px = nx; py = ny;
            }
        }
    }
}
