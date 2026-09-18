using System;
using UnityEngine;
using UnityEngine.UI;
using AfterSeoul.Core;

namespace AfterSeoul.Unity.UI
{
    /// <summary>
    /// uGUI 를 코드로 만드는 헬퍼.
    ///
    /// <para><b>왜 씬·프리팹이 아니라 코드인가:</b> 씬과 프리팹은 바이너리에 가까운 YAML 이라
    /// diff 가 안 읽히고 머지 충돌이 나면 손으로 풀 수가 없다. 1인 개발이라도 — 아니 1인이라
    /// 더더욱 — UI 를 텍스트로 두면 되돌리기가 쉽다. <see cref="Bootstrap"/> 이 씬 배치 없이
    /// 스스로 뜨는 것과 같은 이유다.</para>
    ///
    /// <para>여기 있는 건 전부 "만들어서 돌려주는" 함수다. 상태를 갖지 않는다.</para>
    /// </summary>
    public static class Ui
    {
        // ── 기본 ────────────────────────────────────────────────

        /// <summary>
        /// RectTransform 하나를 만든다.
        ///
        /// <para><b>기본이 "부모 가득 채우기"다.</b> 갓 만든 RectTransform 은 크기가 0 이라
        /// 앵커 설정을 빼먹으면 화면에서 그냥 사라진다 — 그런데 오류가 안 나서 원인을 찾기 어렵다.
        /// 안전한 쪽을 기본으로 둔다. 앵커를 직접 잡는 호출부는 뒤에서 덮어쓰면 되고,
        /// LayoutGroup 자식은 어차피 그룹이 위치와 크기를 다시 잡는다.</para>
        /// </summary>
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return Stretch(rt);
        }

