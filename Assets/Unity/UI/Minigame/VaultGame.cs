using AfterSeoul.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    public sealed class VaultGame : Minigame
    {
        private VaultSearch _board;
        private readonly Button[] _cells = new Button[16];
        private Button _scan, _bank;
        private Text _status;
        private Text[] _rewards;
        private float _resultHold;
        public override MinigameKind Kind => MinigameKind.Vault;
        protected override void Build()
        {
            var panel = MissionUi.Panel(Host, AfterSeoul.Core.Loc.Text("B-04 / 정전된 지하 금고"), AfterSeoul.Core.Loc.Text("숫자 = 주변 8칸의 위험 수 · 첫 탐색은 안전"));
            _rewards = MissionUi.Rewards(panel, 78);
            for (int row = 0; row < 4; row++)
            {
                var strip = Ui.Rect("VaultRow" + row, panel); MissionUi.Band(strip, 122 + row * 76, 68); Ui.Row(strip, 8);
                for (int col = 0; col < 4; col++)
                {
                    int cell = row * 4 + col;
                    _cells[cell] = Ui.Button("Sector" + cell, strip, "", () => { _board.Open(cell); Draw(); }, Theme.PanelAlt, Theme.FontSmall);
                    Ui.Size(_cells[cell].gameObject, flexWidth: 1);
                    Ui.SetEdge((RectTransform)_cells[cell].transform, Theme.Line);
                }
            }
            _status = Ui.Label("SearchStatus", panel, "", 22, TextAnchor.MiddleCenter, Theme.TextDim);
            MissionUi.Band(_status.rectTransform, 428, 30);
            var actions = Ui.Rect("VaultActions", panel); MissionUi.Band(actions, 468, 58); Ui.Row(actions, 12);
            _scan = Ui.Button("Scan", actions, AfterSeoul.Core.Loc.Text("안전 스캔"), () => { _board.Scan(); Draw(); }, Theme.Info, 24);
            _bank = Ui.Button("Extract", actions, AfterSeoul.Core.Loc.Text("회수"), () => { _board.Bank(); Draw(); }, Theme.Accent, 24);
            Ui.Size(_scan.gameObject, flexWidth: 1); Ui.Size(_bank.gameObject, flexWidth: 1);
        }
        protected override void Start(int stage) { _resultHold = 0; _board = new VaultSearch(Random.Range(1, int.MaxValue)); Draw(); }
        public override void Tick(float deltaTime)
        {
            if (!_board.Resolved || Resolved) return;
            _resultHold += Mathf.Max(0, deltaTime);
            if (_resultHold >= 1.1f) Finish((float)_board.Score, _status.text);
        }
        private void Draw()
        {
            for (int i = 0; i < 16; i++)
            {
                bool open = _board.IsOpen(i), hazard = _board.IsHazard(i);
                string label = _board.Resolved && hazard ? AfterSeoul.Core.Loc.Text("위험") : open ? (_board.Nearby(i) == 0 ? AfterSeoul.Core.Loc.Text("안전") : _board.Nearby(i).ToString()) : $"{(char)('A' + i / 4)}-{i % 4 + 1}";
                Ui.SetButtonLabel(_cells[i], label);
                _cells[i].interactable = !open && !_board.Resolved;
                _cells[i].GetComponent<Image>().color = _board.Resolved && hazard ? Theme.Danger : open ? Theme.AccentDim : Theme.PanelAlt;
                _cells[i].GetComponentInChildren<Text>().color = open ? Theme.Text : Theme.TextDim;
            }
            MissionUi.PaintRewards(_rewards, _board.Opened >= 10 ? 3 : _board.Opened >= 6 ? 2 : _board.Opened >= 3 ? 1 : 0);
            _scan.interactable = !_board.Resolved && _board.Scans > 0;
            _bank.interactable = !_board.Resolved && _board.Opened >= 3;
            Ui.SetButtonLabel(_scan, AfterSeoul.Core.Loc.Text("안전 스캔 · {0}회", _board.Scans));
            Ui.SetButtonLabel(_bank, AfterSeoul.Core.Loc.Text("{0}배 회수", _board.Multiplier));
            _status.text = AfterSeoul.Core.Loc.Text("확보 {0}/10 · 3칸: 회수 / 6칸: 2배 / 10칸: 4배", _board.Opened);
            if (_board.Resolved)
            {
                string result = _board.Collapsed ? AfterSeoul.Core.Loc.Text("붕괴 감지 — 추가 물자를 포기하고 철수") : AfterSeoul.Core.Loc.Text("금고 회수 성공 · 보수 {0}배", _board.Multiplier);
                _status.text = result;
            }
        }
    }
}
