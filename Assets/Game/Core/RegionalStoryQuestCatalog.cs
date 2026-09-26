namespace AfterSeoul.Core
{
    public static class RegionalStoryQuestCatalog
    {
        public static readonly RegionalStoryQuestDef[] All = {
            new RegionalStoryQuestDef("YONGSAN_MARKET", "YONGSAN_KIM", "부품 창고", "JUNK25"),
            new RegionalStoryQuestDef("GURO_FACTORY", "DONGDAEMUN_CHOI", "정비 작업실", "JUNK28"),
            new RegionalStoryQuestDef("HAN_RIVER", "DOKKAEBI", "교각 아래", "JUNK_LIGHTER"),
            new RegionalStoryQuestDef("NAMSAN_WOODS", "WILDMAN", "숲속 야영지", "FOOD01"),
            new RegionalStoryQuestDef("GANGNAM_STREETS", "HWANG", "아파트 관리실", "JUNK20"),
            new RegionalStoryQuestDef("YONGSAN_BASE", "US_LIAISON", "막사 의무실", "MED16"),
            new RegionalStoryQuestDef("MYEONGDONG", "BROKER", "의류 매장", "JUNK34"),
            new RegionalStoryQuestDef("UIJEONGBU", "WILDMAN", "대피소 창고", "FOOD02")
        };

        public static RegionalStoryQuestDef Find(string map)
        {
            foreach (var story in All)
                if (story.Map == map) return story;
            return null;
        }
    }
}
