using NUnit.Framework;
using UnityEngine;
using AfterSeoul.Core;
using AfterSeoul.Unity.UI;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// 미니게임을 <b>실제로 한 판씩 쳐 본다.</b>
    ///
    /// <para>이걸 만든 이유: 힘주기를 붙였을 때 '시작 탭'과 '누르기'를 따로 받는 바람에,
    /// 예전 버릇대로 툭 치면 0 에서 손을 뗀 셈이 되어 곧바로 실패하고 막대는 꿈쩍도 하지 않았다.
    /// 규칙(<c>Workbench</c>)은 멀쩡했고 테스트도 전부 통과했는데 손으로 만지면 고장나 있었다 —
    /// 손가락이 닿는 층에도 테스트가 필요하다는 뜻이다.</para>
    ///
    /// <para>화면 없이 돌린다. 게임은 MonoBehaviour 가 아니고 자기 것만 <c>Host</c> 안에
    /// 만들기 때문에, 빈 <see cref="RectTransform"/> 하나만 주면 그대로 돌아간다.</para>
    /// </summary>
    [TestFixture]
    public class MinigamePlayTests
    {
        private const float Frame = 1f / 60f;

        private GameObject _host;
        private RectTransform _hostRect;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("MinigameHost", typeof(RectTransform));
            _hostRect = (RectTransform)_host.transform;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_host);

        /// <summary>
        /// 판마다 새 자리를 준다. 같은 자리에 다시 붙이면 <c>Ui.Clear</c> 가 <c>Object.Destroy</c> 를
        /// 부르는데, 그건 에디트 모드에서 쓸 수 없다 (DestroyImmediate 를 요구한다).
        /// </summary>
        private Minigame Start(MinigameKind kind, int stage = 0)
        {
            var slot = new GameObject("Slot", typeof(RectTransform));
            slot.transform.SetParent(_hostRect, false);

            var game = MinigameFactory.Create(kind);
            game.Mount((RectTransform)slot.transform);
            game.Begin(stage);
            return game;
        }

        /// <summary>끝날 때까지(또는 한계까지) 프레임을 흘린다. 실제 경과 시간을 돌려준다.</summary>
        private static float Run(Minigame game, float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds && !game.Resolved)
            {
                game.Tick(Frame);
                elapsed += Frame;
            }
            return elapsed;
        }

        // ── 어느 게임도 작업대를 멈춰 세우면 안 된다 ───────────────

        /// <summary>
        /// <b>제일 중요하다.</b> 한 판을 시작했으면 손가락 한 번으로 반드시 끝나야 한다.
        /// 끝나지 않으면 공정이 영원히 그 단계에 머물고, 취소 버튼은 작업 중에 숨어 있다.
        /// </summary>
        [Test]
        public void EveryGame_EndsFromASinglePressAndRelease()
        {
            foreach (var kind in Minigames.All)
            {
                var game = Start(kind);

                // 사람처럼 친다 — 조금 보다가, 누르고, 잠깐 있다가 뗀다.
                Run(game, 0.9f);
                if (!game.Resolved) game.Press();
                Run(game, 0.6f);
                if (!game.Resolved) game.Release();
                Run(game, 0.2f);

                Assert.IsTrue(game.Resolved, $"{Minigames.LabelOf(kind)}: 한 판이 끝나지 않는다");
                Assert.IsNotEmpty(game.ResultText, $"{Minigames.LabelOf(kind)}: 결과 문구가 없다");
                Assert.That(game.Score, Is.InRange(0f, 1f));
            }
        }

        /// <summary>손을 아예 안 대도 영원히 붙잡고 있으면 안 된다 — 시간제한이 있는 게임은 스스로 끝난다.</summary>
        [Test]
        public void TimedGames_GiveUpOnTheirOwn()
        {
            var inspect = Start(MinigameKind.Inspect);
            Run(inspect, 10f);
            Assert.IsTrue(inspect.Resolved, "골라내기는 검사대가 다 지나가면 끝나야 한다");
            Assert.AreEqual(0f, inspect.Score, "안 누르고 통과하면 실패다");

            // 힘주기는 누른 채로 버텨도 끝난다 (과압).
            var hold = Start(MinigameKind.Hold);
            hold.Press();
            Run(hold, 10f);
            Assert.IsTrue(hold.Resolved, "힘주기는 넘기면 스스로 끝나야 한다");
            Assert.AreEqual(0f, hold.Score);
        }

        /// <summary>타이밍은 시간으로 끝나지 않는다 — 마커가 계속 오간다. 재촉하는 게임이 아니다.</summary>
        [Test]
        public void Timing_WaitsForYou()
        {
            var game = Start(MinigameKind.Timing);
            Run(game, 8f);

            Assert.IsFalse(game.Resolved, "타이밍은 안 누르면 계속 기다려야 한다");
        }

        // ── 힘주기 ───────────────────────────────────────────────

        /// <summary>
        /// 누르고 있으면 실제로 차오르고, 목표에서 떼면 좋은 점수가 나온다.
        /// <b>이게 무너져 있었다</b> — 시작 탭이 눌림을 삼켜서 막대가 아예 안 움직였다.
        /// </summary>
        [Test]
        public void Hold_FillsWhileHeld_AndScoresWhenReleasedInTime()
        {
            var game = Start(MinigameKind.Hold);
            game.Press();

            // 목표는 0.40~0.92 사이, 속도는 0.50/초. 끝날 때(과압)까지 눌러 보고
            // 그 직전 어딘가에서 뗐으면 점수가 나왔어야 한다는 걸 역으로 확인한다.
            float held = Run(game, 5f);

            Assert.IsTrue(game.Resolved, "계속 누르면 언젠가 과압으로 끝난다");
            Assert.Greater(held, 0.7f, "목표가 0.40 지점보다 앞에 있으면 반응할 틈이 없다");
            Assert.Less(held, 2.5f, "너무 오래 눌러야 하면 손가락이 먼저 지친다");
        }

        /// <summary>누른 적이 없는데 떼는 신호가 와도(시작 탭의 뗌) 판이 끝나면 안 된다.</summary>
        [Test]
        public void Hold_IgnoresAReleaseWithoutAPress()
        {
            var game = Start(MinigameKind.Hold);

            game.Release();

            Assert.IsFalse(game.Resolved,
                "누른 적 없는 뗌으로 끝나면, 시작 탭에서 손을 떼는 순간 0 점으로 실패한다");
        }

        // ── 골라내기 ─────────────────────────────────────────────

        /// <summary>시작하자마자 검사대가 돌면 눈이 따라가지 못한다. 준비 시간이 있어야 한다.</summary>
        [Test]
        public void Inspect_GivesYouAMomentBeforeItStarts()
        {
            var game = Start(MinigameKind.Inspect);

            game.Press();   // 시작 버튼에서 손이 채 떨어지기 전

            Assert.IsFalse(game.Resolved,
                "준비 시간에 누른 것으로 실패하면, 시작 탭이 곧 실패가 된다");
        }

        [Test]
        public void Inspect_FailsWhenYouPickAGoodOne()
        {
            var game = Start(MinigameKind.Inspect);

            Run(game, 0.6f);        // 준비 시간(0.5초)이 지나 첫 칸이 열린다
            game.Press();           // 첫 칸은 절대 불량이 아니다

            Assert.IsTrue(game.Resolved);
            Assert.AreEqual(0f, game.Score, "멀쩡한 것을 골라내면 실패다");
        }

        // ── 단계 ─────────────────────────────────────────────────

        /// <summary>레시피 단계 수가 난이도 표보다 길어도 넘어가지 않는다 (4단계 RCP_AMMO).</summary>
        [Test]
        public void LaterStages_DoNotOverrunTheDifficultyTable()
        {
            foreach (var kind in Minigames.All)
                for (int stage = 0; stage < 8; stage++)
                    Assert.DoesNotThrow(() => Start(kind, stage),
                        $"{Minigames.LabelOf(kind)} {stage}단계에서 터진다");
        }

        [Test]
        public void EveryKind_HasAPromptAndHint()
        {
            foreach (var kind in Minigames.All)
            {
                Assert.IsNotEmpty(Minigames.PromptOf(kind));
                Assert.IsNotEmpty(Minigames.HintOf(kind));
                Assert.AreEqual(kind, Start(kind).Kind, "만들어진 게임이 종류와 다르다");
            }
        }
    }
}