        /// <summary>부모를 가득 채운다.</summary>
        public static RectTransform Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>부모 위쪽에 붙이고 높이를 고정한다.</summary>
        public static RectTransform Top(RectTransform rt, float height, float inset = 0)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(inset, -height);
            rt.offsetMax = new Vector2(-inset, 0f);
            return rt;
        }

        /// <summary>부모 아래쪽에 붙이고 높이를 고정한다.</summary>
        public static RectTransform Bottom(RectTransform rt, float height, float inset = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(inset, 0f);
            rt.offsetMax = new Vector2(-inset, height);
            return rt;
        }

        /// <summary>모서리가 각진 단색 면. 헤더·탭 바처럼 화면 끝까지 닿는 띠에 쓴다.</summary>
        public static Image Panel(string name, Transform parent, Color color)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// 모서리가 둥글고 머리카락 굵기의 테두리가 있는 면 (GDD §26).
        ///
        /// <para>테두리를 <see cref="Image"/> 하나로 합치지 않고 겹쳐 올린다. 한 장으로 하면
        /// 테두리 색이 채움 색의 함수가 되어 버려서 — 같은 회색 카드에 상태별로 다른 테두리를
        /// 두를 수가 없다. 지금 진행 중인 줄만 테두리를 녹색으로 바꾸는 게 그 경우다.</para>
        /// </summary>
        public static RectTransform Surface(string name, Transform parent, Color fill, Color? edge = null)
        {
            var rt = Rect(name, parent);

            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Skin.Panel;
            img.type = Image.Type.Sliced;
            img.color = fill;
            img.raycastTarget = false;

            SetEdge(rt, edge ?? Theme.Edge);
            return rt;
        }

        /// <summary>
        /// 면의 테두리 색을 바꾼다. 없으면 만들고, 투명을 주면 지운 것처럼 보인다.
        /// 상태가 바뀐 줄을 다시 만들지 않고 테두리만 갈아 끼울 때 쓴다.
        /// </summary>
        public static Image SetEdge(RectTransform surface, Color color)
        {
            if (surface == null) return null;

            var existing = surface.Find("Edge") as RectTransform;
            if (existing == null)
            {
                existing = Rect("Edge", surface);
                var created = existing.gameObject.AddComponent<Image>();
                created.sprite = Skin.PanelEdge;
                created.type = Image.Type.Sliced;
                created.raycastTarget = false;

                // 면에는 보통 LayoutGroup 이 붙는다. 그대로 두면 테두리가 "첫 번째 줄"로
                // 배치돼서 내용이 한 칸 밀리고 테두리는 띠처럼 찌그러진다.
                existing.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

                // 테두리는 항상 맨 위다. 나중에 붙는 내용에 덮이면 있으나 마나다.
                existing.SetAsLastSibling();
            }

            var img = existing.GetComponent<Image>();
            img.color = color;
            // 내용이 뒤에 붙어도 테두리가 위에 남게 매번 끌어올린다.
            existing.SetAsLastSibling();
            return img;
        }

        /// <summary>
        /// 네 귀퉁이 꺾쇠. 창과 연출처럼 <b>한 번에 하나만 뜨는 것</b>에만 붙인다.
        /// 목록의 모든 줄에 붙이면 표시가 아니라 소음이 된다.
        /// </summary>
        public static void Brackets(RectTransform parent, Color color, float size = 34f, float inset = 10f)
        {
            if (parent == null) return;

            // (앵커, 회전) 네 쌍. 스프라이트는 왼쪽 아래를 향하는 ㄴ 자 하나뿐이라 돌려 쓴다.
            var anchors = new[]
            {
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
            };
            var angles = new[] { 0f, -90f, 180f, 90f };

            for (int i = 0; i < 4; i++)
            {
                var rt = Rect("Corner" + i, parent);
                rt.anchorMin = rt.anchorMax = anchors[i];
                rt.pivot = anchors[i];
                rt.sizeDelta = new Vector2(size, size);
                rt.anchoredPosition = new Vector2(
                    anchors[i].x == 0f ? inset : -inset,
                    anchors[i].y == 0f ? inset : -inset);
                rt.localRotation = Quaternion.Euler(0f, 0f, angles[i]);
                rt.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

                var img = rt.gameObject.AddComponent<Image>();
                img.sprite = Skin.Corner;
                img.color = color;
                img.raycastTarget = false;
            }
        }

        /// <summary>진행 막대. 목록 줄 아래에 얇게 깐다.</summary>
        public static ProgressBar Bar(Transform parent, float height = 10f, Color? fill = null)
            => ProgressBar.Create(parent, height, fill);

        public static Text Label(string name, Transform parent, string text,
            int size = Theme.FontBody, TextAnchor anchor = TextAnchor.MiddleLeft, Color? color = null)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Theme.Font;
            t.fontSize = size;
            // Longer translations remain within the same mobile touch layout.
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = Mathf.Min(size, Mathf.Max(18, Mathf.RoundToInt(size * .72f)));
            t.resizeTextMaxSize = size;
            t.text = text;
            t.color = color ?? Theme.Text;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>줄바꿈이 필요한 본문용. Overflow 대신 Wrap.</summary>
        public static Text Paragraph(string name, Transform parent, string text,
            int size = Theme.FontBody, Color? color = null)
        {
            var t = Label(name, parent, text, size, TextAnchor.UpperLeft, color);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        // ── 버튼 ────────────────────────────────────────────────

        /// <param name="withLabel">
        /// false 면 글자 자식을 만들지 않는다. 아이콘과 글자를 세로로 쌓는 탭처럼
        /// 속을 직접 채울 버튼에 쓴다 — 가운데 정렬 한 줄짜리 라벨이 미리 들어 있으면 얹을 수가 없다.
        /// </param>
        public static UnityEngine.UI.Button Button(string name, Transform parent, string label, Action onClick,
            Color? fill = null, int fontSize = Theme.FontBody, bool withLabel = true)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Skin.Panel;
            img.type = Image.Type.Sliced;
            img.color = fill ?? Theme.AccentDim;

            var btn = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = img;

            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.55f);
            colors.fadeDuration = 0.05f;
            btn.colors = colors;

            if (withLabel)
            {
                var ink = img.color.grayscale > .5f ? Theme.Bg : Theme.Text;
                var text = Label(name + "Label", rt, label, fontSize, TextAnchor.MiddleCenter, ink);
                Stretch(text.rectTransform, 12f, 12f, 4f, 4f);
            }

            if (onClick != null) btn.onClick.AddListener(() => { Sfx.Tap(); onClick(); });

            // 눌린 티. 색만 바뀌면 손가락에 가려 안 보인다 — 손가락이 덮은 자리 바깥이 움직여야
            // 눌렸다는 걸 안다. 크기는 6% 뿐이고 0.18초에 되돌아온다.
            btn.onClick.AddListener(() => Tween.Punch(rt));
            return btn;
        }

        /// <summary>버튼 안의 글자를 바꾼다. 캐싱 없이 자식에서 찾는다 — 호출이 잦지 않다.</summary>
        public static void SetButtonLabel(UnityEngine.UI.Button btn, string label)
        {
            var t = btn.GetComponentInChildren<Text>();
            if (t != null) t.text = label;
        }

        // ── 레이아웃 ────────────────────────────────────────────

        public static VerticalLayoutGroup Column(RectTransform rt, float spacing = 12f, RectOffset padding = null)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding ?? new RectOffset(0, 0, 0, 0);
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childAlignment = TextAnchor.UpperLeft;
            return v;
        }

        public static HorizontalLayoutGroup Row(RectTransform rt, float spacing = 12f, RectOffset padding = null)
        {
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = padding ?? new RectOffset(0, 0, 0, 0);
            // false 여야 flexibleWidth 가 폭 분배를 결정한다. true 면 남는 폭을
            // 모든 자식에 균등 분배해서 "하나만 늘리기"가 안 된다.
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childAlignment = TextAnchor.MiddleCenter;
            return h;
        }

        public static LayoutElement Size(GameObject go, float? height = null, float? width = null,
            float flexWidth = -1f, float flexHeight = -1f)
        {
            // ?? 를 쓰지 않는다. GetComponent 는 없을 때 "가짜 null"(네이티브 포인터가 0 인
            // 관리 객체)을 돌려줄 수 있는데, ?? 는 UnityEngine.Object 의 == 오버로드를 타지 않고
            // 참조 동일성만 본다 — 그러면 가짜 null 이 그대로 통과해서 만지는 순간 터진다.
            // 실제로 Tween.GroupOf 가 같은 모양으로 터졌다. TryGetComponent 는 네이티브에서 판정한다.
            LayoutElement le;
            if (!go.TryGetComponent(out le)) le = go.AddComponent<LayoutElement>();

            if (height.HasValue) le.preferredHeight = height.Value;
            if (width.HasValue) le.preferredWidth = width.Value;
            if (flexWidth >= 0f) le.flexibleWidth = flexWidth;
            if (flexHeight >= 0f) le.flexibleHeight = flexHeight;
            return le;
        }

        // ── 스크롤 목록 ──────────────────────────────────────────

        /// <summary>
        /// 세로 스크롤 목록을 만들고 <b>내용을 담을 Content</b> 를 돌려준다.
        /// 항목은 Content 아래에 붙이면 되고, 높이는 VerticalLayoutGroup + ContentSizeFitter 가 잡는다.
        /// </summary>
        public static RectTransform ScrollList(string name, Transform parent, out ScrollRect scroll, float spacing = 10f)
        {
            // 부모를 가득 채운다. 이걸 빼먹으면 root 가 크기 0 이 되고,
            // viewport 의 RectMask2D 가 내용을 전부 잘라내 "작은 사각형"만 남는다.
            var root = Stretch(Rect(name, parent));

            scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.08f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.12f;
            scroll.scrollSensitivity = 40f;

            var viewport = Rect("Viewport", root);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            // ScrollRect 는 viewport 에 Graphic 이 없어도 동작하지만,
            // 빈 영역을 드래그해도 스크롤되게 하려면 투명 Image 가 필요하다.
            var vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0f);

            var content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(0f, 0f);
            content.offsetMax = new Vector2(0f, 0f);

            Column(content, spacing);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;
            return content;
        }

        /// <summary>
        /// 제목 + 본문 카드. 홈 화면처럼 정보를 묶어 보여줄 때.
        ///
        /// <para>왼쪽 모서리에 짧은 색 띠를 붙인다 — 서류철 라벨이자, 스크롤을 빠르게 내릴 때
        /// 카드가 어디서 시작하는지 잡아주는 표시다. 제목 글씨만으로는 비슷한 회색 덩어리가
        /// 몇 개 지나갔는지 세어지지 않는다.</para>
        /// </summary>
        public static RectTransform Card(Transform parent, string title, out RectTransform body)
        {
            // ContentSizeFitter 를 붙이지 않는다. 카드는 항상 LayoutGroup 자식이고,
            // 부모가 이 카드의 VerticalLayoutGroup 이 계산한 preferredHeight 로 크기를 잡는다.
            // 둘을 같이 두면 Unity 가 "레이아웃 그룹 자식에 Fitter" 경고를 내고 크기가 튄다.
            var card = Surface("Card_" + title, parent, Theme.Panel);
            Column(card, 8f, new RectOffset(24, 20, 16, 16));

            var tab = Rect("Tab", card);
            tab.anchorMin = new Vector2(0f, 0f);
            tab.anchorMax = new Vector2(0f, 1f);
            tab.pivot = new Vector2(0f, 0.5f);
            tab.offsetMin = new Vector2(0f, 14f);
            tab.offsetMax = new Vector2(6f, -14f);
            tab.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            var tabImg = tab.gameObject.AddComponent<Image>();
            tabImg.sprite = Skin.Pill;
            tabImg.type = Image.Type.Sliced;
            tabImg.color = Theme.AccentDim;
            tabImg.raycastTarget = false;

            var head = Label("Title", card, "[ " + title + " ]", Theme.FontSmall, TextAnchor.MiddleLeft, Theme.Info);
            Size(head.gameObject, 38f);

            var divider = Panel("Divider", card, Theme.Line);
            Size(divider.gameObject, 1f);

            body = Rect("Body", card);
            Column(body, 6f);
            return card;
        }

        // ── 창 ──────────────────────────────────────────────────

        /// <summary>
        /// 화면을 덮는 창. 바깥(어두운 배경)을 누르면 닫힌다.
        ///
        /// <para>반환값은 창 전체의 루트다. 닫을 때는 이걸 Destroy 하면 된다.
        /// <paramref name="body"/> 는 내용이 들어갈 스크롤 영역이고, 목록이 길어도 스크롤된다.</para>
        ///
        /// <para>모바일에서 화면을 새로 만드는 대신 창을 쓰는 이유: 장비를 고르는 동안
        /// 스캐브 목록이 뒤에 그대로 보여야 "누구 것을 고르는 중인지"를 잃지 않는다.</para>
        /// </summary>
        public static RectTransform Modal(string name, Transform parent, string title,
            Action onClose, out RectTransform body)
        {
            var root = Rect(name, parent);
            root.gameObject.AddComponent<ModalState>();
            Stretch(root);

            // 어두운 바탕. raycastTarget 을 켜야 뒤쪽 버튼이 눌리지 않는다.
            var scrim = root.gameObject.AddComponent<Image>();
            scrim.color = Theme.Scrim;
            scrim.raycastTarget = true;

            var closeOnScrim = root.gameObject.AddComponent<UnityEngine.UI.Button>();
            closeOnScrim.targetGraphic = scrim;
            closeOnScrim.transition = Selectable.Transition.None;
            if (onClose != null) closeOnScrim.onClick.AddListener(() => onClose());

            var panel = Rect("Panel", root);
            Stretch(panel, 36f, 36f, 70f, 70f);

            // 창 자체를 눌렀을 때 닫히면 안 되므로 여기서 클릭을 막는다.
            var panelBg = panel.gameObject.AddComponent<Image>();
            panelBg.sprite = Skin.Panel;
            panelBg.type = Image.Type.Sliced;
            panelBg.color = Theme.Bg;
            panelBg.raycastTarget = true;

            // A Graphic alone does not stop ExecuteHierarchy from finding the scrim's Button.
            // Consume panel clicks here while keeping child controls independently interactive.
            var panelClick = panel.gameObject.AddComponent<UnityEngine.UI.Button>();
            panelClick.targetGraphic = panelBg;
            panelClick.transition = Selectable.Transition.None;
            panelClick.navigation = new Navigation { mode = Navigation.Mode.None };

            SetEdge(panel, Theme.Edge);
            Brackets(panel, Theme.AccentDim);

            Column(panel, 10f, new RectOffset(22, 22, 18, 18));

            // 툭 나타나면 어디서 왔는지 몰라 한 박자 늦게 읽힌다. 배경은 0.14초에 걸쳐 어두워지고
            // 창은 아주 살짝 커지며 올라온다 — 뒤에 있던 목록이 그대로 남아 있다는 게 보여야 한다.
            var scrimGroup = Tween.GroupOf(root);
            scrimGroup.alpha = 0f;
            Tween.Play(scrimGroup, "alpha", 0.14f, t => scrimGroup.alpha = t, Tween.Ease.OutQuad);
            Tween.Play(panel, "in", 0.2f,
                t => panel.localScale = Vector3.one * Mathf.LerpUnclamped(0.96f, 1f, t),
                Tween.Ease.OutBack);

            var head = Rect("Head", panel);
            Size(head.gameObject, 74f, flexHeight: 0f);
            Row(head, 10f);

            var titleLabel = Label("Title", head, title, Theme.FontHeading,
                TextAnchor.MiddleLeft, Theme.Accent);
            Size(titleLabel.gameObject, flexWidth: 1f);

            var closeBtn = Button("Close", head, Loc.Text("닫기"), onClose, Theme.Line, Theme.FontSmall);
            Size(closeBtn.gameObject, width: 170f, flexWidth: 0f);

            ScrollRect scroll;
            body = ScrollList("Body", panel, out scroll, 10f);
            // 스크롤이 남은 높이를 전부 먹어야 한다. 이걸 빼면 높이 0 이 되어
            // 내용이 있는데도 빈 창처럼 보인다.
            Size(scroll.gameObject, flexHeight: 1f);

            return root;
        }

        /// <summary>
        /// 아이템군 아이콘 한 칸. 목록 줄 맨 앞에 붙인다.
        ///
        /// <para>글자만 있는 목록은 한 줄씩 읽어야 하지만 아이콘이 붙으면 훑을 수 있다 —
        /// 창고에 약이 있나 없나를 확인하는 데 이름을 다 읽을 필요가 없어진다.</para>
        /// </summary>
        public static Image Icon(string name, Transform parent, ItemGroup group, float size = 44f)
        {
            return ItemIcon(name, parent, ItemArtwork.KindOf(group), group, size);
        }

        public static Image Icon(string name, Transform parent, ItemDef item, float size = 44f)
        {
            return ItemIcon(name, parent, ItemArtwork.KindOf(item), ItemGroups.Of(item), size, ItemArtwork.For(item));
        }

        private static Image ItemIcon(string name, Transform parent, ItemArtworkKind kind, ItemGroup group, float size, Sprite individual = null)
        {
            var rt = Rect(name, parent);
            Size(rt.gameObject, height: size, width: size, flexWidth: 0f, flexHeight: 0f);

            var img = rt.gameObject.AddComponent<Image>();
            var artwork = individual != null ? individual : ItemArtwork.For(kind);
            img.sprite = artwork != null ? artwork : IconSet.For(group);
            img.color = artwork != null ? Color.white : IconSet.ColorOf(group);
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// 자식 전부 제거. 목록을 다시 그릴 때.
        ///
        /// <para><c>Destroy</c> 는 프레임 끝에야 실제로 지운다. 그래서 지우고 곧바로 다시 만들면
        /// 그 한 프레임 동안 옛 것과 새 것이 같이 배치되고 같이 그려진다 — 목록에서는 한 번 덜컹하는
        /// 정도지만, 공정마다 새로 만드는 미니게임에서는 눈에 띄는 깜빡임이 된다.
        /// 그래서 먼저 계층에서 떼어내고 나서 파괴한다.</para>
        /// </summary>
        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var child = t.GetChild(i).gameObject;
                child.transform.SetParent(null, false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(child);
                else UnityEngine.Object.DestroyImmediate(child);
            }
        }
    }
}
