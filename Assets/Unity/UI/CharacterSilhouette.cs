using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    /// <summary>Resolution-independent clothed figure for the equipment diagram.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CharacterSilhouette : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Color outline = Theme.TextFaint, cloth = Color.Lerp(Theme.Panel, Theme.Info, .23f);
            Polygon(mesh, outline, .38f,.98f, .62f,.98f, .70f,.93f, .69f,.82f, .60f,.77f, .40f,.77f, .31f,.82f, .30f,.93f);
            Polygon(mesh, cloth, .39f,.96f, .61f,.96f, .67f,.91f, .66f,.86f, .34f,.86f, .33f,.91f);
            Polygon(mesh, Theme.Bg, .37f,.85f, .63f,.85f, .62f,.82f, .57f,.79f, .43f,.79f, .38f,.82f);
            Polygon(mesh, cloth, .34f,.76f, .66f,.76f, .74f,.70f, .66f,.48f, .34f,.48f, .26f,.70f);
            Polygon(mesh, outline, .34f,.72f, .66f,.72f, .63f,.52f, .37f,.52f);
            Polygon(mesh, cloth, .39f,.69f, .61f,.69f, .59f,.55f, .41f,.55f);
            Polygon(mesh, outline, .26f,.73f, .33f,.68f, .22f,.51f, .16f,.53f);
            Polygon(mesh, cloth, .16f,.54f, .23f,.51f, .19f,.37f, .10f,.39f);
            Polygon(mesh, outline, .74f,.73f, .67f,.68f, .78f,.51f, .84f,.53f);
            Polygon(mesh, cloth, .84f,.54f, .77f,.51f, .81f,.37f, .90f,.39f);
            Polygon(mesh, outline, .10f,.39f, .19f,.37f, .18f,.32f, .09f,.33f);
            Polygon(mesh, outline, .90f,.39f, .81f,.37f, .82f,.32f, .91f,.33f);
            Polygon(mesh, cloth, .34f,.46f, .49f,.46f, .47f,.28f, .42f,.09f, .29f,.09f, .31f,.28f);
            Polygon(mesh, cloth, .51f,.46f, .66f,.46f, .69f,.28f, .71f,.09f, .58f,.09f, .53f,.28f);
            Polygon(mesh, outline, .34f,.50f, .66f,.50f, .66f,.46f, .34f,.46f);
            Polygon(mesh, outline, .29f,.09f, .42f,.09f, .42f,.02f, .22f,.02f, .22f,.05f);
            Polygon(mesh, outline, .58f,.09f, .71f,.09f, .78f,.05f, .78f,.02f, .58f,.02f);
        }

        private void Polygon(VertexHelper mesh, Color shade, params float[] points)
        {
            var r = rectTransform.rect;
            int start = mesh.currentVertCount;
            for (int i = 0; i < points.Length; i += 2)
                mesh.AddVert(new Vector3(r.xMin + points[i] * r.width, r.yMin + points[i + 1] * r.height), shade, Vector2.zero);
            for (int i = 1; i < points.Length / 2 - 1; i++) mesh.AddTriangle(start, start + i, start + i + 1);
        }
    }
}
