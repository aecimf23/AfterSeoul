using AfterSeoul.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    public sealed class SignalGame : Minigame
    {
        private DeliverySignal _signal;
        private bool _held;
        private RectTransform _target, _marker;
        private Text _status;
        private Text _warning;
        private RectTransform _decisions;
        private Image _connection;
        private Button _bank, _risk;
        public override MinigameKind Kind => MinigameKind.Signal;
        public override bool WantsActionButton => _signal == null || (!_signal.Choosing && !_signal.Resolved);

        protected override void Build()
        {
            var panel = MissionUi.Panel(Host, Loc.Text("배송 물자 모으기"), Loc.Text("누르면 오른쪽 · 떼면 왼쪽"));
            var bar = Ui.Surface("DeliveryFrequency", panel, Theme.PanelAlt, Theme.Line);
            MissionUi.Band(bar, 82, 62);
            _target = Block("SignalWindow", bar, Theme.AccentDim);
            _marker = Block("Receiver", bar, Theme.Text);
            _status = Ui.Label("CargoSignal", panel, "", 22, TextAnchor.MiddleCenter, Theme.Text);
            MissionUi.Band(_status.rectTransform, 154, 36);
            _connection = MissionUi.Meter(panel, 196, "Connection", Theme.Accent);
            _warning = Ui.Label("ConnectionHint", panel, "", 24, TextAnchor.MiddleCenter, Theme.TextDim);
            MissionUi.Band(_warning.rectTransform, 214, 36);
            var actions = Ui.Rect("CargoDecision", panel);
            _decisions = actions;
            MissionUi.Band(actions, 258, 64);
            Ui.Row(actions, 12);
            _bank = Ui.Button("BankCargo", actions, AfterSeoul.Core.Loc.Text("회수"), () => { _signal.Bank(); UpdateView(); }, Theme.AccentDim, Theme.FontSmall);
            _risk = Ui.Button("RiskCargo", actions, AfterSeoul.Core.Loc.Text("추가 도전"), () => { _held = false; _signal.Continue(); UpdateView(); }, Theme.Line, Theme.FontSmall);
            Ui.Size(_bank.gameObject, flexWidth: 1);
            Ui.Size(_risk.gameObject, flexWidth: 1);
        }

        protected override void Start(int stage)
        {
            _signal = new DeliverySignal(); _held = false; UpdateView();
        }

        public override void Press() { if (!Resolved) _held = true; }
        public override void Release() { _held = false; }
        public override void PauseInput() { _held = false; }
        public override void Tick(float deltaTime)
        {
            if (Resolved) return;
            int previousTier = _signal.Tier;
            _signal.Tick(deltaTime, _held);
            if (_signal.Tier > previousTier) Sfx.Step();
            UpdateView();
        }

        private void UpdateView()
        {
            Span(_target, (float)(_signal.Target - _signal.HalfWidth), (float)(_signal.Target + _signal.HalfWidth));
            Pin(_marker, (float)_signal.Cursor, 5);
            _bank.interactable = _risk.interactable = _signal.Choosing && !_signal.Resolved;
            _decisions.gameObject.SetActive(_signal.Choosing && !_signal.Resolved);
            MissionUi.Fill(_connection, (float)_signal.Progress);
            int reward = _signal.Tier >= 2 ? 2 : 1;
            Ui.SetButtonLabel(_bank, Loc.Text("{0} 지금 받기", Reward(reward)));
            Ui.SetButtonLabel(_risk, Loc.Text("{0}까지 더 모으기", Reward(_signal.Tier == 1 ? 2 : 4)));
            _status.text = _signal.Choosing
                ? Loc.Text("{0} 확보 · {1}초 뒤 자동 받기", Reward(reward), Mathf.CeilToInt((float)_signal.ChoiceSecondsLeft))
                : Loc.Text("물자 모으는 중 · {0:P0}", _signal.Progress);
            _warning.text = _signal.Choosing ? Loc.Text("더 도전하다 실패하면 추가 물자는 잃어요.") :
                _signal.Trace > .55 ? Loc.Text("연결이 끊기려 해요! 밝은 구간으로 돌아오세요.") :
                Loc.Text("밝은 구간에 바늘을 유지하세요.");
            _warning.color = _signal.Trace > .55 ? Theme.Warn : Theme.TextDim;
            _status.color = _signal.Trace > .7 ? Theme.Danger : Theme.Text;
            if (_signal.Resolved)
            {
                int multiplier = DeliverySignal.MultiplierFor(_signal.Score);
                string text = _signal.Score == 0 ? Loc.Text("연결 종료 · 기본 물자 {0}", Reward(1)) : Loc.Text("물자 {0} 확보!", Reward(multiplier));
                _status.text = text;
                Finish((float)_signal.Score, text);
            }
        }
    }
}
