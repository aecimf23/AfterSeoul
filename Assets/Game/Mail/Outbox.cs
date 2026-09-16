using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Inventory;

namespace AfterSeoul.Mail
{
    /// <summary>
    /// PC 본편 발송함 (GDD §10).
    ///
    /// <para>MVP 는 <b>큐에 쌓는 데까지</b>다 — 실제 전송은 P5. 그래도 검증은 지금 전부 건다.
    /// 나중에 붙이면 "이미 쌓여 있는 화물"이 정책을 안 거친 상태라 그걸 어떻게 할지부터 곤란해진다.</para>
    ///
    /// <para><b>가치 상한이 진짜 방어선이다.</b> 본편 탄약은 단가가 900원부터 268,000원까지
    /// 300배 차이가 나서, "60발까지" 같은 수량 제한은 방어가 되지 못한다. 한 스택이
    /// 90만원어치일 수 있다. 그래서 어떤 경로로도 <see cref="TransferLimitsDef.MaxShipmentValue"/> 와
    /// <see cref="TransferLimitsDef.MaxDailyValue"/> 를 건너뛰지 않는다.</para>
    /// </summary>
    public static class Outbox
    {
        /// <summary>
        /// 화물 하나를 발송함에 넣는다. 창고에서 물건이 빠지고 일일 한도가 차감된다.
        /// 막히면 <b>아무것도 바꾸지 않고</b> null — 사유는 <see cref="BlockReason"/>.
        /// </summary>
        public static MailShipment TryQueue(
            GameSave save, IDataRegistry data, IReadOnlyList<ItemStack> items, System.DateTimeOffset now)
        {
            if (BlockReason(save, data, items, now) != null) return null;

            ResetQuotaIfNewDay(save, now);

            long value = ValueOf(data, items);

            var shipment = new MailShipment
            {
                TxId = NewTxId(save, now),
                AccountId = save.Mail.AccountId,
                QueuedAt = now,
                TotalValue = value,
            };

            foreach (var stack in items)
            {
                // 검증을 통과했으므로 전부 성공한다.
                // 차감이 먼저다 (V7). 넣고 나서 빼면 그 사이에 앱이 죽었을 때
                // 물건이 두 곳에 존재하게 되고, 그게 본편 경제를 부수는 복제 사고다.
                Warehouse.TryRemove(save.Warehouse, stack.ItemId, stack.Count);
                shipment.Items.Add(stack);
            }

            // 체크섬은 내용이 확정된 뒤에 찍는다. 본편의 P4 가 이걸로 스키마 불일치를 걸러낸다.
            shipment.Checksum = ShipmentChecksum.Compute(
                shipment.TxId, save.SchemaVersion, shipment.Items);

            save.Mail.Outbox.Add(shipment);
            save.Mail.DailyQuotaUsedValue += value;
            save.Mail.DailyShipmentsUsed++;
            return shipment;
        }

