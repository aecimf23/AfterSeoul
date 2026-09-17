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
        private readonly Image _art;
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
            _art = GameArt.Draw("CityArt", content, GameArt.World(11), false);
            _art.rectTransform.anchorMin = new Vector2(0, .32f); _art.rectTransform.anchorMax = new Vector2(1, .70f);
            _art.rectTransform.offsetMin = _art.rectTransform.offsetMax = Vector2.zero;
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
            if (_closed) return;
            _flow.Tick(seconds); Draw();
            _fade = Mathf.Min(1, _fade + seconds * 2.5f);
            _ink.alpha = _fade;
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
                UnityEngine.Object.Destroy(_root.gameObject);
                Sfx.Confirm(); _enter();
            }
            else { Sfx.Tap(); Draw(); }
        }
        private void Draw()
        {
            if (_shown == _flow.Phase) return;
            _shown = _flow.Phase; _fade = 0; _ink.alpha = 0;
            _eyebrow.text = "ESCAPE FROM SEOUL  /  ANOTHER SIDE";
            _title.text = "AFTER\nSEOUL";
            _art.sprite = GameArt.World(_shown == LaunchPhase.Logo ? 14 : 11);
            _caption.text = _shown == LaunchPhase.Logo ? Loc.Text("서울에 남은 사람들의 이야기")
                : _shown == LaunchPhase.Story ? Loc.Text("누군가는 서울을 빠져나가려 한다.\n누군가는 남아서, 내일을 준비한다.")
                : Loc.Text("불빛이 남아 있는 곳.\n당신의 다음 하루가 시작됩니다.");
            _hint.text = _shown == LaunchPhase.Title ? Loc.Text("화면을 터치하여 시작") : Loc.Text("터치하여 건너뛰기");
        }
    }
}
