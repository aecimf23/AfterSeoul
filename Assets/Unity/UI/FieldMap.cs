using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    /// <summary>서울 작전도의 개략도. 실제 이동 경로나 해금 상태를 나타내지 않는 배경 그래픽.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class FieldMap : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            for (int i = 1; i < 10; i++)
            {
                Line(vh, new Vector2(i / 10f, 0), new Vector2(i / 10f, 1), 1, new Color(0.2f, 0.4f, 0.4f, 0.19f));
                Line(vh, new Vector2(0, i / 10f), new Vector2(1, i / 10f), 1, new Color(0.2f, 0.4f, 0.4f, 0.19f));
            }
            var river = new[] { new Vector2(0, .34f), new Vector2(.23f, .23f), new Vector2(.45f, .4f), new Vector2(.68f, .31f), new Vector2(1, .51f) };
            for (int i = 1; i < river.Length; i++)
            {
                Line(vh, river[i - 1], river[i], 16, new Color(.1f, .48f, .5f, .16f));
                Line(vh, river[i - 1], river[i], 2, new Color(.28f, .8f, .8f, .65f));
            }
            // Region markers are real selectable buttons supplied by the screen.
            // Keeping decoration marker-free prevents locked locations leaking onto the map.
        }

        private void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color tint)
        {
            var rect = rectTransform.rect;
            a = rect.min + Vector2.Scale(a, rect.size);
            b = rect.min + Vector2.Scale(b, rect.size);
            var normal = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
            int start = vh.currentVertCount;
            vh.AddVert(a - normal, tint, Vector2.zero);
            vh.AddVert(a + normal, tint, Vector2.zero);
            vh.AddVert(b + normal, tint, Vector2.zero);
            vh.AddVert(b - normal, tint, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }

    public static class TerminalPanel
    {
        public static Text Briefing(Transform parent, string heading, string subtitle)
        {
            var root = Ui.Surface("FieldBriefing", parent, Theme.Panel, Theme.EdgeLive);
            Ui.Size(root.gameObject, 268f);
            var mapRoot = Ui.Rect("SeoulSchematic", root);
            mapRoot.anchorMin = new Vector2(.58f, .09f);
            mapRoot.anchorMax = new Vector2(.97f, .88f);
            mapRoot.offsetMin = mapRoot.offsetMax = Vector2.zero;
            mapRoot.gameObject.AddComponent<ThemeScene>().raycastTarget = false;
            var content = Ui.Rect("BriefingText", root);
            content.anchorMax = new Vector2(.59f, 1);
            content.offsetMin = new Vector2(24, 18);
            content.offsetMax = new Vector2(-8, -18);
            Ui.Column(content, 8);
            var code = Ui.Label("Code", content, AfterSeoul.Core.Loc.Text("AFTER SEOUL / 살아남은 도시"), 24, TextAnchor.MiddleLeft, Theme.Info);
            Ui.Size(code.gameObject, 30);
            var title = Ui.Label("Heading", content, heading, Theme.FontHeading, TextAnchor.MiddleLeft);
            Ui.Size(title.gameObject, 54);
            var status = Ui.Paragraph("Briefing", content, subtitle, Theme.FontSmall, Theme.TextDim);
            Ui.Size(status.gameObject, 100);
            return status;
        }
    }
}