        /// <summary>보낼 수 없는 이유. 보낼 수 있으면 null.</summary>
        public static string BlockReason(
            GameSave save, IDataRegistry data, IReadOnlyList<ItemStack> items, System.DateTimeOffset now)
        {
            var policy = data.Transfer;
            if (policy == null) return "전송 정책을 읽지 못했습니다";

            // 연결 전에는 보낼 데가 없다. 화면은 버튼 자체를 숨기지만(LINK_CONTRACT §5-1),
            // 규칙 쪽에서도 막아 둔다 — 화면 하나만 고치면 되는 검사는 언젠가 빠진다.
            if (!save.Mail.Linked) return "본편과 연결되지 않았습니다";

            if (items == null || items.Count == 0) return "보낼 물건을 고르세요";

            var limits = policy.Limits;

            if (items.Count > limits.MaxItemStacksPerShipment)
                return $"한 번에 {limits.MaxItemStacksPerShipment}종까지 보낼 수 있습니다";

            // 일일 한도는 "오늘" 기준이다. 날짜가 바뀌었으면 이미 초기화된 셈으로 본다.
            bool newDay = IsNewDay(save, now);
            int usedShipments = newDay ? 0 : save.Mail.DailyShipmentsUsed;
            long usedValue = newDay ? 0 : save.Mail.DailyQuotaUsedValue;

            if (usedShipments >= limits.MaxShipmentsPerDay)
                return $"오늘 발송 횟수를 다 썼습니다 ({limits.MaxShipmentsPerDay}회)";

            // 본편이 아직 안 가져간 화물이 너무 많다 (LINK_CONTRACT §V6).
            //
            // 이 검사가 빠져 있었다. 규약에는 적혀 있는데 코드에 없어서 발송함이 무한히 쌓였고,
            // 그걸 비워 주는 쪽은 P5(본편)라 지금은 아무도 안 비운다. 상한 없이 두면 클라우드
            // 문서가 규약 크기를 넘고, 그 사고는 연동을 붙이는 날에야 드러난다.
            if (PendingCount(save) >= limits.MaxPendingShipments)
                return $"본편이 안 가져간 화물이 {limits.MaxPendingShipments}건입니다 — 본편에서 먼저 수령하세요";

            var perCategory = new Dictionary<string, int>();
            var seen = new HashSet<string>();

            foreach (var stack in items)
            {
                if (stack.Count <= 0) return "수량이 0 인 항목이 있습니다";
                if (!seen.Add(stack.ItemId)) return "같은 물건이 두 번 들어 있습니다";

                var def = data.GetItem(stack.ItemId);
                if (def == null) return "아이템 정보를 찾을 수 없습니다";

                if (Warehouse.CountOf(save.Warehouse, stack.ItemId) < stack.Count)
                    return $"{Loc.ItemName(stack.ItemId)} 수량이 모자랍니다";

                string reason = ItemBlockReason(policy, def);
                if (reason != null) return reason;

                string category = CategoryOf(stack.ItemId);
                int already;
                perCategory.TryGetValue(category, out already);
                perCategory[category] = already + stack.Count;
            }

            foreach (var pair in perCategory)
            {
                var cat = CategoryDef(policy, pair.Key);
                if (cat == null) continue;
                if (pair.Value > cat.MaxPerShipment)
                    return $"{pair.Key} 는 한 번에 {cat.MaxPerShipment}개까지입니다";
            }

            // ── 가치 상한. 여기가 본편 경제를 지키는 유일한 선이다. ──
            long value = ValueOf(data, items);
            if (value > limits.MaxShipmentValue)
                return $"1회 한도 초과 — {value:N0}원 / 한도 {limits.MaxShipmentValue:N0}원";
            if (usedValue + value > limits.MaxDailyValue)
                return $"오늘 한도 초과 — 남은 한도 {limits.MaxDailyValue - usedValue:N0}원";

            return null;
        }

        /// <summary>
        /// AFTER SEOUL 전용 아이템의 접두사. 이걸 단 물건은 본편에 존재하지 않는다.
        ///
        /// <para><b>데이터가 아니라 코드에 둔다</b> (LINK_CONTRACT §V2). 원래 이 규칙은
        /// <c>transferable_items.json</c> 의 <c>"AS": {allowed:false}</c> 한 줄에만 기대고 있었는데,
        /// 그 파일은 <c>extract_mainline_data.py</c> 가 <b>생성</b>한다. 추출기의 상수 목록에서
        /// 줄 하나가 빠지는 날 규칙이 조용히 사라지고, 그러면 모바일이 지어낸 아이템이
        /// 본편 창고로 들어간다 — 부록 A 의 위험 목록에서 <b>유일하게 되돌릴 수 없는</b> 사고다.</para>
        ///
        /// <para>생성되는 값에 기대는 불변식은 불변식이 아니다.</para>
        /// </summary>
        public const string MobileOnlyPrefix = "AS_";

        /// <summary>이 물건 하나를 보낼 수 있는가. 목록 화면이 회색 처리에 쓴다.</summary>
        public static string ItemBlockReason(TransferPolicyDef policy, ItemDef def)
        {
            if (def == null) return "아이템 정보를 찾을 수 없습니다";

            // 제일 먼저 본다. 뒤쪽 검사들은 전부 데이터가 정하는 것이고 이것만 규칙이다.
            if (def.Id != null && def.Id.StartsWith(MobileOnlyPrefix, System.StringComparison.Ordinal))
                return "이 게임 전용 물건이라 본편으로 보낼 수 없습니다";

            foreach (var denied in policy.DenyItemIds)
                if (denied == def.Id) return "본편으로 보낼 수 없는 물건입니다";

            var cat = CategoryDef(policy, CategoryOf(def.Id));
            if (cat == null || !cat.Allowed) return "본편으로 보낼 수 없는 분류입니다";

            if (def.BasePrice > policy.UnitPriceCeiling)
                return $"단가 상한 초과 ({policy.UnitPriceCeiling:N0}원)";

            return null;
        }

        public static bool CanSend(TransferPolicyDef policy, ItemDef def)
            => ItemBlockReason(policy, def) == null;

