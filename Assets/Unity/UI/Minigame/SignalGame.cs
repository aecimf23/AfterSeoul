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
        private Text[] _rewards;
        private Image _connection, _trace;
        private Button _bank, _risk;
        public override MinigameKind Kind => MinigameKind.Signal;

        protected override void Build()
        {
            var panel = MissionUi.Panel(Host, AfterSeoul.Core.Loc.Text("RX-07 / 끊어진 배송망"), AfterSeoul.Core.Loc.Text("밝은 구간 안에 수신 바늘을 유지하세요"));
            _rewards = MissionUi.Rewards(panel, 78);
            var bar = Ui.Surface("DeliveryFrequency", panel, Theme.PanelAlt, Theme.Line);
            MissionUi.Band(bar, 124, 52);
            for (int i = 1; i < 12; i++)
            {
                var tick = Block("FrequencyTick" + i, bar, Theme.Line);
                Pin(tick, i / 12f, 1);
            }
            _target = Block("SignalWindow", bar, Theme.AccentDim);
            _marker = Block("Receiver", bar, Theme.Text);
            _status = Ui.Label("CargoSignal", panel, "", 22, TextAnchor.MiddleCenter, Theme.Text);
            MissionUi.Band(_status.rectTransform, 180, 32);
            _connection = MissionUi.Meter(panel, 216, "Connection", Theme.Accent);
            _trace = MissionUi.Meter(panel, 230, "Trace", Theme.Danger);
            var actions = Ui.Rect("CargoDecision", panel);
            MissionUi.Band(actions, 250, 54);
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
            MissionUi.PaintRewards(_rewards, _signal.Tier);
            MissionUi.Fill(_connection, (float)_signal.Progress);
            MissionUi.Fill(_trace, (float)_signal.Trace);
            int reward = _signal.Tier >= 2 ? 2 : 1;
            Ui.SetButtonLabel(_bank, AfterSeoul.Core.Loc.Text("{0}배 확보 · 회수", reward));
            Ui.SetButtonLabel(_risk, AfterSeoul.Core.Loc.Text("{0}배 도전", (_signal.Tier == 1 ? 2 : 4)));
            _status.text = _signal.Choosing
                ? AfterSeoul.Core.Loc.Text("화물 확보 · 8초 뒤 자동 회수")
                : AfterSeoul.Core.Loc.Text("{0} · 연결 {1:P0} · 추적 {2:P0}", (_signal.Tier == 0 ? AfterSeoul.Core.Loc.Text("일반 배송") : _signal.Tier == 1 ? AfterSeoul.Core.Loc.Text("기업 보안 화물") : AfterSeoul.Core.Loc.Text("기밀 회수 화물")), _signal.Progress, _signal.Trace);
            _status.color = _signal.Trace > .7 ? Theme.Danger : Theme.Text;
            if (_signal.Resolved)
            {
                int multiplier = DeliverySignal.MultiplierFor(_signal.Score);
                string text = _signal.Score == 0 ? AfterSeoul.Core.Loc.Text("연결 단절 · 추가 화물 상실") : AfterSeoul.Core.Loc.Text("배송 성공 · 보수 {0}배", multiplier);
                _status.text = text;
                Finish((float)_signal.Score, text);
            }
        }
    }
}
