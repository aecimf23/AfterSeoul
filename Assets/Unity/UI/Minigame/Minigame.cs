using AfterSeoul.Core;
using UnityEngine;

namespace AfterSeoul.Unity.UI
{
    /// <summary>
    /// 작업대 미니게임 한 판. 한 판이 공정 한 단계다.
    ///
    /// <para>결과는 0~1 점수 하나로만 바깥에 나간다 — <c>Session.AdvanceWork(score)</c> 가
    /// 그걸 품질로 바꾼다. 그래서 게임을 몇 개를 만들든 규칙 쪽은 손대지 않는다.</para>
    ///
    /// <para><b>입력은 누름/뗌으로 받는다.</b> 탭만 받으면 '누르고 있다가 뗀다'는 동작을
    /// 표현할 수 없고, 그러면 결국 전부 같은 게임이 된다.</para>
    /// </summary>
    public abstract class Minigame
    {
        protected RectTransform Host;

        /// <summary>이번 판이 끝났는가. 화면이 매 프레임 이걸 보고 결과를 거둔다.</summary>
        public bool Resolved { get; private set; }

        public float Score { get; private set; }

        /// <summary>왜 이 점수인지 한 마디. "빗나감" 과 "과압" 은 다른 실수다.</summary>
        public string ResultText { get; private set; }

        /// <summary>어떤 게임인가. 문구는 <see cref="Minigames"/> 가 갖고 있다 —
        /// 시작하기 전에도 보여줘야 해서, 인스턴스가 없을 때도 읽을 수 있어야 한다.</summary>
        public abstract MinigameKind Kind { get; }

        public void Mount(RectTransform host)
        {
            Host = host;
            Ui.Clear(host);
            Build();
        }

        /// <summary>이 게임이 쓰는 것들을 <see cref="Host"/> 안에 만든다.</summary>
        protected abstract void Build();

        /// <summary>한 판 시작. <paramref name="stage"/> 는 이번이 몇 번째 공정인지 (0부터).</summary>
        public void Begin(int stage)
        {
            Resolved = false;
            Score = 0f;
            ResultText = null;
            Start(stage < 0 ? 0 : stage);
        }

        protected abstract void Start(int stage);

        public abstract void Tick(float deltaTime);

        public virtual void Press() { }
        public virtual void Release() { }

        protected void Finish(float score, string text)
        {
            if (Resolved) return;
            Score = Mathf.Clamp01(score);
            ResultText = text;
            Resolved = true;
        }

        /// <summary>단계 배열에서 안전하게 꺼낸다 — 4단계 레시피가 3칸짜리 표를 넘어서지 않게.</summary>
        protected static float At(float[] table, int stage) =>
            table[stage >= table.Length ? table.Length - 1 : stage];

        // ── 공용 조각 ────────────────────────────────────────────

        /// <summary>가로 막대 하나. 타이밍/힘주기가 같은 모양을 쓴다 — 눈이 자리를 외운다.</summary>
        protected RectTransform Bar(string name)
        {
            var bar = Ui.Rect(name, Host);
            Ui.Stretch(bar, 0f, 0f, 34f, 34f);
            var img = bar.gameObject.AddComponent<UnityEngine.UI.Image>();
            img.color = Theme.Bg;
            img.raycastTarget = false;
            return bar;
        }

        protected static RectTransform Block(string name, Transform parent, Color color)
        {
            var rt = Ui.Rect(name, parent);
            var img = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }

        /// <summary>막대 위에서 [min,max] 구간을 차지하게 놓는다.</summary>
        protected static void Span(RectTransform rt, float min, float max)
        {
            rt.anchorMin = new Vector2(min, 0f);
            rt.anchorMax = new Vector2(max, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>막대 위 한 점에 두께 <paramref name="halfWidth"/>×2 로 놓는다.</summary>
        protected static void Pin(RectTransform rt, float at, float halfWidth)
        {
            rt.anchorMin = new Vector2(at, 0f);
            rt.anchorMax = new Vector2(at, 1f);
            rt.offsetMin = new Vector2(-halfWidth, 0f);
            rt.offsetMax = new Vector2(halfWidth, 0f);
        }

        /// <summary>구간 안에서 가장자리 0.5 ~ 정중앙 1.0. 밖이면 0.</summary>
        protected static float Closeness(float distance, float half) =>
            distance > half ? 0f : 0.5f + 0.5f * (1f - distance / half);
    }

    /// <summary>종류로 게임을 만든다. 여기 한 줄 추가하는 것이 새 미니게임을 붙이는 전부다.</summary>
    public static class MinigameFactory
    {
        public static Minigame Create(MinigameKind kind)
        {
            switch (kind)
            {
                case MinigameKind.Hold: return new HoldGame();
                case MinigameKind.Inspect: return new InspectGame();
                default: return new TimingGame();
            }
        }
    }
}
