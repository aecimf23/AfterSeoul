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
        private Text _hint;
        private int _lastOpened = -1;
        private float _resultHold;
        public override MinigameKind Kind => MinigameKind.Vault;
        protected override void Build()
        {
            var panel = MissionUi.Panel(Host, Loc.Text("상자에서 물자 찾기"), "");
            _hint = panel.Find("MissionSubtitle").GetComponent<Text>();
            for (int row = 0; row < 4; row++)
            {
                var strip = Ui.Rect("VaultRow" + row, panel); MissionUi.Band(strip, 88 + row * 80, 72); Ui.Row(strip, 10);
                for (int col = 0; col < 4; col++)
                {
                    int cell = row * 4 + col;
                    _cells[cell] = Ui.Button("Sector" + cell, strip, "", () => { if (_board.Open(cell)) _lastOpened = cell; Draw(); }, Theme.PanelAlt, Theme.FontSmall);
                    Ui.Size(_cells[cell].gameObject, flexWidth: 1);
                    Ui.SetEdge((RectTransform)_cells[cell].transform, Theme.Line);
                }
            }
            _status = Ui.Label("SearchStatus", panel, "", 22, TextAnchor.MiddleCenter, Theme.TextDim);
            MissionUi.Band(_status.rectTransform, 418, 42);
            var actions = Ui.Rect("VaultActions", panel); MissionUi.Band(actions, 476, 64); Ui.Row(actions, 12);
            _scan = Ui.Button("Scan", actions, "", () => { _board.Scan(); _lastOpened = -1; Draw(); }, Theme.Info, 24);
            _bank = Ui.Button("Extract", actions, AfterSeoul.Core.Loc.Text("회수"), () => { _board.Bank(); Draw(); }, Theme.Accent, 24);
            Ui.Size(_scan.gameObject, flexWidth: 1); Ui.Size(_bank.gameObject, flexWidth: 1);
        }
        protected override void Start(int stage) { _resultHold = 0; _lastOpened = -1; _board = new VaultSearch(Random.Range(1, int.MaxValue)); Draw(); }
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
                string label = _board.Resolved && hazard ? Loc.Text("위험") : open ? _board.Nearby(i).ToString() : Loc.Text("열기");
                Ui.SetButtonLabel(_cells[i], label);
                _cells[i].interactable = !open && !_board.Resolved;
                _cells[i].GetComponent<Image>().color = _board.Resolved && hazard ? Theme.Danger : open ? Theme.AccentDim : Theme.PanelAlt;
                _cells[i].GetComponentInChildren<Text>().color = open ? Theme.Text : Theme.TextDim;
                bool neighbor = _lastOpened >= 0 && i != _lastOpened &&
                    Mathf.Abs(i / 4 - _lastOpened / 4) <= 1 && Mathf.Abs(i % 4 - _lastOpened % 4) <= 1;
                Ui.SetEdge((RectTransform)_cells[i].transform, neighbor ? Theme.Info : Theme.Line);
            }
            _scan.interactable = !_board.Resolved && _board.Scans > 0;
            _bank.interactable = !_board.Resolved && _board.Opened >= 3;
            Ui.SetButtonLabel(_scan, Loc.Text("안전하게 열기 · {0}회", _board.Scans));
            Ui.SetButtonLabel(_bank, _board.Opened < 3 ? Loc.Text("상자 {0}개 더 열면 받기", 3 - _board.Opened) : Loc.Text("{0} 지금 받기", Reward(_board.Multiplier)));
            _hint.text = _board.Opened == 0 ? Loc.Text("아무 상자나 열어 보세요. 첫 상자는 안전해요.") :
                _lastOpened >= 0 ? Loc.Text("방금 나온 숫자 {0} = 주변 8칸의 위험 개수", _board.Nearby(_lastOpened)) :
                Loc.Text("숫자는 주변 위험 개수예요. 0 옆을 찾아보세요.");
            _status.text = _board.Opened < 3 ? Loc.Text("안전한 상자 {0}/3개 찾음", _board.Opened) :
                Loc.Text("{0} 받을 수 있어요 · 상자 {1}개 더 열면 {2}", Reward(_board.Multiplier),
                    (_board.Opened < 6 ? 6 : 10) - _board.Opened, Reward(_board.Opened < 6 ? 2 : 4));
            if (_board.Resolved)
            {
                string result = _board.Collapsed ? Loc.Text("위험 발견 · 기본 물자 {0}만 가져왔어요", Reward(1)) : Loc.Text("물자 {0} 확보!", Reward(_board.Multiplier));
                _status.text = result;
            }
        }
    }
}