        /// <summary>
        /// 본편이 아직 안 가져간 화물 수.
        ///
        /// <para><c>Outbox.Count</c> 가 아닌 이유: 수령된 것은 자리를 차지하면 안 된다.
        /// 지금은 수령 표시를 찍는 쪽(P5 본편)이 없어서 둘이 같은 값이지만,
        /// 붙는 날 이 함수만 맞으면 나머지는 저절로 맞는다.</para>
        /// </summary>
        public static int PendingCount(GameSave save)
        {
            if (save == null) return 0;

            int n = 0;
            foreach (var shipment in save.Mail.Outbox)
                if (!shipment.Claimed) n++;
            return n;
        }

        public static long ValueOf(IDataRegistry data, IReadOnlyList<ItemStack> items)
        {
            long total = 0;
            foreach (var stack in items)
            {
                var def = data.GetItem(stack.ItemId);
                total += (def != null ? def.BasePrice : 0) * stack.Count;
            }
            return total;
        }

        /// <summary>오늘 남은 가치 한도.</summary>
        public static long RemainingDailyValue(GameSave save, IDataRegistry data, System.DateTimeOffset now)
        {
            long used = IsNewDay(save, now) ? 0 : save.Mail.DailyQuotaUsedValue;
            long left = data.Transfer.Limits.MaxDailyValue - used;
            return left > 0 ? left : 0;
        }

        /// <summary>오늘 남은 발송 횟수.</summary>
        public static int RemainingShipments(GameSave save, IDataRegistry data, System.DateTimeOffset now)
        {
            int used = IsNewDay(save, now) ? 0 : save.Mail.DailyShipmentsUsed;
            int left = data.Transfer.Limits.MaxShipmentsPerDay - used;
            return left > 0 ? left : 0;
        }

        // ── 내부 ─────────────────────────────────────────────────

        /// <summary>
        /// 아이템 id 의 카테고리 = 앞쪽 영문 접두사. <c>AMO01</c> → <c>AMO</c>.
        /// 본편 id 규칙이 그렇게 생겼고, 정책 파일도 그 접두사로 적혀 있다.
        /// </summary>
        public static string CategoryOf(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return "";

            int i = 0;
            while (i < itemId.Length && itemId[i] >= 'A' && itemId[i] <= 'Z') i++;
            return i > 0 ? itemId.Substring(0, i) : itemId;
        }

        private static TransferCategoryDef CategoryDef(TransferPolicyDef policy, string category)
        {
            TransferCategoryDef def;
            if (policy.Categories.TryGetValue(category, out def)) return def;

            // JUNK_CIG 처럼 밑줄이 섞인 id 는 접두사가 JUNK 다.
            int underscore = category.IndexOf('_');
            if (underscore > 0 && policy.Categories.TryGetValue(category.Substring(0, underscore), out def))
                return def;

            return null;
        }

        /// <summary>일일 한도가 어제 것인가.</summary>
        private static bool IsNewDay(GameSave save, System.DateTimeOffset now)
        {
            string today = GameTime.GameDateOf(now).ToString();
            return save.Mail.DailyQuotaGameDate != today;
        }

        /// <summary>
        /// 날짜가 바뀌었으면 한도를 비운다.
        ///
        /// <para>타임라인 사건으로 두지 않는 이유: 한도 초기화는 보상이 아니라서
        /// 다른 사건과의 순서가 결과를 바꾸지 않는다. 쓰는 순간 날짜를 비교하는 것으로 충분하고,
        /// 사흘 만에 접속해도 "오늘 것"이 맞게 나온다.</para>
        /// </summary>
        private static void ResetQuotaIfNewDay(GameSave save, System.DateTimeOffset now)
        {
            string today = GameTime.GameDateOf(now).ToString();
            if (save.Mail.DailyQuotaGameDate == today) return;

            save.Mail.DailyQuotaGameDate = today;
            save.Mail.DailyQuotaUsedValue = 0;
            save.Mail.DailyShipmentsUsed = 0;
        }

        /// <summary>
        /// 거래 id. 본편이 중복 수령을 막는 열쇠라 유일해야 한다.
        /// 프로토콜 v1의 GUID를 사용한다. 기기 간 시계나 로컬 난수 시드가 같아도 겹치지 않는다.
        /// </summary>
        private static string NewTxId(GameSave save, System.DateTimeOffset now)
        {
            return "m2p_" + System.Guid.NewGuid().ToString("N");
        }
    }
}
