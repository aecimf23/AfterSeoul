using System.Collections.Generic;
using System.Text;
using AfterSeoul.Core;

namespace AfterSeoul.Mail
{
    /// <summary>
    /// 배송 체크섬 — <c>sha256(txId + 정렬된 items 직렬화 + schemaVersion)</c> (LINK_CONTRACT §3).
    ///
    /// <para><b>이건 보안 장치가 아니다.</b> 계산식이 양쪽 저장소에 다 있으니 마음먹은 사람은
    /// 얼마든지 맞춰서 쓸 수 있다. 막으려는 건 변조가 아니라 <b>조용한 불일치</b>다 —
    /// 스키마가 어긋난 채로 배포된 빌드끼리 통신해서 엉뚱한 물건이 본편 창고에 들어가는 일.
    /// 실질 방어선은 값 상한(P8/P10)이고 그건 이 파일 밖에 있다 (LINK_CONTRACT §5-3-1).</para>
    ///
    /// <para><b>본편과 글자 하나까지 같아야 한다.</b> 정렬 순서, 구분자, 대소문자가 하나라도
    /// 다르면 모든 배송이 P4 에서 조용히 폐기되고, 증상은 "보냈는데 안 온다" 하나뿐이라
    /// 어디가 틀렸는지 알 길이 없다. 그래서 형식을 여기 한 곳에만 적고 규칙을 주석으로 박아 둔다.</para>
    /// </summary>
    public static class ShipmentChecksum
    {
        public const string Prefix = "sha256:";

        /// <summary>
        /// 직렬화 형식: <c>{txId}|{schemaVersion}|{itemId}x{count};{itemId}x{count};…</c>
        ///
        /// <para>items 는 <b>itemId 사전순</b>으로 정렬한다 (같은 itemId 는 합치지 않고 count 순).
        /// 목록 순서는 플레이어가 고른 순서라 같은 화물도 실행마다 다를 수 있는데,
        /// 그대로 해싱하면 같은 내용인데 체크섬이 달라진다.</para>
        /// </summary>
        public static string Payload(string txId, int schemaVersion, IReadOnlyList<ItemStack> items)
        {
            var sorted = new List<ItemStack>(items ?? new List<ItemStack>());
            sorted.Sort((a, b) =>
            {
                int byId = string.CompareOrdinal(a.ItemId, b.ItemId);
                return byId != 0 ? byId : a.Count.CompareTo(b.Count);
            });

            var sb = new StringBuilder();
            sb.Append(txId).Append('|').Append(schemaVersion).Append('|');

            for (int i = 0; i < sorted.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(sorted[i].ItemId).Append('x').Append(sorted[i].Count);
            }

            return sb.ToString();
        }

        public static string Compute(string txId, int schemaVersion, IReadOnlyList<ItemStack> items)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(Payload(txId, schemaVersion, items)));

                var hex = new StringBuilder(Prefix, Prefix.Length + bytes.Length * 2);
                foreach (byte b in bytes) hex.Append(b.ToString("x2"));   // 소문자 고정
                return hex.ToString();
            }
        }

        public static string Of(MailShipment shipment, int schemaVersion) =>
            shipment == null ? null : Compute(shipment.TxId, schemaVersion, shipment.Items);

        /// <summary>본편의 P4 검사와 같은 판정. 모바일 쪽에서도 보낼 때 스스로 확인한다.</summary>
        public static bool Matches(MailShipment shipment, int schemaVersion) =>
            shipment != null &&
            !string.IsNullOrEmpty(shipment.Checksum) &&
            shipment.Checksum == Of(shipment, schemaVersion);
    }
}
