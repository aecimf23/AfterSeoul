namespace AfterSeoul.Core
{
    /// <summary>New starter dialogue based on mainline TRADER_*_CHAT: supply officer, physician, gunsmith.</summary>
    public static class ExplorationDialogue
    {
        public static string Gift(string npc)
        {
            switch (npc)
            {
                case "DR_CHOI": return Loc.Text("밖으로 나가셔야 한다면, 몸을 지킬 수단은 있어야겠어요. 동대문 최 사장님께 부탁해 둔 권총이에요. 약과 드실 것도 챙겼으니, 다치면 참지 말고 돌아오세요.");
                case "YONGSAN_KIM": return Loc.Text("최씨가 가져온 권총인데, 내가 작동은 확인해 뒀어. 처음부터 비싼 총 찾을 것 없어. 이걸로 시작해. 돌아오면 어디가 불편했는지 말해 주고. 총보다 네가 돌아오는 게 먼저니까.");
                default: return Loc.Text("맨손으로 나가겠다는 건 아니겠지. 동대문 최씨에게 넘겨받은 권총이다. 탄약도 함께 불출하겠다. 보급품은 다시 구하면 된다. 너는 살아서 복귀해라.");
            }
        }

        public static string Invitation(string npc)
        {
            switch (npc)
            {
                case "DR_CHOI": return Loc.Text("나가시기 전에 잠깐 들러 주세요. 몸을 지킬 물건과 드실 것을 챙겨 드릴게요.");
                case "YONGSAN_KIM": return Loc.Text("나가기 전에 작업대 쪽으로 와. 네 손에 맞을 만한 권총 하나 손봐 뒀어.");
                default: return Loc.Text("출발 전에 보급부터 받아라. 맨손으로 내보낼 생각은 없다.");
            }
        }

        public static string Ready(string npc)
        {
            switch (npc)
            {
                case "DR_CHOI": return Loc.Text("권총은 착용해 두었어요. 약과 물도 확인하셨나요? 아래에서 용산 전자상가을 선택하시면 돼요. 무리하지 마세요.");
                case "YONGSAN_KIM": return Loc.Text("권총은 바로 쓸 수 있게 해 뒀어. 탄약 맞는지 확인하고, 아래에서 용산 전자상가을 골라. 돌아와서 상태 한번 보자.");
                default: return Loc.Text("권총 착용 확인. 탄약과 보급품을 점검하고 아래에서 용산 전자상가을 선택해라. 첫 임무는 무사 복귀다.");
            }
        }
    }
}
