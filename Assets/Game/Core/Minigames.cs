

namespace AfterSeoul.Core
{
    /// <summary>
    /// 작업대 미니게임의 종류.
    ///
    /// <para>여기(엔진 없는 쪽)에 두는 이유는 <b>어떤 단계가 어떤 게임인지가 데이터</b>이기 때문이다.
    /// 실제로 마커를 움직이고 손가락을 받는 건 UI 쪽 일이지만, "탄피 선별은 눈으로 골라내는 것이고
    /// 탄두 압착은 힘을 주는 것"이라는 판단은 레시피에 속한다. 그래야 테스트가
    /// "한 레시피 안에서 같은 동작만 반복되지 않는다"를 검사할 수 있다.</para>
    /// </summary>
    public enum MinigameKind
    {
        /// <summary>지나가는 마커를 목표 구간에서 친다. 예측과 정확도.</summary>
        Timing,

        /// <summary>누르고 있으면 차오르고, 목표 구간에서 뗀다. 넘기면 망친다.</summary>
        Hold,

        /// <summary>검사 헤드가 훑는 동안 불량품이 보이면 누른다. 반응.</summary>
        Inspect,
        Signal,
        Vault,
    }

    /// <summary>
    /// 단계별 미니게임 배정.
    ///
    /// <para><b>같은 동작을 네 번 반복하면 그건 미니게임이 아니라 절차다.</b>
    /// 레시피가 단계마다 다른 게임을 지정하고, 지정하지 않았으면 돌려가며 배정한다 —
    /// 데이터를 대충 넣어도 최소한 단조롭지는 않게.</para>
    /// </summary>
    public static class Minigames
    {
        public static readonly MinigameKind[] All =
        {
            MinigameKind.Timing, MinigameKind.Hold, MinigameKind.Inspect, MinigameKind.Signal, MinigameKind.Vault,
        };

        public static string IdOf(MinigameKind kind)
        {
            switch (kind)
            {
                case MinigameKind.Vault: return "vault";
                case MinigameKind.Signal: return "signal";
                case MinigameKind.Hold: return "hold";
                case MinigameKind.Inspect: return "inspect";
                default: return "timing";
            }
        }

        /// <summary>버튼과 진행 줄에 붙는 짧은 이름. 무슨 동작을 할 차례인지 먼저 알려준다.</summary>
        public static string LabelOf(MinigameKind kind)
        {
            switch (kind)
            {
                case MinigameKind.Vault: return Loc.Text("지하 금고 탐색");
                case MinigameKind.Signal: return Loc.Text("배송망 해킹");
                case MinigameKind.Hold: return Loc.Text("힘주기");
                case MinigameKind.Inspect: return Loc.Text("골라내기");
                default: return Loc.Text("타이밍");
            }
        }

        /// <summary>
        /// 작업 버튼에 붙는 문구. <b>시작하기 전에도</b> 이걸 보여줘야 한다 —
        /// 무슨 손짓을 할지 모르고 누르면 첫 판은 그냥 버린다.
        /// </summary>
        public static string PromptOf(MinigameKind kind)
        {
            switch (kind)
            {
                case MinigameKind.Vault: return Loc.Text("상자를 열어 보세요");
                case MinigameKind.Signal: return Loc.Text("누르면 오른쪽 · 떼면 왼쪽");
                case MinigameKind.Hold: return Loc.Text("누르고 계세요");
                case MinigameKind.Inspect: return Loc.Text("불량을 누르세요");
                default: return Loc.Text("지금!");
            }
        }

        public static string HintOf(MinigameKind kind)
        {
            switch (kind)
            {
                case MinigameKind.Vault:
                    return Loc.Text("안전한 상자를 찾아 물자를 모으세요.");
                case MinigameKind.Signal:
                    return Loc.Text("밝은 구간에 바늘을 유지해 물자를 확보하세요.");
                case MinigameKind.Hold:
                    return Loc.Text("누르면 차오릅니다. 구간에 닿으면 떼세요 — 넘기면 망칩니다");
                case MinigameKind.Inspect:
                    return Loc.Text("검사대가 한 칸씩 열립니다. 불량이 보이면 누르세요");
                default:
                    return Loc.Text("마커가 구간에 들어오면 누르세요");
            }
        }

