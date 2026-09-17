using AfterSeoul.Core;
using UnityEngine;

namespace AfterSeoul.Unity.UI
{
    /// <summary>
    /// 힘주기 — 누르고 있으면 차오르고, 목표 구간에서 손을 뗀다.
    ///
    /// <para>타이밍과 다른 점은 <b>실패하는 방향이 둘</b>이라는 것이다. 덜 조이면 0 점,
    /// 넘겨도 0 점 — 그래서 "안전하게 끝까지 눌러두기"가 안 통한다. 조이기·압착처럼
    /// 힘을 주는 공정에 붙인다.</para>
    ///
    /// <para><b>이 게임은 공정을 시작시킨 그 누름부터 시작한다</b>
    /// (<c>Minigames.StartsOnPress</c>). 시작 탭을 따로 받으면, 누르고 있는 것 자체가 플레이인
    /// 게임에서 첫 누름이 버려진다. 게다가 예전 버릇대로 툭 치면 0 에서 손을 뗀 셈이 되어
    /// 곧바로 실패하고, 그 사이 막대는 꿈쩍도 안 해서 고장난 것처럼 보인다.</para>
    ///
    /// <para>차오르는 속도는 목표 한가운데까지 대략 1.0~1.5초. 반응해서 뗄 시간은
    /// 목표 폭 / 속도 = 1단계 520ms / 2단계 362ms / 3단계 이후 258ms 다.</para>
    /// </summary>
    public sealed class HoldGame : Minigame
    {
        private static readonly float[] Rates = { 0.50f, 0.58f, 0.66f, 0.66f };
        private static readonly float[] Widths = { 0.26f, 0.21f, 0.17f, 0.17f };

        private RectTransform _bar, _target, _center, _fill;
        private float _rate, _half, _targetCenter, _value;
        private bool _pressing;

        public override MinigameKind Kind => MinigameKind.Hold;

        protected override void Build()
        {
            _bar = Bar("HoldBar");

            // 순서가 중요하다. 채움을 먼저 깔고 목표 띠를 반투명하게 덮는다 —
            // 반대로 하면 채움이 목표를 가려서, 정작 떼야 하는 순간에 어디가 목표였는지 안 보인다.
            _fill = Block("Fill", _bar, Theme.Info);
            _target = Block("Target", _bar, Fade(Theme.Accent, 0.45f));
            _center = Block("Center", _bar, Theme.Accent);
        }

        private static Color Fade(Color c, float alpha) => new Color(c.r, c.g, c.b, alpha);

        public override void PauseInput() { _pressing = false; }

        protected override void Start(int stage)
        {
            _rate = At(Rates, stage);
            _half = At(Widths, stage) * 0.5f;

            // 시작하자마자 목표가 오면 반응할 틈이 없다. 최소 0.40 지점부터.
            _targetCenter = Random.Range(0.40f + _half, 0.92f - _half);
            _value = 0f;
            _pressing = false;

            Span(_target, _targetCenter - _half, _targetCenter + _half);
            Pin(_center, _targetCenter, 2f);
            Span(_fill, 0f, 0f);
        }

        public override void Tick(float deltaTime)
        {
            if (Resolved || !_pressing) return;

            _value += deltaTime * _rate;
            Span(_fill, 0f, Mathf.Min(_value, 1f));

            // 넘어간 순간 바로 끝난다 — 손을 떼야 알게 되면 뗀 타이밍 탓으로 읽힌다.
            if (_value > _targetCenter + _half)
            {
                _pressing = false;
                Finish(0f, AfterSeoul.Core.Loc.Text("과압 — 너무 눌렀습니다"));
            }
        }

        public override void Press()
        {
            if (Resolved) return;
            _pressing = true;
        }

        public override void Release()
        {
            if (Resolved || !_pressing) return;
            _pressing = false;

            float score = Closeness(Mathf.Abs(_value - _targetCenter), _half);

            Finish(score,
                score <= 0f ? AfterSeoul.Core.Loc.Text("덜 조임")
                : score >= 0.92f ? AfterSeoul.Core.Loc.Text("정확")
                : score >= 0.7f ? AfterSeoul.Core.Loc.Text("양호")
                : AfterSeoul.Core.Loc.Text("아슬아슬"));
        }
    }
}
