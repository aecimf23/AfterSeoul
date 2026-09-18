using System;
using System.Linq;
using AfterSeoul.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    internal sealed class QuestJournalWindow
    {
        private readonly AppShell _shell;
        private readonly GameSession _session;
        private readonly RectTransform _root, _body, _list;
        private readonly Button _mainTab, _dailyTab;
        private readonly Text _intro;
        private bool _daily;
        private string _receipt;
        private Color _receiptColor;

        internal QuestJournalWindow(AppShell shell, GameSession session, Transform parent, bool daily)
        {
            _shell = shell; _session = session; _daily = daily;
            _root = Ui.Modal("QuestJournalWindow", parent, Loc.Text("퀘스트 · 오늘의 목표"), () => shell.CloseQuestJournal(), out _body);
            _root.gameObject.AddComponent<SafeArea>();
            // Keep category navigation visible while the quest cards scroll independently.
            var panel = (RectTransform)_root.Find("Panel");
            var tabs = Ui.Rect("QuestTabs", panel); Ui.Size(tabs.gameObject, 84, flexHeight: 0); Ui.Row(tabs, 12); tabs.SetSiblingIndex(panel.Find("Head").GetSiblingIndex() + 1);
            _mainTab = Ui.Button("MainQuests", tabs, Loc.Text("메인 퀘스트"), () => { _daily = false; Refresh(); }, Theme.AccentDim, 30);
            _dailyTab = Ui.Button("DailyQuests", tabs, Loc.Text("일일 퀘스트"), () => { _daily = true; Refresh(); }, Theme.PanelAlt, 30);
            Ui.Size(_mainTab.gameObject, flexWidth: 1); Ui.Size(_dailyTab.gameObject, flexWidth: 1);
            _intro = Ui.Paragraph("QuestOverview", _body, "", 27, Theme.TextDim);
            _list = Ui.Rect("QuestCards", _body); Ui.Column(_list, 18);
            Refresh();
        }

        internal void Refresh(string receipt = null, bool success = true)
        {
            if (_root == null) return;
            if (receipt != null) { _receipt = receipt; _receiptColor = success ? Theme.Safe : Theme.Warn; }
            Ui.Clear(_list);
            var entries = QuestJournalData.Build(_session);
            int ready = entries.Count(e => e.Daily && e.Ready), remaining = entries.Count(e => e.Daily && !e.Completed);
            _intro.text = Loc.Text("메인: 다음 지역으로 이어지는 정찰\n일일: 남은 의뢰 {0}개 · 보상 가능 {1}개\n일일 퀘스트는 매일 오전 5시(한국 시각)에 갱신됩니다.", remaining, ready);
            _mainTab.targetGraphic.color = _daily ? Theme.PanelAlt : Theme.AccentDim;
            _dailyTab.targetGraphic.color = _daily ? Theme.AccentDim : Theme.PanelAlt;
            Ui.SetButtonLabel(_dailyTab, Loc.Text("일일 퀘스트 · {0}", remaining));
            var current = QuestJournalData.Current(_session);
            if (_receipt != null) {
                Paragraph(_list, "QuestReceipt", _receipt, _receiptColor);
                Paragraph(_list, "NextQuest", current == null ? Loc.Text("현재 의뢰를 모두 완료했습니다.") : Loc.Text("다음 추적 목표 · {0}", current.Title), Theme.Info);
                if (current != null && current.Daily != _daily) AddButton(_list, "ShowNextQuest", Loc.Text("다음 목표 보기"), () => { _daily = current.Daily; Refresh(); });
            }
            var filtered = entries.Where(e => e.Daily == _daily).OrderBy(e => e.Completed).ThenBy(e => e.Id == current?.Id ? 0 : 1).ThenBy(e => e.Ready ? 0 : 1).ToList();
            if (filtered.Count == 0) Paragraph(_list, "NoQuests", Loc.Text("등록된 의뢰가 없습니다."), Theme.TextDim);
            foreach (var entry in filtered) {
                var card = Ui.Card(_list, (entry.Completed ? Loc.Text("완료 · ") : entry.Id == current?.Id ? Loc.Text("추적 중 · ") : "") + entry.Title, out var body);
                Paragraph(body, "QuestContact", Loc.TraderName(entry.Npc), Theme.Info);
                Paragraph(body, "QuestDescription", entry.Description, Theme.TextDim);
                Paragraph(body, "QuestObjective", entry.Objective, entry.Completed ? Theme.TextFaint : Theme.Text);
                Paragraph(body, "QuestReward", Loc.Text("보상 · ") + entry.Reward, Theme.Accent);
                var action = AddButton(body, "QuestAction_" + entry.Id, QuestJournalData.ActionLabel(_session, entry), () => _shell.ActOnQuest(entry.Id));
                action.interactable = !entry.Completed;
                if (!entry.Completed && entry.Id != current?.Id)
                    AddButton(body, "TrackQuest_" + entry.Id, Loc.Text("이 목표 추적하기"), () => _shell.TrackQuest(entry.Id), false);
            }
            _body.anchoredPosition = Vector2.zero;
        }

        internal void ShowSupplies()
        {
            Ui.Clear(_list);
            var entry = QuestJournalData.Current(_session);
            Paragraph(_list, "SupplyObjective", entry?.Objective ?? "", Theme.Text);
            Paragraph(_list, "SupplyHelp", Loc.Text("필요한 물자를 창고에 모은 뒤 퀘스트 창에서 납품하세요. 창고 보유량이 목표에 반영됩니다."), Theme.TextDim);
            AddButton(_list, "QuestGoFactory", Loc.Text("공장에서 제작하기"), () => { _shell.CloseQuestJournal(true); _shell.SelectByName("공장"); });
            AddButton(_list, "QuestGoExplore", Loc.Text("탐색에서 물자 찾기"), () => { _shell.CloseQuestJournal(true); _shell.OpenExploration(); });
            AddButton(_list, "QuestGoWarehouse", Loc.Text("창고 물자 확인"), () => { _shell.CloseQuestJournal(true); _shell.SelectByName("창고"); }, false);
            AddButton(_list, "QuestBack", Loc.Text("퀘스트 목록으로"), () => Refresh(), false);
        }

        private static void Paragraph(Transform parent, string name, string value, Color color)
        {
            var text = Ui.Paragraph(name, parent, value, 29, color); text.resizeTextForBestFit = false;
        }
        private static Button AddButton(Transform parent, string name, string title, Action action, bool accent = true)
        {
            var button = Ui.Button(name, parent, title, action, accent ? Theme.AccentDim : Theme.PanelAlt, 29);
            Ui.Size(button.gameObject, 88); return button;
        }
        internal void Destroy()
        {
            if (_root == null) return;
            _root.gameObject.SetActive(false); _root.SetParent(null, false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(_root.gameObject); else UnityEngine.Object.DestroyImmediate(_root.gameObject);
        }
    }
}
