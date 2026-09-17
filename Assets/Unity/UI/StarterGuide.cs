using System;
using AfterSeoul.Core;
using UnityEngine;

namespace AfterSeoul.Unity.UI
{
    /// <summary>The same next action stays visible in the workbench, warehouse and hiring market.</summary>
    internal static class StarterGuide
    {
        public static void Draw(RectTransform parent, GameSession session, AppShell shell, string currentTab, Action practice = null)
        {
            if (!StarterSupport.Active(session.Save)) return;
            Ui.Card(parent, Loc.Text("첫 동료 지원"), out var body);
            bool production = StarterSupport.Next(session.Save) == TutorialStep.MakeSomething && practice == null;
            var hint = Ui.Paragraph("StarterHint", body, production ? Loc.Text("첫 총기를 납품하세요. 작업비를 받고 첫 1티어 동료를 무료로 고용합니다.") : StarterSupport.Hint(session.Save), Theme.FontBody, Theme.Text);
            Ui.Size(hint.gameObject, 96f);
            var step = StarterSupport.Next(session.Save);
            if (step == TutorialStep.HireScav && currentTab == "인원") return;
            string label = step == TutorialStep.HireScav ? Loc.Text("첫 동료 고르기")
                : step == TutorialStep.SellIt ? (currentTab == "창고" ? Loc.Text("고철 1개 판매") : Loc.Text("회수한 물자 판매하기"))
                : Loc.Text(production ? "총기 제작 시작" : "무료 회수 실습");
            var button = Ui.Button("StarterNext", body, label, () => {
                if (step == TutorialStep.MakeSomething && practice != null) { practice(); return; }
                if (step == TutorialStep.SellIt && currentTab == "창고") {
                    if (session.Sell("JUNK16", 1)) {
                        Sfx.Confirm(); shell.Toast(Loc.Text("판매 완료 · 첫 동료의 계약금이 지원됩니다."), 3f);
                    }
                    shell.AfterAction(); return;
                }
                shell.SelectByName(Tutorial.TabOf(step));
            }, Theme.Accent, Theme.FontSmall);
            Ui.Size(button.gameObject, 84f);
        }
    }
}
