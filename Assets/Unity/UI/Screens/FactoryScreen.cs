using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Factory;
using AfterSeoul.Inventory;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI.Screens
{
    /// <summary>
    /// 공장. GDD §6 — <b>손으로 물건을 만든다.</b>
    ///
    /// <para><b>미니게임은 연타가 아니다.</b> GDD §9 가 명시적으로 금지한다.
    /// 한 번의 성공이 공정을 한 단계 민다.</para>
    ///
    /// <para><b>단계마다 손이 하는 일이 다르다.</b> 같은 판정을 네 번 반복하면 그건 놀이가 아니라
    /// 절차다. 레시피가 단계별로 게임을 지정하고(<c>Minigames.KindFor</c>) 화면은 그때그때
    /// 맞는 것을 만들어 붙인다 — 타이밍(맞추기) / 힘주기(누르고 떼기) / 골라내기(반응).</para>
    ///
    /// <para><b>화면이 답해야 하는 것은 "무엇을, 얼마나 만들었나"다.</b> 예전에는 두드리면
    /// 돈만 생기고 아무것도 쌓이지 않아서 놀이가 아니라 작업으로 느껴졌다. 이제 만들 물건을
    /// 고르고, 공정 칸이 하나씩 차고, 마지막에 물건이 나온다. 품질은 보수 배수가 아니라
    /// <b>산출 개수</b>를 바꾼다 — 잘해서 하나 더 나온 것이 눈에 보여야 한다.</para>
    /// </summary>
    public sealed class FactoryScreen : ScreenBase
    {
        public override string TabName => "공장";
        public override IconSet.TabGlyph Glyph => IconSet.TabGlyph.Factory;
        public override string Title => AfterSeoul.Core.Loc.Text("공장");

        // ── 작업대 ──
        private Text _makingLabel;      // 무엇을 만드는 중인가
        private RectTransform _steps;   // 공정 칸들
        private Text _stageLabel;
        private Text _resultLabel;
        private RectTransform _gameHost;
        private Button _actionButton;
        private Button _cancelButton;
        private Button _helpButton;
        private RectTransform _gameHelp;

        /// <summary>만들 물건 목록. 작업 중에는 비운다.</summary>
        private RectTransform _picker;

        private RectTransform _queueBody;

        /// <summary>작업대 성장 — 업그레이드와 보조 인력. GDD §6 의 성장 경로가 여기 붙는다.</summary>
        private RectTransform _stationBody;
        private Button _quickUpgrade, _quickAssistant;
        private Text _upgradeFeedback;
        private RectTransform _completion;

        // 미니게임 상태는 한 단계 분량만 여기 있다. 공정 진행은 세이브에 있다.
        private Minigame _game;
        private bool _running;
        internal bool IsWorking => _running || _celebrate > 0 || _gameHelp != null;

        /// <summary>완성 문구를 남겨 둘 시간. 바로 지우면 무엇이 나왔는지 못 읽는다.</summary>
        private float _celebrate;

        /// <summary>마지막으로 그린 공정 칸 수. 방금 한 칸이 찼는지 알려면 이전 값이 있어야 한다.</summary>
        private int _shownSteps = -1;

        /// <summary>
        /// 제작 큐의 살아 있는 줄들. 매 프레임 여기 막대를 민다.
        ///
        /// <para><see cref="Refresh"/> 는 정산이 끝났을 때나 조작 뒤에만 불린다 — 5초에 한 번꼴이다.
        /// 그 간격으로 남은 시간을 갱신하면 숫자가 뚝뚝 끊겨서 멈춘 것처럼 보인다.</para>
        /// </summary>
        private readonly List<QueueRow> _queueRows = new List<QueueRow>();

        private sealed class QueueRow
        {
            public CraftJob Job;
            public ProgressBar Bar;
            public Text Time;
            public RectTransform Surface;

            /// <summary>다 됐다고 이미 표시했는가. 매 프레임 같은 글자를 다시 넣지 않으려고 둔다.</summary>
            public bool MarkedDone;
        }

        protected override void Build()
        {
            var host = Ui.Rect("Host", Root);
            Ui.Stretch(host, Theme.Gutter, Theme.Gutter, 16f, 16f);

            Ui.Column(host, 12f);
            BuildWorkbench(host);
            BuildUpgradeDock(host);
            var list = Ui.ScrollList("Scroll", host, out var scroll, 16f);
            Ui.Size(scroll.gameObject, flexHeight: 1f);

            _picker = Ui.Rect("Picker", list);
            Ui.Column(_picker, 10f);

            // 높이를 고정하지 않는다. 작업대를 올리면 큐 칸이 최대 4개까지 늘어나는데,
            // 230px 에 묶여 있으면 세 번째 줄부터 카드 밖으로 새어 나간다.
            Ui.Card(list, AfterSeoul.Core.Loc.Text("제작 큐"), out _queueBody);

            Ui.Card(list, AfterSeoul.Core.Loc.Text("작업대"), out _stationBody);
        }

        private RectTransform _workbenchCard;

        private void BuildUpgradeDock(RectTransform parent)
        {
            var dock = Ui.Surface("UpgradeDock", parent, Theme.Panel, Theme.EdgeLive);
            Ui.Size(dock.gameObject, 210f, flexHeight: 0);
            Ui.Column(dock, 8f, new RectOffset(16, 16, 12, 12));
            _upgradeFeedback = Ui.Label("UpgradeFeedback", dock, Loc.Text("업그레이드 · 한 번 터치로 바로 적용"),
                Theme.FontSmall, TextAnchor.MiddleLeft, Theme.Info);
            Ui.Size(_upgradeFeedback.gameObject, 34f, flexHeight: 0);
            var row = Ui.Rect("Purchases", dock);
            Ui.Row(row, 12f);
            Ui.Size(row.gameObject, 144f, flexHeight: 0);
            _quickUpgrade = Ui.Button("QuickUpgrade", row, "", OnUpgrade, Theme.AccentDim, Theme.FontSmall);
            Ui.Size(_quickUpgrade.gameObject, flexWidth: 1f);
            _quickAssistant = Ui.Button("QuickAssistant", row, "", OnHireAssistant, Theme.PanelAlt, Theme.FontSmall);
            Ui.Size(_quickAssistant.gameObject, flexWidth: 1f);
        }

        private void RefreshUpgradeDock()
        {
            var save = Session.Save;
            int level = save.Factory.StationLevel;
            string blocked = Station.UpgradeBlockReason(save, Session.Data);
            double speed = (Session.Data.Balance.Station ?? new StationTuning()).SpeedPerLevel;
            Ui.SetButtonLabel(_quickUpgrade, Station.IsMaxLevel(save, Session.Data)
                ? Loc.Text("작업대") + " Lv." + level + " · MAX"
                : Loc.Text("작업대") + $" Lv.{level} → {level + 1} · " + Theme.Won(Station.UpgradeCost(save, Session.Data)) + "\n" +
                  Loc.Text("제작 큐 {0}칸", level) + $" → {level + 1} · " +
                  Loc.Text("제작 속도 {0:P0} 단축", 1 - System.Math.Pow(speed, level)) +
                  (blocked == null ? "" : "\n" + blocked));
            _quickUpgrade.interactable = blocked == null;
            string assistantBlock = Station.HireAssistantBlockReason(save, Session.Data);
            _quickAssistant.gameObject.SetActive(Station.AssistantsUnlocked(save, Session.Data));
            Ui.SetButtonLabel(_quickAssistant, Loc.Text("보조 인력") + $" {save.Factory.AutoLevel} → " +
                Mathf.Min(save.Factory.AutoLevel + 1, Station.MaxAssistants(Session.Data)) + "\n" +
                Loc.Text("자리를 비워도 계속 제작") + "\n" +
                (assistantBlock ?? Theme.Won(Station.AssistantCost(save, Session.Data))));
            _quickAssistant.interactable = assistantBlock == null;
        }

        private void BuildWorkbench(RectTransform parent)
        {
            RectTransform body;
            var card = Ui.Card(parent, AfterSeoul.Core.Loc.Text("작업대"), out body);
            _workbenchCard = card;
            Ui.Size(card.gameObject, 560f, flexHeight: 0);

            // 무엇을 만드는 중인가 — 이 화면에서 제일 먼저 읽혀야 하는 한 줄.
            _makingLabel = Ui.Label("Making", body, "", Theme.FontHeading,
                TextAnchor.MiddleLeft, Theme.Text);
            Ui.Size(_makingLabel.gameObject, 42f);

            // 공정 칸. 한 단계 끝날 때마다 하나씩 채워진다 — 쌓이는 것이 보이는 자리다.
            _steps = Ui.Rect("Steps", body);
            Ui.Size(_steps.gameObject, 34f, flexHeight: 0);
            Ui.Row(_steps, 8f);

            _stageLabel = Ui.Label("Stage", body, "", Theme.FontSmall,
                TextAnchor.MiddleLeft, Theme.TextDim);
            Ui.Size(_stageLabel.gameObject, 32f);

            // 미니게임이 자기 것을 여기에 만든다. 종류마다 생긴 게 다르다.
            _gameHost = Ui.Rect("Game", body);
            Ui.Size(_gameHost.gameObject, 118f);

            _resultLabel = Ui.Label("Result", body, "", Theme.FontBody,
                TextAnchor.MiddleCenter, Theme.TextDim);
            _resultLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Size(_resultLabel.gameObject, 64f);

            var buttons = Ui.Rect("Buttons", body);
            Ui.Size(buttons.gameObject, 80f, flexHeight: 0);
            Ui.Row(buttons, 12f);

            // 버튼은 onClick 을 쓰지 않는다 — 누름과 뗌을 따로 받아야 '힘주기'가 성립한다.
            _actionButton = Ui.Button("Action", buttons, AfterSeoul.Core.Loc.Text("작업"), () => { }, Theme.Accent);
            Ui.Size(_actionButton.gameObject, flexWidth: 1f);

            var press = _actionButton.gameObject.AddComponent<PressButton>();
            press.Pressed = OnPress;
            press.Released = OnRelease;

            _cancelButton = Ui.Button("Cancel", buttons, AfterSeoul.Core.Loc.Text("취소"), OnCancel, Theme.Line, Theme.FontSmall);
            Ui.Size(_cancelButton.gameObject, width: 200f, flexWidth: 0f);
            _helpButton = Ui.Button("GameHelp", buttons, Loc.Text("방법"), ShowGameHelp, Theme.PanelAlt, Theme.FontSmall);
            Ui.Size(_helpButton.gameObject, width: 150f, flexWidth: 0);
        }

        // ── 그리기 ───────────────────────────────────────────────

        public override void Refresh()
        {
            if (_makingLabel == null) return;
            Workbench.NormalizeDeliveryWork(Session.Save, Session.Data);

            var bench = Session.Save.Factory.Workbench;
            var recipe = bench.IsIdle ? null : Session.Data.GetRecipe(bench.RecipeId);

            _workbenchCard.gameObject.SetActive(recipe != null || _celebrate > 0);
            if (recipe == null) DropGame();
            bool signalStage = recipe != null && Minigames.KindFor(recipe, bench.StepsDone) == MinigameKind.Signal;
            bool vaultStage = recipe != null && Minigames.KindFor(recipe, bench.StepsDone) == MinigameKind.Vault;
            float gameHeight = vaultStage ? 552f : signalStage ? 334f : 118f;
            Ui.Size(_gameHost.gameObject, gameHeight);
            bool single = recipe != null && recipe.ManualSteps == 1;
            _steps.gameObject.SetActive(!single && recipe != null);
            _stageLabel.gameObject.SetActive(!single || !_running);
            _resultLabel.gameObject.SetActive(!(signalStage || vaultStage) || !_running || _celebrate > 0);
            Ui.Size(_workbenchCard.gameObject, gameHeight + (single && _running ? 228f : 400f));

            RefreshHeadline(bench, recipe);
            RefreshSteps(bench, recipe);
            RefreshPicker(bench);
            RefreshQueue();
            RefreshStation();
            RefreshUpgradeDock();
            _queueBody.parent.gameObject.SetActive(!_running);
            _stationBody.parent.gameObject.SetActive(!_running);

            _cancelButton.gameObject.SetActive(recipe != null && !_running);
            _helpButton.gameObject.SetActive(recipe != null);
            _actionButton.gameObject.SetActive(!(_running && (vaultStage || (_game != null && !_game.WantsActionButton))));
            Ui.SetButtonLabel(_actionButton, ActionLabel(bench, recipe));
            _actionButton.interactable = recipe != null && !(_running && vaultStage);
        }

        /// <summary>
        /// 버튼 문구.
        ///
        /// <para>시작하기 전에도 <b>무슨 손짓을 할지</b>를 보여준다. 누름이 곧 플레이인 게임
        /// (힘주기)에서 "작업"이라고만 써두면, 눌렀다 떼는 순간 한 판이 끝나 버린다.</para>
        /// </summary>
        private string ActionLabel(WorkbenchState bench, RecipeDef recipe)
        {
            if (recipe == null) return AfterSeoul.Core.Loc.Text("만들 것을 고르세요");

            var kind = Minigames.KindFor(recipe, bench.StepsDone);
            if (_running) return Minigames.PromptOf(kind);

            return Minigames.StartsOnPress(kind)
                ? Minigames.PromptOf(kind)
                : AfterSeoul.Core.Loc.Text("작업 — {0}", Minigames.LabelOf(kind));
        }

        /// <summary>작업대가 비면 만들어둔 것도 치운다. 남겨두면 무엇에 대한 화면인지 헷갈린다.</summary>
        private void DropGame()
        {
            if (_game == null) return;
            _game = null;
            _running = false;
            Ui.Clear(_gameHost);
        }

        private void RefreshHeadline(WorkbenchState bench, RecipeDef recipe)
        {
            if (recipe == null)
            {
                _makingLabel.text = AfterSeoul.Core.Loc.Text("작업대가 비어 있습니다");
                _makingLabel.color = Theme.TextFaint;
                _stageLabel.text = AfterSeoul.Core.Loc.Text("아래에서 만들 물건을 고르세요");
                if (_celebrate <= 0f) _resultLabel.text = "";
                return;
            }

            int good = Workbench.OutputCountFor(Session.Data, recipe, CraftQuality.Good);
            _makingLabel.text = Loc.ItemName(recipe.OutputItemId) + " · " + Loc.Text("기본 {0}개", good);
            _makingLabel.color = Theme.Text;

            // 지금(또는 다음) 단계가 무슨 일인지 — 이름과 동작을 같이 보여준다.
            int step = bench.StepsDone;
            string name = Workbench.StepName(recipe, step);
            string game = Minigames.LabelOf(Minigames.KindFor(recipe, step));

            _stageLabel.text = _running
                ? $"{step + 1}/{recipe.ManualSteps}  {name} · {game}"
                : step == 0
                    ? AfterSeoul.Core.Loc.Text("{0}단계 · 첫 공정은 {1}({2})", recipe.ManualSteps, name, game)
                    : AfterSeoul.Core.Loc.Text("{0}단계 남음 · 다음은 {1}({2})", recipe.ManualSteps - step, name, game);

            // 시작하기 전에 무엇을 하는 게임인지 읽을 수 있어야 한다. 시작한 뒤에 알려주면
            // 골라내기처럼 몇 초 만에 끝나는 게임은 첫 판을 통째로 버리게 된다.
            if (!_running && _celebrate <= 0f)
            {
                _resultLabel.text = Minigames.HintOf(Minigames.KindFor(recipe, step));
                _resultLabel.color = Theme.TextFaint;
            }
        }

        /// <summary>
        /// 공정 칸. 끝낸 단계는 점수 색으로 차고, 지금 단계는 밝게, 남은 단계는 비어 있다.
        /// 두드릴 때마다 칸이 하나씩 차는 것이 이 화면의 핵심 피드백이다.
        /// </summary>
        private void RefreshSteps(WorkbenchState bench, RecipeDef recipe)
        {
            Ui.Clear(_steps);

            if (recipe == null)
            {
                _shownSteps = -1;
                return;
            }

            int done = bench.Scores.Count;

            // 방금 한 칸이 찼는가. 처음 그리는 경우(-1)는 제외한다 — 화면을 켰을 뿐인데
            // 이미 끝나 있던 칸들이 축하하듯 터지면 무슨 일이 난 줄 안다.
            bool advanced = _shownSteps >= 0 && done > _shownSteps;
            _shownSteps = done;

            for (int i = 0; i < recipe.ManualSteps; i++)
            {
                var cell = Ui.Rect("S" + i, _steps);
                Ui.Size(cell.gameObject, flexWidth: 1f);

                var img = cell.gameObject.AddComponent<Image>();
                img.sprite = Skin.Pill;
                img.type = Image.Type.Sliced;
                img.raycastTarget = false;

                Color target;
                if (i < done) target = ScoreColor(bench.Scores[i]);
                else if (i == bench.StepsDone && _running) target = Theme.AccentDim;
                else target = Theme.Line;

                img.color = target;

                // 지금 막 찬 칸만 한 번 번쩍이고 제 색으로 내려앉는다. 이 화면에서 쌓이는 것이
                // 보이는 유일한 자리라(GDD §6) 여기서 아무 일도 안 일어나면 두드림이 헛돈다.
                if (advanced && i == done - 1)
                {
                    img.color = Color.white;
                    Tween.Tint(img, target, 0.34f);
                    Tween.Punch(cell, 0.22f, 0.26f);
                }

                // 지금 하고 있는 칸은 조용히 숨을 쉰다. 어디를 보고 있어야 하는지의 표시다.
                if (i == bench.StepsDone && _running)
                    Tween.Loop(img, "breathe", 1.1f,
                        t => img.color = Color.Lerp(Theme.Line, Theme.Accent, Tween.PingPong(t)));
            }
        }

        private static Color ScoreColor(double score)
        {
            if (score <= 0.0) return Theme.Danger;
            if (score >= 0.92) return Theme.Safe;
            if (score >= 0.7) return Theme.Accent;
            return Theme.Warn;
        }

        /// <summary>만들 수 있는 것들. 작업 중에는 숨긴다 — 고르는 화면과 하는 화면을 섞지 않는다.</summary>
        private void RefreshPicker(WorkbenchState bench)
        {
            Ui.Clear(_picker);
            if (!bench.IsIdle) return;

            var head = Ui.Label("PickHead", _picker, AfterSeoul.Core.Loc.Text("만들 물건"), Theme.FontHeading,
                TextAnchor.MiddleLeft, Theme.Accent);
            Ui.Size(head.gameObject, 54f);

            foreach (var recipe in Workbench.AvailableRecipes(Session.Save, Session.Data))
                BuildRecipeRow(recipe);
        }

        private void BuildRecipeRow(RecipeDef recipe)
        {
            string block = Workbench.StartBlockReason(Session.Save, Session.Data, recipe.Id);
            bool ok = block == null;
            string recipeId = recipe.Id;

            var btn = Ui.Button("R_" + recipe.Id, _picker, "", () => OnPick(recipeId),
                ok ? Theme.Panel : Theme.Line);
            btn.interactable = ok;
            Ui.Size(btn.gameObject, ok ? 132f : 170f);

            var col = Ui.Rect("Content", btn.transform);
            Ui.Stretch(col, 20f, 20f, 10f, 10f);
            Ui.Column(col, 2f);

            var head = Ui.Rect("Head", col);
            Ui.Size(head.gameObject, 46f);
            Ui.Row(head, 10f);

            Ui.Icon("Icon", head, Session.Data.GetItem(recipe.OutputItemId), 40f);

            int good = Workbench.OutputCountFor(Session.Data, recipe, CraftQuality.Good);
            var name = Ui.Label("Name", head,
                (recipe.Id == "RCP_VAULT" ? Loc.Text("상자에서 물자 찾기") : recipe.Id == "RCP_SALVAGE" ? Loc.Text("배송 물자 모으기") : $"{Loc.ItemName(recipe.OutputItemId)} ×{good}"), Theme.FontBody,
                TextAnchor.MiddleLeft, ok ? Theme.Text : Theme.TextFaint);
            Ui.Size(name.gameObject, flexWidth: 1f);

            var steps = Ui.Label("Steps", head, AfterSeoul.Core.Loc.Text("{0}단계", recipe.ManualSteps), Theme.FontSmall,
                TextAnchor.MiddleRight, Theme.TextFaint);
            Ui.Size(steps.gameObject, width: 160f, flexWidth: 0f);

            var inputs = Ui.Label("Inputs", col, recipe.Inputs.Length == 0 ? Loc.Text("무료로 시작") : InputsText(recipe), Theme.FontSmall,
                TextAnchor.MiddleLeft, Theme.TextDim);
            Ui.Size(inputs.gameObject, 38f);

            // "잘하면 더 나온다"를 고를 때부터 보여준다. 품질이 숨은 배수가 아니라 약속이 된다.
            int fail = Workbench.OutputCountFor(Session.Data, recipe, CraftQuality.Failed);
            int best = Workbench.OutputCountFor(Session.Data, recipe, CraftQuality.Excellent);
            string yieldText = (recipe.Id == "RCP_SALVAGE" || recipe.Id == "RCP_VAULT") ? Loc.Text("{0} {1}~{2}개 획득", Loc.ItemName(recipe.OutputItemId), good, good * 4)
                : Loc.Text("품질에 따라 {0}~{1}개", fail, best);
            var yield = Ui.Label("Yield", col, yieldText,
                Theme.FontSmall, TextAnchor.MiddleLeft, Theme.Info);
            Ui.Size(yield.gameObject, 36f);

            if (!ok)
            {
                var why = Ui.Label("Why", col, block, Theme.FontSmall,
                    TextAnchor.MiddleLeft, Theme.Warn);
                Ui.Size(why.gameObject, 36f);
            }

            BuildQueueButton(recipe, ok);
        }

        /// <summary>
        /// 같은 레시피를 기다려서 만드는 길. 손으로 하면 빠르고 품질이 좋지만 손이 묶이고,
        /// 큐에 넣으면 느리지만 그동안 다른 걸 한다 — 성장하면 이쪽으로 옮겨간다 (GDD §2).
        /// </summary>
        private void BuildQueueButton(RecipeDef recipe, bool hasInputs)
        {
            var save = Session.Save;
            bool roomLeft = FactorySystem.ActiveJobs(save) < FactorySystem.QueueCapacity(save, Session.Clock.UtcNow);
            bool ok = hasInputs && roomLeft;

            int minutes = (int)System.Math.Round(
                recipe.WorkSeconds * FactorySystem.SpeedDivisor(save, Session.Data) / 60.0);
            string recipeId = recipe.Id;

            var btn = Ui.Button("Q_" + recipe.Id, _picker,
                roomLeft ? AfterSeoul.Core.Loc.Text("큐에 넣기 · 약 {0}분", minutes) : AfterSeoul.Core.Loc.Text("큐가 가득 찼습니다"),
                () => OnEnqueue(recipeId), ok ? Theme.Line : Theme.Panel, Theme.FontSmall);
            btn.interactable = ok;
            Ui.Size(btn.gameObject, 66f);
        }

        private void OnEnqueue(string recipeId)
        {
            if (Session.EnqueueCraft(recipeId) == null)
            {
                Shell.Toast(AfterSeoul.Core.Loc.Text("큐에 넣지 못했습니다 — 재료나 빈 칸을 확인하세요"), 3f);
                return;
            }

            Sfx.Tap();
            Shell.AfterAction();
        }

        private static string StepFlowText(RecipeDef recipe)
        {
            var parts = new List<string>();
            for (int i = 0; i < recipe.ManualSteps; i++)
                parts.Add(Workbench.StepName(recipe, i));

            return string.Join(" → ", parts.ToArray());
        }

        private string InputsText(RecipeDef recipe)
        {
            if (recipe.Inputs.Length == 0) return AfterSeoul.Core.Loc.Text("재료 없음 — 폐자재에서 뽑아낸다");

            var parts = new List<string>();
            foreach (var input in recipe.Inputs)
            {
                int have = Warehouse.CountOf(Session.Save.Warehouse, input.ItemId);
                parts.Add($"{Loc.ItemName(input.ItemId)} {have}/{input.Count}");
            }
            return AfterSeoul.Core.Loc.Text("재료  ") + string.Join("   ", parts.ToArray());
        }

        private void RefreshQueue()
        {
            Ui.Clear(_queueBody);
            _queueRows.Clear();

            var save = Session.Save;
            var queue = save.Factory.Queue;

            var head = Ui.Label("Cap", _queueBody,
                AfterSeoul.Core.Loc.Text("{0}/{1}칸 사용 중", FactorySystem.ActiveJobs(save), FactorySystem.QueueCapacity(save, Session.Clock.UtcNow)),
                Theme.FontSmall, TextAnchor.MiddleLeft, Theme.TextDim);
            Ui.Size(head.gameObject, 40f);

            if (queue.Count == 0)
            {
                var none = Ui.Label("Empty", _queueBody,
                    AfterSeoul.Core.Loc.Text("아래 목록에서 '큐에 넣기'를 누르면 손을 안 대도 만들어집니다."),
                    Theme.FontSmall, TextAnchor.MiddleLeft, Theme.TextFaint);
                Ui.Size(none.gameObject, 44f);
                return;
            }

            for (int i = 0; i < queue.Count; i++) BuildQueueRow(queue[i], i);
            TickQueue();
        }

        /// <summary>
        /// 큐 한 줄 = 이름 + 남은 시간 + 막대.
        ///
        /// <para>막대를 붙인 이유: 세 줄이 동시에 돌아갈 때 "어느 게 먼저 끝나나"를 알려면
        /// 숫자 세 개를 읽고 비교해야 한다. 막대는 비교가 필요 없다 — 길이가 이미 답이다.</para>
        /// </summary>
        private void BuildQueueRow(CraftJob job, int index)
        {
            var recipe = Session.Data.GetRecipe(job.RecipeId);

            var surface = Ui.Surface("Q" + index, _queueBody, Theme.PanelAlt);
            Ui.Size(surface.gameObject, 84f);
            Ui.Column(surface, 6f, new RectOffset(16, 16, 10, 12));

            var head = Ui.Rect("Head", surface);
            Ui.Size(head.gameObject, 40f);
            Ui.Row(head, 8f);

            var name = Ui.Label("Name", head,
                recipe != null ? Loc.ItemName(recipe.OutputItemId) : job.RecipeId, Theme.FontSmall);
            Ui.Size(name.gameObject, flexWidth: 1f);

            var time = Ui.Label("Time", head, "", Theme.FontSmall, TextAnchor.MiddleRight, Theme.Info);
            Ui.Size(time.gameObject, width: 260f, flexWidth: 0f);

            var bar = Ui.Bar(surface, 8f, Theme.Info);

            _queueRows.Add(new QueueRow { Job = job, Bar = bar, Time = time, Surface = surface });
        }

        /// <summary>
        /// 매 프레임. 남은 시간과 막대를 민다.
        ///
        /// <para>막대는 보간하지 않는다(<c>animate: false</c>) — 매 프레임 값이 오는데 또 보간을
        /// 걸면 늘 한 박자 뒤처진 값을 그리게 된다.</para>
        /// </summary>
        private void TickQueue()
        {
            if (_queueRows.Count == 0) return;

            var now = Session.Clock.UtcNow;

            foreach (var row in _queueRows)
            {
                if (row.Bar == null || !row.Bar.Alive || row.Time == null) continue;

                double total = (row.Job.CompletesAt - row.Job.StartedAt).TotalSeconds;
                var left = row.Job.CompletesAt - now;

                float value = total <= 0.0
                    ? 1f
                    : Mathf.Clamp01(1f - (float)(left.TotalSeconds / total));
                row.Bar.Set(value);

                if (left.TotalSeconds > 0)
                {
                    row.Time.text = left.TotalHours >= 1
                        ? AfterSeoul.Core.Loc.Text("{0}시간 {1}분", (int)left.TotalHours, left.Minutes)
                        : AfterSeoul.Core.Loc.Text("{0}분 {1}초", (int)left.TotalMinutes, left.Seconds);
                    continue;
                }

                if (row.MarkedDone) continue;
                row.MarkedDone = true;

                // 다 됐는데 아직 정산 전이다(정산은 5초마다 돈다). 글자를 바꾸고 맥박을 켠다 —
                // 그 5초 동안 "멈춘 건가?"라고 생각하게 두지 않는다.
                row.Time.text = AfterSeoul.Core.Loc.Text("완료");
                row.Time.color = Theme.Safe;
                row.Bar.SetColor(Theme.Safe);
                row.Bar.SetPulsing(true);
                Ui.SetEdge(row.Surface, Theme.EdgeLive);
            }
        }

        // ── 작업대 성장 ──────────────────────────────────────────

        /// <summary>
        /// 업그레이드와 보조 인력.
        ///
        /// <para>값만 보여주고 무엇을 사는지 안 알려주면 플레이어는 숫자가 오르는 걸 사게 된다.
        /// 그래서 값 옆에 <b>그 레벨이 여는 것</b>을 같이 적는다.</para>
        /// </summary>
        private void RefreshStation()
        {
            Ui.Clear(_stationBody);

            var save = Session.Save;
            int level = save.Factory.StationLevel;

            var now = Ui.Label("Now", _stationBody,
                AfterSeoul.Core.Loc.Text("{0}단계   ·   {1}", level, Station.UnlockedAt(level, Session.Data)),
                Theme.FontSmall, TextAnchor.MiddleLeft, Theme.Text);
            Ui.Size(now.gameObject, 44f);

            BuildAssistantRows(save);
        }

        private void BuildAssistantRows(Core.GameSave save)
        {
            if (!Station.AssistantsUnlocked(save, Session.Data)) return;

            int count = save.Factory.AutoLevel;
            var recipe = string.IsNullOrEmpty(save.Factory.AutoRecipeId)
                ? null
                : Session.Data.GetRecipe(save.Factory.AutoRecipeId);

            var head = Ui.Label("Assist", _stationBody,
                count == 0
                    ? AfterSeoul.Core.Loc.Text("보조 인력 없음 — 자리를 비우면 공장도 멈춥니다")
                    : recipe == null
                        ? AfterSeoul.Core.Loc.Text("보조 인력 {0}명 · 만들 것이 정해지지 않았습니다", count)
                        : AfterSeoul.Core.Loc.Text("보조 인력 {0}명 · {1} 계속 제작", count, Loc.ItemName(recipe.OutputItemId)),
                Theme.FontSmall, TextAnchor.MiddleLeft,
                count > 0 && recipe == null ? Theme.Warn : Theme.Text);
            Ui.Size(head.gameObject, 44f);

            if (count == 0) return;

            // 무엇을 시킬지. 목록이 길지 않으니 창을 띄우지 않고 그 자리에 늘어놓는다.
            foreach (var option in Workbench.AvailableRecipes(save, Session.Data))
            {
                bool current = option.Id == save.Factory.AutoRecipeId;
                string id = option.Id;

                var row = Ui.Button("Auto_" + id, _stationBody,
                    (current ? "▸ " : "   ") + Loc.ItemName(option.OutputItemId),
                    () => OnSetAutoRecipe(id),
                    current ? Theme.AccentDim : Theme.Panel, Theme.FontSmall);
                Ui.Size(row.gameObject, 62f);
            }
        }

        private void OnUpgrade()
        {
            if (!Session.UpgradeStation())
            {
                Shell.Toast(Station.UpgradeBlockReason(Session.Save, Session.Data) ?? AfterSeoul.Core.Loc.Text("개선하지 못했습니다"), 3f);
                return;
            }

            Sfx.Complete();
            int level = Session.Save.Factory.StationLevel;
            Shell.AfterAction();
            _upgradeFeedback.text = Loc.Text("작업대 {0}단계 적용 완료", level);
            Tween.Punch(_quickUpgrade.transform, .08f, .3f);
        }

        private void OnHireAssistant()
        {
            if (!Session.HireAssistant())
            {
                Shell.Toast(Station.HireAssistantBlockReason(Session.Save, Session.Data) ?? AfterSeoul.Core.Loc.Text("고용하지 못했습니다"), 3f);
                return;
            }

            Sfx.Complete();
            Shell.AfterAction();
            _upgradeFeedback.text = Loc.Text("보조 인력 {0}명 — 자동 제작 중", Session.Save.Factory.AutoLevel);
            Tween.Punch(_quickAssistant.transform, .08f, .3f);
        }

        private void OnSetAutoRecipe(string recipeId)
        {
            if (!Session.SetAutoRecipe(recipeId)) return;

            Sfx.Tap();
            Shell.AfterAction();
        }

        // ── 조작 ────────────────────────────────────────────────

        private void OnPick(string recipeId)
        {
            if (!Session.StartWork(recipeId))
            {
                string why = Workbench.StartBlockReason(Session.Save, Session.Data, recipeId);
                Shell.Toast(why ?? AfterSeoul.Core.Loc.Text("시작하지 못했습니다"), 3f);
                Shell.AfterAction();
                return;
            }

            Sfx.Tap();
            DropGame();
            _resultLabel.text = "";
            _celebrate = 0f;
            Shell.AfterAction();
        }

        private void OnCancel()
        {
            if (!Session.CancelWork()) return;

            Sfx.Tap();
            DropGame();
            _resultLabel.text = AfterSeoul.Core.Loc.Text("작업을 접었습니다. 재료는 돌려받았습니다.");
            _resultLabel.color = Theme.TextDim;
            _celebrate = 2f;
            Shell.AfterAction();
        }

        /// <summary>
        /// 손가락이 닿는 순간. 시작도 판정도 여기서 한다 — Button.onClick(뗄 때)로 시작을 받으면
        /// '누르는 순간 판정'하는 게임에서 같은 탭이 판정과 다음 시작을 동시에 일으킨다.
        /// </summary>
        private void OnPress()
        {
            if (_gameHelp != null) return;
            var bench = Session.Save.Factory.Workbench;
            if (bench.IsIdle) return;   // 버튼이 꺼져 있어도 눌림 자체는 들어온다

            if (!_running)
            {
                var recipe = Session.Data.GetRecipe(bench.RecipeId);
                if (recipe == null) return;
                var kind = Minigames.KindFor(recipe, bench.StepsDone);
                if (Session.Save.LearnedMinigames == null || !Session.Save.LearnedMinigames.Contains(Minigames.IdOf(kind))) {
                    ShowGameHelp(); return;
                }
                StartStage(bench);

                // 누르고 있는 것 자체가 플레이인 게임은, 시작시킨 이 누름이 곧 한 판의 시작이다.
                // 여기서 넘겨주지 않으면 첫 누름이 버려지고 막대가 꿈쩍도 하지 않는다.
                if (_running && _game != null && Minigames.StartsOnPress(_game.Kind)) _game.Press();
                return;
            }

            _game.Press();
            CollectIfDone();
        }

        private void OnRelease()
        {
            if (_gameHelp != null) return;
            if (!_running || _game == null) return;

            _game.Release();
            CollectIfDone();
        }

        private void StartStage(WorkbenchState bench)
        {
            var recipe = Session.Data.GetRecipe(bench.RecipeId);
            if (recipe == null) return;

            int step = bench.StepsDone;

            _game = MinigameFactory.Create(Minigames.KindFor(recipe, step));
            _game.BaseRewardCount = Workbench.OutputCountFor(Session.Data, recipe,
                FactorySystem.GradeManualWork(Session.Data, .75));
            _game.RewardIsEstimate = recipe.ManualSteps > 1;
            _game.Mount(_gameHost);
            _game.Begin(step);

            _running = true;
            _celebrate = 0f;
            _resultLabel.text = Minigames.HintOf(_game.Kind);
            _resultLabel.color = Theme.TextFaint;

            Refresh();
        }

        private void ShowGameHelp()
        {
            if (_gameHelp != null) return;
            var bench = Session.Save.Factory.Workbench;
            var recipe = bench.IsIdle ? null : Session.Data.GetRecipe(bench.RecipeId);
            if (recipe == null) return;
            var kind = Minigames.KindFor(recipe, bench.StepsDone);
            bool resume = _running;
            _game?.PauseInput();
            System.Action close = () => {
                if (_gameHelp == null) return;
                var old = _gameHelp; _gameHelp = null;
                old.gameObject.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(old.gameObject);
                else UnityEngine.Object.DestroyImmediate(old.gameObject);
            };
            _gameHelp = Ui.Modal("MinigameHelp", Root, Minigames.LabelOf(kind), close, out var body);
            var panel = (RectTransform)_gameHelp.Find("Panel");
            panel.anchorMin = new Vector2(0, .2f); panel.anchorMax = new Vector2(1, .8f);
            panel.offsetMin = new Vector2(24, 0); panel.offsetMax = new Vector2(-24, 0);
            var intro = Ui.Paragraph("LessonIntro", body, Loc.Text("이렇게 즐겨 보세요"), 34, Theme.Accent);
            Ui.Size(intro.gameObject, 65);
            var lines = Minigames.LessonOf(kind);
            for (int i = 0; i < lines.Length; i++) {
                var instruction = Ui.Paragraph("Lesson" + i, body, (i + 1) + ". " + lines[i], 30, Theme.Text);
                Ui.Size(instruction.gameObject, i == 2 ? 185 : 145);
            }
            var start = Ui.Button("StartMinigame", body, Loc.Text(resume ? "계속하기" : "직접 해보기"), () => {
                string id = Minigames.IdOf(kind);
                if (Session.Save.LearnedMinigames == null) Session.Save.LearnedMinigames = new List<string>();
                if (!Session.Save.LearnedMinigames.Contains(id)) {
                    Session.Save.LearnedMinigames.Add(id);
                    try { Session.Commit(); }
                    catch (System.Exception) {
                        Session.Save.LearnedMinigames.Remove(id);
                        Shell.Toast(Loc.Get("GUIDE_SAVE_ERROR")); return;
                    }
                }
                close();
                if (!resume) StartStage(Session.Save.Factory.Workbench);
            }, Theme.AccentDim);
            Ui.Size(start.gameObject, 90);
        }

        /// <summary>판이 끝났으면 점수를 규칙 쪽에 넘긴다. 끝나지 않았으면 아무 일도 없다.</summary>
        private void CollectIfDone()
        {
            if (!_running || _game == null || !_game.Resolved) return;

            float score = _game.Score;
            string text = _game.ResultText;
            _running = false;

            Sfx.ForScore(score);
            var result = Session.AdvanceWork(score);

            if (result.Completed)
            {
                Sfx.Complete();
                ShowCompletion(result);
            }
            else
            {
                Sfx.Step();
                _resultLabel.text = text;
                _resultLabel.color = ScoreColor(score);
            }

            Shell.AfterAction();
        }

        private void ShowCompletion(WorkStepResult result)
        {
            string name = Loc.ItemName(result.Output.ItemId);
            int stored = result.Output.Count - result.Overflow;

            _resultLabel.text = result.Overflow > 0
                ? AfterSeoul.Core.Loc.Text("{0} ×{1} 완성 · {2}개는 창고가 가득 차 버려짐", name, stored, result.Overflow)
                : AfterSeoul.Core.Loc.Text("{0} ×{1} 완성  ({2})", name, result.Output.Count, Theme.QualityLabel(result.Quality));
            _resultLabel.color = result.Overflow > 0 ? Theme.Warn : Theme.QualityColor(result.Quality);

            _celebrate = 3f;
            _workbenchCard.gameObject.SetActive(true);
            if (_completion != null) UnityEngine.Object.Destroy(_completion.gameObject);
            _completion = Ui.Surface("CraftComplete", _workbenchCard, Theme.PanelAlt, Theme.Accent);
            _completion.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            _completion.anchorMin = new Vector2(0, .28f);
            _completion.anchorMax = new Vector2(1, .75f);
            _completion.offsetMin = new Vector2(24, 0);
            _completion.offsetMax = new Vector2(-24, 0);
            var badge = Ui.Label("Reward", _completion, "✦  " + Loc.Text("완성") + "  ✦\n" +
                name + " ×" + stored + "\n" + Theme.QualityLabel(result.Quality),
                40, TextAnchor.MiddleCenter, Theme.QualityColor(result.Quality));
            Ui.Stretch(badge.rectTransform, 160, 24, 18, 18);
            var rewardIcon = Ui.Icon("RewardIcon", _completion, Session.Data.GetItem(result.Output.ItemId), 132);
            rewardIcon.rectTransform.anchorMin = rewardIcon.rectTransform.anchorMax = new Vector2(0, .5f);
            rewardIcon.rectTransform.sizeDelta = new Vector2(132, 132);
            rewardIcon.rectTransform.anchoredPosition = new Vector2(86, 0);
            Tween.Punch(_completion, .12f, .4f);
            Tween.FadeIn(_completion, .2f);
            var flash = _completion.GetComponent<Image>();
            flash.color = Theme.AccentDim;
            Tween.Tint(flash, Theme.PanelAlt, .8f);
            for (int i = 0; i < 10; i++)
            {
                var particle = Ui.Panel("Spark" + i, _completion, Theme.Accent);
                var rt = particle.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
                rt.sizeDelta = new Vector2(7, 7);
                float angle = i * Mathf.PI * 2 / 10;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Tween.Play(rt, "burst", .8f, t => {
                    if (rt == null) return;
                    rt.anchoredPosition = direction * (40 + t * 160);
                    particle.color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 1 - t);
                });
            }

            // 완성 문구가 조용히 바뀌면 마지막 한 번의 두드림이 어디로 갔는지 알 수가 없다.
            Tween.Punch(_resultLabel.rectTransform, 0.16f, 0.3f);
            Tween.FadeIn(_resultLabel.rectTransform, 0.2f);

            if (result.Overflow > 0)
                Shell.Toast(AfterSeoul.Core.Loc.Text("창고가 가득 찼습니다 — {0} {1}개를 버렸습니다", name, result.Overflow), 3.5f);
        }

        public override void Tick(float deltaTime)
        {
            // 큐는 미니게임과 무관하게 계속 흐른다. 작업대 앞에 앉아 있는 동안에도
            // 뒤에서 돌아가는 것이 보여야 이 화면이 "공장"이 된다.
            TickQueue();
            if (_gameHelp != null) return;

            if (_celebrate > 0f) _celebrate -= deltaTime;
            if (_celebrate <= 0f && _completion != null) {
                UnityEngine.Object.Destroy(_completion.gameObject); _completion = null;
                Refresh();
            }
            if (!_running || _game == null) return;

            _game.Tick(deltaTime);
            _actionButton.gameObject.SetActive(_game.Kind != MinigameKind.Vault && _game.WantsActionButton);

            // 시간이 다 돼서 끝나는 게임도 있다 (힘주기 과압, 골라내기 놓침).
            CollectIfDone();
        }
    }
}
