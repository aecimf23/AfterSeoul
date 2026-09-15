using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    /// <summary>Small vector scene: decorative only, never consumes the game's RNG.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ThemeScene : MaskableGraphic
    {
        public string PreviewThemeId;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var id = string.IsNullOrEmpty(PreviewThemeId) ? Theme.Id : PreviewThemeId;
            var p = Theme.Palette(id);
            Quad(vh, 0, 0, 1, 1, p[0]);
            if (id == "military")
            {
                for (int i = 1; i < 10; i++)
                {
                    Line(vh, i / 10f, 0, i / 10f, 1, .002f, Alpha(p[9], .18f));
                    Line(vh, 0, i / 10f, 1, i / 10f, .003f, Alpha(p[9], .18f));
                }
                Line(vh, 0, .25f, .4f, .42f, .025f, Alpha(p[9], .45f));
                Line(vh, .4f, .42f, 1, .3f, .025f, Alpha(p[9], .45f));
                float[] xs = { .18f, .42f, .65f, .84f }, ys = { .55f, .7f, .52f, .8f };
                for (int i = 0; i < 4; i++)
                {
                    Line(vh, .18f, .55f, xs[i], ys[i], .003f, p[7]);
                    Quad(vh, xs[i] - .012f, ys[i] - .025f, .024f, .05f, p[7]);
                }
                return;
            }
            // Bands of mist, a ridge, and apartment silhouettes.
            for (int i = 0; i < 18; i++)
                Quad(vh, 0, i / 18f, 1, 1f / 18f, Color.Lerp(p[3], p[0], i / 17f));
            Triangle(vh, new Vector2(0,.24f), new Vector2(.5f,.63f), new Vector2(1,.24f), p[1]);
            Triangle(vh, new Vector2(.3f,.22f), new Vector2(.78f,.51f), new Vector2(1,.22f), p[2]);
            // Namsan tower silhouette.
            Quad(vh, .496f, .57f, .014f, .24f, p[5]);
            Quad(vh, .475f, .70f, .055f, .046f, p[7]);
            Quad(vh, .501f, .81f, .004f, .07f, p[7]);
            for (int i = 0; i < 13; i++)
            {
                float x = i / 13f, h = .16f + ((i * 7) % 5) * .035f;
                Quad(vh, x, .12f, .065f, h, i % 2 == 0 ? p[0] : p[1]);
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 3; c++)
                        if ((i * 13 + r * 3 + c) % 4 == 0)
                            Quad(vh, x + .009f + c * .017f, .14f + r * .037f, .006f, .012f, Alpha(p[7], .75f));
            }
            Quad(vh, 0, 0, 1, .12f, p[0]);
            Line(vh, 0,.065f,1,.10f,.004f,Alpha(p[9],.7f));
            if (id == "shelter")
            {
                // Looking out through an old shelter window beneath a warm lamp.
                Quad(vh, 0, 0, .035f, 1, p[8]); Quad(vh, .965f, 0, .035f, 1, p[8]);
                Quad(vh, 0, .95f, 1, .05f, p[8]); Quad(vh, 0, 0, 1, .05f, p[8]);
                Quad(vh, .48f, 0, .024f, 1, p[8]); Quad(vh, 0, .44f, 1, .025f, p[8]);
                Line(vh, .86f,1,.86f,.85f,.008f,p[5]);
                Triangle(vh,new Vector2(.79f,.79f),new Vector2(.86f,.87f),new Vector2(.93f,.79f),p[7]);
                Quad(vh,.8f,.77f,.12f,.01f,p[4]);
            }
            else
            {
                // Rain marks and distant lights; static to avoid distracting motion.
                for (int i = 0; i < 20; i++)
                {
                    float x = ((i * 17) % 29) / 29f, y = .35f + ((i * 11) % 17) / 28f;
                    Line(vh,x,y,x-.015f,y-.045f,.0015f,Alpha(p[9],.22f));
                }
            }
        }
        private static Color Alpha(Color c, float a) { c.a = a; return c; }
        private Vector2 Point(Vector2 p) => rectTransform.rect.min + Vector2.Scale(p, rectTransform.rect.size);
        private void Quad(VertexHelper vh,float x,float y,float w,float h,Color c)
        {
            int n=vh.currentVertCount;
            vh.AddVert(Point(new Vector2(x,y)),c,Vector2.zero);
            vh.AddVert(Point(new Vector2(x,y+h)),c,Vector2.zero);
            vh.AddVert(Point(new Vector2(x+w,y+h)),c,Vector2.zero);
            vh.AddVert(Point(new Vector2(x+w,y)),c,Vector2.zero);
            vh.AddTriangle(n,n+1,n+2); vh.AddTriangle(n,n+2,n+3);
        }
        private void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color tint)
        {
            int n=vh.currentVertCount;
            vh.AddVert(Point(a),tint,Vector2.zero); vh.AddVert(Point(b),tint,Vector2.zero); vh.AddVert(Point(c),tint,Vector2.zero);
            vh.AddTriangle(n,n+1,n+2);
        }
        private void Line(VertexHelper vh,float x,float y,float x2,float y2,float width,Color c)
        {
            var a=new Vector2(x,y); var b=new Vector2(x2,y2);
            var d=new Vector2(-(b-a).y,(b-a).x).normalized*width*.5f;
            Triangle(vh,a-d,a+d,b+d,c); Triangle(vh,a-d,b+d,b-d,c);
        }
    }
}
