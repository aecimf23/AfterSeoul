using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    /// <summary>
    /// 진행 막대 하나.
    ///
    /// <para><b>왜 숫자만으로는 부족한가:</b> "12분 34초 남음"은 읽어야 알고, 두 줄이 있으면
    /// 어느 쪽이 먼저 끝나는지 비교하려고 두 번 읽어야 한다. 막대는 안 읽어도 보인다.
    /// 제작 큐와 파견 목록처럼 <b>여러 개가 동시에 흘러가는 곳</b>에서 차이가 크다.</para>
    ///
    /// <para>값은 매 프레임 <see cref="ScreenBase.Tick"/> 에서 들어온다. 그래서 기본은
    /// 보간 없이 곧바로 반영하고(<c>animate: false</c>), 한 번에 크게 뛰는 경우 —
    /// 처음 그릴 때나 정산 직후 — 에만 보간한다.</para>
    /// </summary>
    public sealed class ProgressBar
    {
        private readonly RectTransform _root;
        private readonly Image _fill;

        /// <summary>완료 직전에 깜빡이는 덮개. 채움 색과 분리해 둬야 색을 바꿔도 맥박이 안 깨진다.</summary>
        private readonly Image _glow;

        private float _value;
        private bool _pulsing;

        public RectTransform Root => _root;
        public float Value => _value;

        /// <summary>목록을 다시 그리면 게임오브젝트가 파괴된다. 그 뒤의 호출을 조용히 흘려보낸다.</summary>
        public bool Alive => _root != null;

        private ProgressBar(RectTransform root, Image fill, Image glow)
        {
            _root = root;
            _fill = fill;
            _glow = glow;
        }

        public static ProgressBar Create(Transform parent, float height = 12f, Color? fillColor = null)
        {
            var root = Ui.Rect("Bar", parent);
            Ui.Size(root.gameObject, height, flexWidth: 1f);

            var track = root.gameObject.AddComponent<Image>();
            track.sprite = Skin.Pill;
            track.type = Image.Type.Sliced;
            track.color = Theme.BarTrack;
            track.raycastTarget = false;

            var fillRt = Ui.Rect("Fill", root);
            Ui.Stretch(fillRt);
            var fill = fillRt.gameObject.AddComponent<Image>();
            fill.sprite = Skin.Pill;
            // Filled 는 Sliced 와 같이 쓸 수 없다. 캡슐 스프라이트를 가로로 잘라 쓰면
            // 왼쪽 끝은 둥글게 남고 오른쪽은 잘린 단면이 된다 — 진행 막대로는 그게 맞다.
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = 0f;
            fill.color = fillColor ?? Theme.Info;
            fill.raycastTarget = false;

            var glowRt = Ui.Rect("Glow", root);
            Ui.Stretch(glowRt);
            var glow = glowRt.gameObject.AddComponent<Image>();
            glow.sprite = Skin.Pill;
            glow.type = Image.Type.Sliced;
            glow.color = new Color(1f, 1f, 1f, 0f);
            glow.raycastTarget = false;

            return new ProgressBar(root, fill, glow);
        }

        public void Set(float value01, bool animate = false)
        {
            if (_fill == null) return;

            value01 = Mathf.Clamp01(value01);
            _value = value01;

            if (!animate)
            {
                Tween.Cancel(_fill, "fill");
                _fill.fillAmount = value01;
                return;
            }

            float from = _fill.fillAmount;
            Tween.Play(_fill, "fill", 0.35f,
                t => { if (_fill != null) _fill.fillAmount = Mathf.LerpUnclamped(from, value01, t); });
        }

        public void SetColor(Color color)
        {
            if (_fill == null) return;
            _fill.color = color;
        }

        /// <summary>
        /// 다 됐는데 아직 손대지 않은 상태. 맥박으로 알린다.
        ///
        /// <para>완료를 색만으로 알리면 화면을 스치듯 보는 사람은 놓친다. 움직이는 것은 안 놓친다 —
        /// 대신 <b>완료된 것에만</b> 쓴다. 진행 중인 줄까지 움직이면 화면이 그냥 시끄러워진다.</para>
        /// </summary>
        public void SetPulsing(bool on)
        {
            if (_glow == null || _pulsing == on) return;
            _pulsing = on;

            if (!on)
            {
                Tween.Cancel(_glow, "pulse");
                _glow.color = new Color(1f, 1f, 1f, 0f);
                return;
            }

            Tween.Loop(_glow, "pulse", 1.3f, t =>
            {
                if (_glow == null) return;
                _glow.color = new Color(1f, 1f, 1f, Tween.PingPong(t) * 0.16f);
            });
        }
    }
}
