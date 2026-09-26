using System.Collections.Generic;
using System.Linq;
using AfterSeoul.Core;
using AfterSeoul.Inventory;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    internal static class LevelRewardUi
    {
        internal static string Summary(IEnumerable<CharacterLevelReward> rewards, IDataRegistry data)
        {
            var list = rewards.ToList();
            var lines = new List<string> { Loc.Text("지원금 +{0:N0}원", list.Sum(x => x.Money)) };
            var items = list.SelectMany(x => x.Items).GroupBy(x => x.ItemId);
            foreach (var item in items)
                lines.Add(ItemPresentation.Name(data, item.Key) + " × " + item.Sum(x => x.Count));
            int slots = list.Sum(x => x.WarehouseSlots);
            if (slots > 0) lines.Add(Loc.Text("창고 영구 확장 +{0}칸", slots));
            return string.Join(" · ", lines);
        }

        internal static string Next(GameSession session)
        {
            var next = CharacterLevelRewards.Next(session.Save, session.Data.Balance);
            return next == null ? Loc.Text("모든 레벨업 보상을 획득했습니다.")
                : Loc.Text("Lv.{0} 달성 보상\n{1}", next.Level, Summary(new[] { next }, session.Data));
        }

        internal static void DrawReceipt(Transform body, GameSession session, IReadOnlyList<CharacterLevelReward> rewards)
        {
            var first = rewards[0];
            var last = rewards[rewards.Count - 1];
            Line(body, "RewardLevelRange", first.Level == last.Level
                ? Loc.Text("캐릭터 레벨 {0} 달성", last.Level)
                : Loc.Text("레벨 상승! Lv.{0} → Lv.{1}", first.Level - 1, last.Level), 42, Theme.Accent);
            Line(body, "RewardGranted", Loc.Text("레벨업 보상 지급 완료"), 32, Theme.Safe);
            Line(body, "RewardMoney", Loc.Text("지원금 +{0:N0}원", rewards.Sum(x => x.Money)), 38, Theme.Accent);
            foreach (var group in rewards.SelectMany(x => x.Items).GroupBy(x => x.ItemId)) {
                var row = Ui.Rect("RewardItem_" + group.Key, body);
                Ui.Row(row, 14); Ui.Size(row.gameObject, 110);
                Ui.Icon("Icon", row, session.Data.GetItem(group.Key), 94);
                var label = Ui.Paragraph("RewardItemName", row, ItemPresentation.Name(session.Data, group.Key) + " × " + group.Sum(x => x.Count), 30, Theme.Text);
                Ui.Size(label.gameObject, flexWidth: 1);
            }
            int slots = rewards.Sum(x => x.WarehouseSlots);
            if (slots > 0) Line(body, "RewardCapacity", Loc.Text("창고 영구 확장 +{0}칸", slots), 32, Theme.Safe);
            Line(body, "RewardStorage", Loc.Text("지원금은 소지금에, 보급품은 창고에 지급했습니다. 공간이 모자란 물품은 탐색 준비 화면에서 수령할 수 있습니다."), 26, Theme.TextDim);
            Line(body, "RewardNext", Next(session), 30, Theme.Info);
            long remaining = CharacterProgression.ExpToNextLevel(session.Save.Player, session.Data.Balance);
            if (remaining > 0) Line(body, "RewardNextExperience", Loc.Text("다음 캐릭터 레벨까지 경험치 {0}", remaining), 26, Theme.TextDim);
        }

        internal static Text Line(Transform body, string name, string value, int size, Color color)
        {
            var line = Ui.Paragraph(name, body, value, size, color);
            Ui.Size(line.gameObject, flexHeight: 0);
            return line;
        }
    }
}
