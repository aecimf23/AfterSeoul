using System;
using AfterSeoul.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    internal sealed class LaunchPresentation
    {
        private readonly LaunchFlow _flow = new LaunchFlow();
        private readonly RectTransform _root;
        private readonly Text _title, _caption, _hint, _eyebrow;
        private readonly Text _legacy;
        private readonly Image _art;
        private readonly RectTransform _mapViewport;
        private readonly CanvasGroup _ink;
        private readonly Action _enter;
        private LaunchPhase _shown = (LaunchPhase)(-1);
        private float _fade;
        private bool _closed;

        internal LaunchPresentation(Transform parent, Action enter, Action language = null)
        {
            _enter = enter;
            _root = Ui.Rect("LaunchPresentation", parent);
            var backdrop = _root.gameObject.AddComponent<Image>();
            backdrop.color = Theme.Bg; backdrop.raycastTarget = true;
            var tap = _root.gameObject.AddComponent<Button>();
            tap.targetGraphic = backdrop; tap.transition = Selectable.Transition.None;
            tap.onClick.AddListener(Tap);
            var safe = Ui.Rect("LaunchSafeArea", _root); safe.gameObject.AddComponent<SafeArea>();
            var content = Ui.Rect("LaunchInk", safe); Ui.Stretch(content, 32, 32, 24, 24);
            _ink = content.gameObject.AddComponent<CanvasGroup>();
            _ink.blocksRaycasts = false;
            _eyebrow = TextAt(content, "Eyebrow", .88f, .96f, 24, Theme.Info);
            _title = TextAt(content, "Title", .73f, .86f, 84, Theme.Text);
            _mapViewport=Ui.Rect("MapViewport",content);
            _mapViewport.anchorMin=_mapViewport.anchorMax=new Vector2(.5f,.51f);
            _mapViewport.anchoredPosition=Vector2.zero;
            _legacy=TextAt(_mapViewport,"LegacyMap",0,1,24,Theme.Info);
            _legacy.font=Theme.ArtFont;
            var rows=Resources.Load<TextAsset>("Art/launch_map_legacy").text.TrimEnd('\r','\n').Replace("\r","").Split('\n');
            int columns=0;
            foreach(var row in rows) columns=Math.Max(columns,row.Length);
            for(int i=0;i<rows.Length;i++) rows[i]=rows[i].PadRight(columns);
            _legacy.text=string.Join("\n",rows);
            _legacy.horizontalOverflow=HorizontalWrapMode.Overflow;
            _legacy.verticalOverflow=VerticalWrapMode.Overflow;
            _legacy.resizeTextForBestFit=false;
            _legacy.alignment=TextAnchor.UpperLeft;
            _legacy.rectTransform.anchorMin=_legacy.rectTransform.anchorMax=new Vector2(0,1);
            _legacy.rectTransform.pivot=new Vector2(0,1);
            _legacy.rectTransform.anchoredPosition=Vector2.zero;
            _art = GameArt.Place("CityArt", _mapViewport, GameArt.LaunchMap());
            _caption = TextAt(content, "Caption", .17f, .29f, 32, Theme.TextDim);
            _hint = TextAt(content, "TouchToStart", .04f, .12f, 32, Theme.Warn);
            var languages = Ui.Button("Language", safe, "LANGUAGE", language, Theme.PanelAlt, 24);
            var lr = (RectTransform)languages.transform;
            lr.anchorMin = new Vector2(.66f, .01f); lr.anchorMax = new Vector2(.97f, .055f);
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            Draw();
        }
        private static Text TextAt(Transform parent, string name, float bottom, float top, int size, Color color)
        {
            var text = Ui.Label(name, parent, "", size, TextAnchor.MiddleCenter, color);
            var rt = text.rectTransform;
            rt.anchorMin = new Vector2(0, bottom); rt.anchorMax = new Vector2(1, top);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true; text.resizeTextMinSize = 12; text.resizeTextMaxSize = size;
            return text;
        }
        internal void RefreshLanguage() { _shown = (LaunchPhase)(-1); Draw(); }

        internal void Tick(float seconds)
        {
            if (_closed || seconds<=0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            _flow.Tick(seconds); Draw();
            _fade = Mathf.Min(1, _fade + seconds * 2.5f);
            _ink.alpha = _fade;
            BlendMap();
            _hint.color = Color.Lerp(Theme.TextDim, Theme.Warn, .5f + .5f * Mathf.Sin((float)_flow.Elapsed * 2));
        }
        private void Tap()
        {
            if (_closed) return;
            _flow.Tap();
            if (_flow.Phase == LaunchPhase.Entered)
            {
                _closed = true;
                _root.gameObject.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(_root.gameObject);
                else UnityEngine.Object.DestroyImmediate(_root.gameObject);
                Sfx.Confirm(); _enter();
            }
            else { Sfx.Tap(); Draw(); BlendMap(); }
        }
        private void Draw()
        {
            if (_shown == _flow.Phase) return;
            if (_shown==(LaunchPhase)(-1)) { _fade=0; _ink.alpha=0; }
            _shown = _flow.Phase;
            _eyebrow.text = "ESCAPE FROM SEOUL  /  ANOTHER SIDE";
            _title.text = "AFTER\nSEOUL";
            BlendMap();
            _caption.text = _shown == LaunchPhase.Logo ? Loc.Text("서울에 남은 사람들의 이야기")
                : _shown == LaunchPhase.Story ? Loc.Text("누군가는 서울을 빠져나가려 한다.\n누군가는 남아서, 내일을 준비한다.")
                : Loc.Text("불빛이 남아 있는 곳.\n당신의 다음 하루가 시작됩니다.");
            _hint.text = _shown == LaunchPhase.Title ? Loc.Text("화면을 터치하여 시작") : Loc.Text("터치하여 건너뛰기");
        }

        private void BlendMap()
        {
            // Both renderings occupy the same 2:1 frame at every phone resolution.
            var available=((RectTransform)_mapViewport.parent).rect;
            float width=Mathf.Max(0,Mathf.Min(available.width,available.height*.38f*2));
            _mapViewport.sizeDelta=new Vector2(width,width/2);
            float textWidth=Mathf.Max(1,_legacy.preferredWidth);
            float textHeight=Mathf.Max(1,_legacy.preferredHeight);
            _legacy.rectTransform.sizeDelta=new Vector2(textWidth,textHeight);
            _legacy.rectTransform.localScale=new Vector3(width/textWidth,width/2/textHeight,1);
            // Hold the original briefly, then let the mobile drawing emerge in the same frame.
            float blend=Mathf.SmoothStep(0,1,Mathf.Clamp01(((float)_flow.Elapsed-1.2f)/2.8f));
            var legacyColor=_legacy.color; legacyColor.a=1-blend; _legacy.color=legacyColor;
            _art.color=new Color(1,1,1,blend);
        }
    }
}