        public static string[] LessonOf(MinigameKind kind)
        {
            switch (kind) {
                case MinigameKind.Signal: return new[] {
                    Loc.Text("누르면 바늘이 오른쪽으로, 떼면 왼쪽으로 움직입니다."),
                    Loc.Text("밝은 구간 안에 머물면 물자를 모읍니다. 밖에 오래 있으면 연결이 끊깁니다."),
                    Loc.Text("물자를 모으면 지금 받거나 더 도전할 수 있습니다. 실패하면 추가 물자는 잃지만 기본 물자는 받습니다.") };
                case MinigameKind.Vault: return new[] {
                    Loc.Text("닫힌 상자를 눌러 여세요. 첫 번째 상자는 항상 안전합니다."),
                    Loc.Text("열린 상자의 숫자는 주변 8칸의 위험 개수입니다. 0 옆은 안전하고, 숫자가 클수록 조심하세요."),
                    Loc.Text("안전한 상자 3개부터 물자를 받을 수 있습니다. 더 열면 보상이 늘지만 위험을 밟으면 추가 물자를 잃습니다. ‘안전하게 열기’는 두 번 쓸 수 있습니다.") };
                case MinigameKind.Hold: return new[] {
                    Loc.Text("작업 버튼을 누르고 있으면 막대가 차오릅니다."),
                    Loc.Text("밝은 구간에 닿았을 때 손을 떼세요."),
                    Loc.Text("가운데에 가깝게 맞출수록 좋은 품질이 나옵니다. 구간을 넘기기 전에 떼는 것이 요령입니다.") };
                case MinigameKind.Inspect: return new[] {
                    Loc.Text("물건이 차례로 나타납니다. 먼저 어떤 물건인지 살펴보세요."),
                    Loc.Text("불량이 나타났을 때 작업 버튼을 누르세요. 정상 물건은 그대로 보내세요."),
                    Loc.Text("불량을 빨리 찾아낼수록 품질이 좋아집니다.") };
                default: return new[] {
                    Loc.Text("바늘이 좌우로 움직입니다. 밝은 구간을 눈으로 따라가세요."),
                    Loc.Text("바늘이 밝은 구간 안에 들어왔을 때 작업 버튼을 누르세요."),
                    Loc.Text("가운데에 가깝게 맞출수록 품질이 좋아집니다. 서두르지 않아도 됩니다.") };
            }
        }

        /// <summary>
        /// 공정을 시작시킨 그 누름이 곧 한 판의 시작인가.
        ///
        /// <para>힘주기가 그렇다. 누르고 있는 것 자체가 플레이라서, '시작' 탭과 '누르기'를
        /// 따로 받으면 시작 탭이 버려지고 — 예전 버릇대로 툭 치면 0 에서 손을 뗀 것이 되어
        /// 곧바로 실패한다. 반대로 타이밍·골라내기는 시작하자마자 판정하면 무조건 빗나간다.</para>
        /// </summary>
        public static bool StartsOnPress(MinigameKind kind) => kind == MinigameKind.Hold;

        public static bool TryParse(string id, out MinigameKind kind)
        {
            switch (id)
            {
                case "vault": kind = MinigameKind.Vault; return true;
                case "signal": kind = MinigameKind.Signal; return true;
                case "timing": kind = MinigameKind.Timing; return true;
                case "hold": kind = MinigameKind.Hold; return true;
                case "inspect": kind = MinigameKind.Inspect; return true;
                default: kind = MinigameKind.Timing; return false;
            }
        }

        /// <summary>
        /// 이 단계에서 할 게임. 레시피가 지정했으면 그것, 아니면 순서대로 돌린다.
        /// </summary>
        public static MinigameKind KindFor(RecipeDef recipe, int stepIndex)
        {
            if (stepIndex < 0) stepIndex = 0;

            if (recipe != null && recipe.StepGames != null && stepIndex < recipe.StepGames.Length)
            {
                MinigameKind parsed;
                if (TryParse(recipe.StepGames[stepIndex], out parsed)) return parsed;
            }

            return All[stepIndex % All.Length];
        }

        /// <summary>
        /// 붙어 있는 두 단계가 같은 게임인가. 데이터 테스트가 이걸 본다 —
        /// 연달아 같은 동작이 나오면 단계를 나눈 의미가 없다.
        /// </summary>
        public static bool HasAdjacentRepeat(RecipeDef recipe)
        {
            if (recipe == null || recipe.ManualSteps < 2) return false;

            var previous = KindFor(recipe, 0);
            for (int i = 1; i < recipe.ManualSteps; i++)
            {
                var current = KindFor(recipe, i);
                if (current == previous) return true;
                previous = current;
            }
            return false;
        }
    }
}
