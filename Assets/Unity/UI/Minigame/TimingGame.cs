using AfterSeoul.Core;
using UnityEngine;

namespace AfterSeoul.Unity.UI
{
    /// <summary>
    /// 타이밍 — 오가는 마커를 목표 구간에서 친다.
    ///
    /// <para>수치 근거는 "구간을 통과하는 데 걸리는 시간"(= 폭 / 속도)이다.
    /// 사람 반응속도 + 모바일 터치 지연이 대략 200~300ms 라, 그보다 짧으면
    /// 보고 누르는 게 불가능해지고 예측 게임이 된다.
    /// 1단계 618ms / 2단계 400ms / 3단계 이후 259ms.</para>
    ///
    /// <para>이건 초반 수입원이지 실력 관문이 아니다 (GDD §2 — 초반은 직접 노동 70%).
    /// 집중하면 대체로 성공하고 가끔 '우수'가 나오는 정도가 맞다.</para>
    /// </summary>
    public sealed class TimingGame : Minigame
    {
        private static readonly float[] Speeds = { 0.55f, 0.70f, 0.85f, 0.85f };
        private static readonly float[] Widths = { 0.34f, 0.28f, 0.22f, 0.22f };

        private RectTransform _bar, _target, _center, _marker;
        private float _t, _speed, _half, _targetCenter;

        public override MinigameKind Kind => MinigameKind.Timing;

        protected override void Build()
        {
            _bar = Bar("TimingBar");
            _target = Block("Target", _bar, Theme.AccentDim);
            _center = Block("Center", _bar, Theme.Accent);
            _marker = Block("Marker", _bar, Theme.Text);
        }

        protected override void Start(int stage)
        {
            _speed = At(Speeds, stage);
            _half = At(Widths, stage) * 0.5f;

            // 목표 구간이 막대 밖으로 나가지 않게 가장자리를 피해 잡는다.
            _targetCenter = Random.Range(_half + 0.06f, 1f - _half - 0.06f);
            _t = 0f;

            Span(_target, _targetCenter - _half, _targetCenter + _half);
            Pin(_center, _targetCenter, 2f);
            Pin(_marker, 0f, 5f);
        }

        public override void Tick(float deltaTime)
        {
            if (Resolved) return;
            _t += deltaTime * _speed;
            Pin(_marker, Mathf.PingPong(_t, 1f), 5f);
        }

        public override void Press()
        {
            float distance = Mathf.Abs(Mathf.PingPong(_t, 1f) - _targetCenter);
            float score = Closeness(distance, _half);

            Finish(score,
                score <= 0f ? "빗나감"
                : score >= 0.92f ? "정확"
                : score >= 0.7f ? "양호"
                : "아슬아슬");
        }
    }
}
