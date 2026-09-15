using AfterSeoul.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI.Screens
{
    /// <summary>
    /// 고용주 선택 (GDD §4). 새 세이브가 제일 먼저 보는 화면이다.
    ///
    /// <para><b>고르기 전에 무엇이 다른지 보여준다.</b> 이름과 성향만 적어두고 고르게 하면
    /// 그건 선택이 아니라 제비뽑기다. 보너스 한 줄과 "어떤 의뢰를 주는 사람인가"를 같이 적는다.</para>
    ///
    /// <para><b>절대 규칙도 화면에 적는다</b> (GDD §4): 누구를 골라도 지역이나 콘텐츠가 영구히
    /// 잠기지 않는다. 이걸 안 적으면 플레이어는 "잘못 고르면 망하는 것"으로 읽고,
    /// 그러면 고르는 순간이 즐거움이 아니라 불안이 된다.</para>
    ///
    /// <para>탭에 올리지 않는다. 한 번 고르면 다시 올 일이 없는 화면이라
    /// 탭을 하나 차지하면 그 자리가 영영 죽는다 — <see cref="AppShell"/> 이 덮어씌운다.</para>
    /// </summary>
    public sealed class EmployerScreen
    {
        private readonly AppShell _shell;
        private readonly GameSession _session;
        private readonly RectTransform _root;

        public EmployerScreen(AppShell shell, GameSession session, Transform parent)
        {
            _shell = shell;
            _session = session;

            _root = Ui.Rect("EmployerChoice", parent);
            Ui.Stretch(_root);

            var bg = Ui.Panel("Bg", _root, Theme.Bg);
            Ui.Stretch(bg.rectTransform);

            Build();
        }

        private void Build()
        {
            var col = Ui.Rect("Col", _root);
            Ui.Stretch(col, Theme.Gutter, Theme.Gutter, 40f, 24f);

            var list = Ui.ScrollList("Scroll", col, out var scroll, 14f);

            TerminalPanel.Briefing(list, "현장 배속 신청", "서울에 남은 사람들.\n당신의 첫 고용주를 선택하세요.");

            var title = Ui.Label("Title", list, "누구 밑에서 일하겠습니까", Theme.FontTitle,
                TextAnchor.MiddleLeft, Theme.Text);
            Ui.Size(title.gameObject, 76f);

            var note = Ui.Label("Note", list,
                "고른 사람이 매일 다른 것을 요구하고, 납품하면 신뢰도가 쌓입니다.\n"
                + "누구를 고르든 나중에는 같은 지역에 갈 수 있습니다 — 초반의 결이 달라질 뿐입니다.",
                Theme.FontSmall, TextAnchor.UpperLeft, Theme.TextDim);
            note.horizontalOverflow = HorizontalWrapMode.Wrap;
            Ui.Size(note.gameObject, 84f);

            int index = 0;
            foreach (var employer in _session.Data.AllEmployers) BuildCard(list, employer, index++);

            // 새 세이브가 보는 첫 화면이다. 툭 떠 있으면 메뉴처럼 보이고, 차례로 올라오면
            // 세 사람이 차례로 소개되는 것처럼 읽힌다 — 고르는 일에 무게가 생긴다.
            Tween.FadeIn(_root, 0.25f);
        }

        private void BuildCard(RectTransform parent, EmployerDef employer, int index)
        {
            string npcId = employer.NpcId;

            var btn = Ui.Button("E_" + npcId, parent, "", () => Choose(npcId), Theme.Panel);
            Ui.Size(btn.gameObject, 248f);
            Ui.SetEdge((RectTransform)btn.transform, Theme.Edge);

            var group = Tween.GroupOf((RectTransform)btn.transform);
            group.alpha = 0f;
            Tween.Play(group, "alpha", 0.3f, t => group.alpha = t,
                Tween.Ease.OutQuad, 0.12f + index * 0.09f);

            var col = Ui.Rect("Content", btn.transform);
            Ui.Stretch(col, 22f, 22f, 14f, 14f);
            Ui.Column(col, 4f);

            var dossier = Ui.Label("Dossier", col, $"인사기록 / 0{index + 1}                               >", 23,
                TextAnchor.MiddleLeft, Theme.Info);
            Ui.Size(dossier.gameObject, 28f);

            var name = Ui.Label("Name", col, Loc.TraderName(npcId), Theme.FontHeading,
                TextAnchor.MiddleLeft, Theme.Text);
            Ui.Size(name.gameObject, 52f);

            var desc = Ui.Label("Desc", col, Loc.Get("TRADER_" + npcId + "_DESC"),
                Theme.FontSmall, TextAnchor.UpperLeft, Theme.TextDim);
            desc.horizontalOverflow = HorizontalWrapMode.Wrap;
            desc.verticalOverflow = VerticalWrapMode.Truncate;
            Ui.Size(desc.gameObject, 66f);

            var bonus = Ui.Label("Bonus", col, Employers.SummaryOf(employer), Theme.FontSmall,
                TextAnchor.MiddleLeft, Theme.Accent);
            Ui.Size(bonus.gameObject, 44f);
        }

        private void Choose(string npcId)
        {
            if (!_session.ChooseEmployer(npcId)) return;

            Sfx.Complete();
            _shell.OnEmployerChosen();
        }
    }
}
