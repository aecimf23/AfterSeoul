using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    /// <summary>
    /// 아주 작은 애니메이션 구동기.
    ///
    /// <para><b>왜 DOTween 이 아닌가:</b> 에셋 하나를 더 얹으면 라이선스·업데이트·IL2CPP 링커
    /// 설정이 따라온다. 여기서 필요한 건 "값 하나를 시간에 걸쳐 옮기기" 뿐이고, 그건 60줄이다.
    /// 프로시저럴 아이콘·효과음과 같은 이유로 코드로 둔다.</para>
    ///
    /// <para><b>왜 코루틴이 아닌가:</b> <see cref="ScreenBase"/> 는 MonoBehaviour 가 아니라
    /// StartCoroutine 을 부를 주체가 없다. 게다가 코루틴은 대상이 파괴되면 조용히 죽거나
    /// 파괴된 객체를 계속 만지는데, 여기서는 목록을 통째로 다시 그리는 일이 잦아서
    /// 그 상황이 예외가 아니라 일상이다. 그래서 <b>트랙마다 주인(Owner)을 들고 있다가
    /// 주인이 죽으면 트랙도 같이 버린다</b> — 화면 코드가 정리 책임을 지지 않아도 된다.</para>
    ///
    /// <para>시간은 항상 <c>unscaledDeltaTime</c> 이다. UI 는 <c>Time.timeScale</c> 과
    /// 무관하게 움직여야 한다.</para>
    /// </summary>
    public static class Tween
    {
        public enum Ease
        {
            Linear,
            InQuad,
            OutQuad,
            OutCubic,
            InOutCubic,

            /// <summary>목표를 살짝 지나쳤다 돌아온다. 나타나는 것에만 쓴다 — 사라질 때 쓰면 덜컹거린다.</summary>
            OutBack,
        }

        private sealed class Track
        {
            /// <summary>
            /// 이 트랙이 만지는 대상. <b>파괴되면 트랙도 버린다.</b>
            /// UnityEngine.Object 로 들고 있어야 파괴 판정(<c>== null</c> 연산자 오버로드)이 먹는다.
            /// </summary>
            public UnityEngine.Object Owner;

            /// <summary>같은 주인의 같은 채널은 하나만 산다. 새로 걸면 옛것이 물러난다.</summary>
            public string Channel;

            public float Delay;
            public float Duration;
            public float Elapsed;
            public Ease Ease;
            public bool Loop;
            public Action<float> Apply;
            public Action Done;
        }

        private static readonly List<Track> Tracks = new List<Track>();

        /// <summary>순회 중에 Apply/Done 이 새 트랙을 걸 수 있다. 그래서 복사본을 돈다.</summary>
        private static readonly List<Track> Scratch = new List<Track>();

        /// <summary>지금 살아 있는 트랙 수. 테스트와 진단용.</summary>
        public static int Count => Tracks.Count;

        // ── 구동 ────────────────────────────────────────────────

        /// <summary><see cref="AppShell"/> 의 Update 가 매 프레임 부른다.</summary>
        public static void Tick(float deltaTime)
        {
            if (Tracks.Count == 0) return;

            Scratch.Clear();
            Scratch.AddRange(Tracks);

            for (int i = 0; i < Scratch.Count; i++)
            {
                var track = Scratch[i];

                // 주인이 파괴됐다. 값을 넣을 곳이 없으니 조용히 버린다 —
                // 화면을 다시 그릴 때마다 취소를 호출하게 만들면 언젠가 반드시 빠뜨린다.
                if (track.Owner == null) { Tracks.Remove(track); continue; }

                track.Elapsed += deltaTime;
                if (track.Elapsed < track.Delay) continue;

                float raw = track.Duration <= 0f
                    ? 1f
                    : Mathf.Clamp01((track.Elapsed - track.Delay) / track.Duration);

                track.Apply(Curve(track.Ease, raw));

                if (raw < 1f) continue;

                if (track.Loop)
                {
                    // 남은 시간을 버리지 않고 되감는다. 버리면 프레임이 튈 때마다 박자가 밀린다.
                    track.Elapsed -= track.Duration;
                    continue;
                }

                Tracks.Remove(track);
                if (track.Done != null) track.Done();
            }
        }

        /// <summary>
        /// 값 하나를 <paramref name="duration"/> 동안 0→1 로 옮기며 <paramref name="apply"/> 를 부른다.
        /// 보간 대상은 호출부가 정한다 — 여기는 시간만 센다.
        /// </summary>
        public static void Play(UnityEngine.Object owner, string channel, float duration,
            Action<float> apply, Ease ease = Ease.OutCubic, float delay = 0f, Action done = null)
        {
            if (owner == null || apply == null) return;

            Cancel(owner, channel);

            // 지연이 없으면 시작값을 지금 반영한다. 다음 프레임까지 미루면
            // 페이드인이 한 프레임 동안 불투명하게 보였다가 사라진다.
            if (PresentationSettings.ReducedMotion && IsPresentation(channel)) { apply(1f); done?.Invoke(); return; }
            if (delay <= 0f) apply(Curve(ease, 0f));

            Tracks.Add(new Track
            {
                Owner = owner,
                Channel = channel,
                Delay = Mathf.Max(0f, delay),
                Duration = Mathf.Max(0f, duration),
                Ease = ease,
                Apply = apply,
                Done = done,
            });
        }

        /// <summary>
        /// 끝나지 않고 되풀이한다. 맥박·점멸처럼 "상태가 지속되는 동안 계속"인 것에 쓴다.
        /// 주인이 파괴되면 저절로 멈추므로 멈출 책임을 호출부가 지지 않는다.
        /// </summary>
        public static void Loop(UnityEngine.Object owner, string channel, float period,
            Action<float> apply, Ease ease = Ease.Linear)
        {
            if (owner == null || apply == null || period <= 0f) return;

            Cancel(owner, channel);
            if (PresentationSettings.ReducedMotion && IsPresentation(channel)) { apply(.5f); return; }
            apply(Curve(ease, 0f));

            Tracks.Add(new Track
            {
                Owner = owner,
                Channel = channel,
                Duration = period,
                Ease = ease,
                Loop = true,
                Apply = apply,
            });
        }

        private static bool IsPresentation(string channel) =>
            channel == "punch" || channel == "pos" || channel == "alpha" || channel == "in" || channel == "pulse" || channel == "breathe" || channel == "wipe";
        public static void CompleteTints() => CompleteWhere(t => t.Channel == "tint");
        public static void CompletePresentation()
        {
            if (PresentationSettings.ReducedMotion) CompleteWhere(t => IsPresentation(t.Channel));
        }
        private static void CompleteWhere(Predicate<Track> match)
        {
            var selected = Tracks.FindAll(match);
            foreach (var track in selected)
            {
                Tracks.Remove(track);
                if (track.Owner == null) continue;
                track.Apply(track.Loop ? .5f : 1f);
                track.Done?.Invoke();
            }
        }
        public static void Cancel(UnityEngine.Object owner, string channel)
        {
            for (int i = Tracks.Count - 1; i >= 0; i--)
                if (ReferenceEquals(Tracks[i].Owner, owner) && Tracks[i].Channel == channel)
                    Tracks.RemoveAt(i);
        }

        public static void CancelAll(UnityEngine.Object owner)
        {
            for (int i = Tracks.Count - 1; i >= 0; i--)
                if (ReferenceEquals(Tracks[i].Owner, owner)) Tracks.RemoveAt(i);
        }

        /// <summary>전부 버린다. 셸을 다시 만들 때(에디터 재생 반복) 잔재를 남기지 않는다.</summary>
        public static void Clear() => Tracks.Clear();

        // ── 자주 쓰는 것 ────────────────────────────────────────

        /// <summary>
        /// 알파를 다룰 CanvasGroup 을 보장한다. 이미 있으면 그걸 쓴다.
        ///
        /// <para><b><c>??</c> 를 쓰면 안 된다.</b> <c>GetComponent</c> 는 컴포넌트가 없을 때
        /// "가짜 null" — 네이티브 포인터가 0 인 관리 객체 — 을 돌려줄 수 있는데,
        /// <c>??</c> 와 <c>?.</c> 는 UnityEngine.Object 의 <c>==</c> 오버로드를 <b>타지 않고</b>
        /// 참조 동일성만 본다. 그래서 가짜 null 이 그대로 통과하고, 그걸 만지는 순간
        /// "There is no 'CanvasGroup' attached to..." 로 터진다.</para>
        ///
        /// <para><c>TryGetComponent</c> 는 네이티브 쪽에서 판정하므로 그 함정이 없다.
        /// 덤으로 없을 때 로그 할당도 안 한다.</para>
        /// </summary>
        public static CanvasGroup GroupOf(RectTransform rt)
        {
            if (rt == null) return null;

            CanvasGroup group;
            if (!rt.TryGetComponent(out group)) group = rt.gameObject.AddComponent<CanvasGroup>();
            return group;
        }

        public static void FadeIn(RectTransform rt, float duration = 0.16f, float delay = 0f)
        {
            var group = GroupOf(rt);
            if (group == null) return;
            group.alpha = 0f;
            Play(group, "alpha", duration, t => group.alpha = t, Ease.OutQuad, delay);
        }

        /// <summary>다 사라지면 <paramref name="done"/>. 보통 여기서 Destroy 한다.</summary>
        public static void FadeOut(RectTransform rt, float duration, Action done)
        {
            var group = GroupOf(rt);
            if (group == null) { done?.Invoke(); return; }

            float from = group.alpha;
            Play(group, "alpha", duration, t => group.alpha = Mathf.Lerp(from, 0f, t),
                Ease.InQuad, 0f, done);
        }

        /// <summary>
        /// 제자리로 미끄러져 들어온다. <paramref name="from"/> 은 목표 위치로부터의 어긋남(px).
        ///
        /// <para><b>LayoutGroup 의 자식에는 쓰지 않는다.</b> 레이아웃이 매 패스마다
        /// anchoredPosition 을 덮어써서 애니메이션이 씹힌다 — 줄을 하나 더 감싸고
        /// 그 안쪽을 움직여야 한다.</para>
        /// </summary>
        public static void SlideIn(RectTransform rt, Vector2 from,
            float duration = 0.22f, float delay = 0f, Ease ease = Ease.OutCubic)
        {
            if (rt == null) return;
            Vector2 target = rt.anchoredPosition;
            Vector2 start = target + from;
            rt.anchoredPosition = start;
            Play(rt, "pos", duration, t => rt.anchoredPosition = Vector2.LerpUnclamped(start, target, t),
                ease, delay);
        }

        /// <summary>눌린 느낌. 살짝 커졌다 돌아온다.</summary>
        public static void Punch(Transform target, float strength = 0.06f, float duration = 0.18f)
        {
            if (target == null) return;
            Play(target, "punch", duration,
                t => target.localScale = Vector3.one * (1f + strength * Mathf.Sin(t * Mathf.PI)),
                Ease.Linear, 0f, () => { if (target != null) target.localScale = Vector3.one; });
        }

        /// <summary>
        /// 숫자가 흘러 올라간다.
        ///
        /// <para>소지금이 소리 없이 바뀌면 방금 판 것이 얼마였는지 알 수가 없다.
        /// 숫자가 움직이면 그 자체가 영수증이 된다.</para>
        /// </summary>
        public static void Number(Text label, long from, long to, Func<long, string> format,
            float duration = 0.45f)
        {
            if (label == null || format == null) return;

            if (from == to) { label.text = format(to); return; }

            Play(label, "number", duration,
                t => label.text = format((long)Math.Round(from + (to - from) * (double)t)),
                Ease.OutCubic, 0f, () => { if (label != null) label.text = format(to); });
        }

        /// <summary>색을 옮긴다. 상태가 바뀐 줄이 툭 바뀌면 눈에 안 걸린다.</summary>
        public static void Tint(Graphic graphic, Color to, float duration = 0.2f)
        {
            if (graphic == null) return;
            Color from = graphic.color;
            Play(graphic, "tint", duration, t => graphic.color = Color.LerpUnclamped(from, to, t));
        }

        // ── 곡선 ────────────────────────────────────────────────

        public static float Curve(Ease ease, float t)
        {
            t = Mathf.Clamp01(t);
            switch (ease)
            {
                case Ease.Linear: return t;
                case Ease.InQuad: return t * t;
                case Ease.OutQuad: return 1f - (1f - t) * (1f - t);
                case Ease.InOutCubic:
                    return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
                case Ease.OutBack:
                {
                    const float c1 = 1.70158f;
                    const float c3 = c1 + 1f;
                    float u = t - 1f;
                    return 1f + c3 * u * u * u + c1 * u * u;
                }
                default:
                    return 1f - Mathf.Pow(1f - t, 3f);
            }
        }

        /// <summary>0→1→0. <see cref="Loop"/> 안에서 맥박을 만들 때.</summary>
        public static float PingPong(float t) => 1f - Mathf.Abs(t * 2f - 1f);
    }
}
