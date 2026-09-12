using AfterSeoul.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    /// <summary>
    /// 골라내기 — 검사 헤드가 칸을 하나씩 훑는다. 불량이 나오면 누른다.
    ///
    /// <para>타이밍·힘주기와 갈라지는 지점은 <b>어디를 눌러야 하는지 미리 알 수 없다</b>는 것이다.
    /// 앞의 둘은 목표를 보여주고 맞추라고 하지만, 이건 칸이 열려야 정상인지 불량인지 보인다.
    /// 예측이 아니라 반응이다 — 그래서 선별·검수 공정에 붙인다.</para>
    ///
    /// <para>칸 하나가 열려 있는 시간이 1단계 700ms 다. 사람 반응속도 200~300ms 를 빼면
    /// 판단하고 누를 여유가 400ms 쯤 남는다. 0.22초 안에 누르면 만점이고 거기서부터 깎인다 —
    /// 빨리 알아보는 것 자체가 실력이 되게.</para>
    ///
    /// <para>불량은 절대 첫 칸에 두지 않는다. 열리자마자 판단하라는 건 반응이 아니라 운이다.
    /// 같은 이유로 <b>시작 버튼을 누른 순간 바로 첫 칸을 열지 않는다</b> — 누르고 눈을 옮길
    /// 틈(<see cref="LeadIn"/>)을 준다. 이게 없으면 검사대가 이미 지나간 뒤에 화면을 보게 된다.</para>
    /// </summary>
    public sealed class InspectGame : Minigame
    {
        private const int Cells = 5;

        /// <summary>시작 버튼에서 손을 떼고 검사대로 눈을 옮길 시간.</summary>
        private const float LeadIn = 0.5f;

        /// <summary>
        /// 이 안에 누르면 만점. 사람 반응속도(시각 판단 포함 350~450ms)보다 짧게 잡았다 —
        /// 여기서부터 깎여야 '우수'가 흔해지지 않는다. 400ms 면 양호, 300ms 면 우수쯤 된다.
        /// </summary>
        private const float Grace = 0.22f;

        private static readonly float[] CellTimes = { 0.70f, 0.60f, 0.52f, 0.52f };

        private Image[] _cellImages;
        private Text[] _cellLabels;

        private float _cellTime, _t;
        private int _current, _bad;

        public override MinigameKind Kind => MinigameKind.Inspect;

        protected override void Build()
        {
            var row = Ui.Rect("Cells", Host);
            Ui.Stretch(row, 0f, 0f, 26f, 26f);
            Ui.Row(row, 8f);

            _cellImages = new Image[Cells];
            _cellLabels = new Text[Cells];

            for (int i = 0; i < Cells; i++)
            {
                var cell = Ui.Rect("C" + i, row);
                Ui.Size(cell.gameObject, flexWidth: 1f);

                var img = cell.gameObject.AddComponent<Image>();
                img.color = Theme.Bg;
                img.raycastTarget = false;
                _cellImages[i] = img;

                _cellLabels[i] = Ui.Label("L" + i, cell, "", Theme.FontSmall,
                    TextAnchor.MiddleCenter, Theme.Text);
            }
        }

        protected override void Start(int stage)
        {
            _cellTime = At(CellTimes, stage);
            _bad = Random.Range(1, Cells);   // 첫 칸은 제외
            _t = 0f;

            for (int i = 0; i < Cells; i++)
            {
                _cellImages[i].color = Theme.Bg;
                _cellLabels[i].text = "";
            }

            // -1 = 아직 아무 칸도 열리지 않았다. 첫 칸은 LeadIn 뒤에 열린다.
            _current = -1;
            _t = -LeadIn;
        }

        /// <summary>칸 하나를 연다 — 여기서 비로소 정상인지 불량인지 보인다.</summary>
        private void Open(int index)
        {
            bool bad = index == _bad;
            _cellImages[index].color = bad ? Theme.Danger : Theme.Line;
            _cellLabels[index].text = bad ? "불량" : "정상";
            _cellLabels[index].color = bad ? Theme.Text : Theme.TextFaint;
        }

        /// <summary>지나간 칸은 죽인다. 지금 어디를 보고 있는지가 한눈에 남게.</summary>
        private void Dim(int index)
        {
            _cellImages[index].color = Theme.Bg;
            _cellLabels[index].color = Theme.TextFaint;
        }

        public override void Tick(float deltaTime)
        {
            if (Resolved) return;

            _t += deltaTime;

            // 아직 준비 시간. 다 되면 첫 칸을 연다.
            if (_current < 0)
            {
                if (_t < 0f) return;
                _current = 0;
                Open(0);
                Sfx.Tap();
                return;
            }

            if (_t < _cellTime) return;

            // 불량이 지나갔으면 끝이다. 뒤 칸을 더 볼 이유가 없다.
            if (_current == _bad)
            {
                Finish(0f, "불량을 놓쳤습니다");
                return;
            }

            Dim(_current);
            _t -= _cellTime;
            _current++;

            if (_current >= Cells)
            {
                Finish(0f, "불량을 놓쳤습니다");   // 방어 — _bad 는 항상 범위 안이다
                return;
            }

            Open(_current);
            Sfx.Tap();   // 검사기가 한 칸 넘어가는 소리 — 리듬이 있어야 언제 올지 몸으로 안다
        }

        public override void Press()
        {
            if (Resolved || _current < 0) return;

            if (_current != _bad)
            {
                Finish(0f, "멀쩡한 것을 골라냈습니다");
                return;
            }

            float score = _t <= Grace
                ? 1f
                : Mathf.Max(0.5f, 1f - 0.5f * (_t - Grace) / Mathf.Max(0.05f, _cellTime - Grace));

            Finish(score,
                score >= 0.92f ? "정확"
                : score >= 0.7f ? "양호"
                : "아슬아슬");
        }
    }
}
