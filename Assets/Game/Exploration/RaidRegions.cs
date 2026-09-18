using System;
using AfterSeoul.Core;

namespace AfterSeoul.Exploration
{
    public sealed class RaidSite
    {
        public readonly string Name, Container;
        public readonly bool Indoors, Dangerous;
        public RaidSite(string name, string container, bool indoors, bool dangerous) { Name=name; Container=container; Indoors=indoors; Dangerous=dangerous; }
    }

    public static class RaidRegions
    {
        public static RaidSite[] Sites(string map)
        {
            switch(map) {
                case "YONGSAN_MARKET": return new[] { S("1층 전자 매장","Tool",true), S("부품 창고","Tool",true), S("지하 주차장","Pocket",true), S("옥상 연결로","Weapon",false,true), S("상가 뒷골목","Safe",false,true) };
                case "GURO_FACTORY": return new[] { S("정비 작업실","Tool",true), S("공장 휴게실","Pocket",true), S("철재 적치장","Tool",false,true), S("화물 하역장","Weapon",false,true) };
                case "HAN_RIVER": return new[] { S("세관 사무실","Tool",true), S("교각 아래","Pocket",false), S("밀수 화물 부두","Ammo",false,true), S("봉인된 컨테이너","Safe",true,true) };
                case "NAMSAN_WOODS": return new[] { S("산장 대피소","Medical",true), S("숲속 야영지","Pocket",false), S("능선 감시초소","Weapon",false,true), S("버려진 연구실","Medical",true,true) };
                case "GANGNAM_STREETS": return new[] { S("병원 처치실","Medical",true), S("아파트 관리실","Tool",true), S("대로 검문소","Ammo",false,true), S("고층 금고실","Safe",true,true) };
                case "YONGSAN_BASE": return new[] { S("막사 의무실","Medical",true), S("기지 정문","Pocket",false), S("보급 창고","Weapon",true,true), S("군용 주차장","Ammo",false,true) };
                case "MYEONGDONG": return new[] { S("의류 매장","Tool",true), S("상가 약국","Medical",true), S("보석상 금고","Safe",true,true), S("호텔 뒤편 골목","Weapon",false,true) };
                default: return new[] { S("대피소 창고","Pocket",true), S("정비 차고","Tool",true), S("군수품 집하장","Ammo",false,true), S("전방 관측소","Weapon",false,true) };
            }
        }
        private static RaidSite S(string n,string c,bool inside,bool danger=false) => new RaidSite(n,c,inside,danger);
        public static RaidSite Find(string map,string location) { foreach(var site in Sites(map)) if(site.Name==location) return site; return null; }
        public static string Theme(string map)
        {
            switch(map) {
                case "YONGSAN_MARKET": return "전자 부품 · 회로기판과 전선";
                case "GURO_FACTORY": return "공업 자재 · 볼트와 공구";
                case "HAN_RIVER": return "밀수 물자 · 탄약과 배선";
                case "NAMSAN_WOODS": return "생존 물자 · 식량과 의료품";
                case "GANGNAM_STREETS": return "의료 장비 · 귀중품";
                case "YONGSAN_BASE": return "군수 물자 · 총기와 방어구";
                case "MYEONGDONG": return "의류 자재 · 섬유와 귀중품";
                default: return "전선 보급 · 탄약과 공업 자재";
            }
        }
        public static string[] Materials(string map)
        {
            switch(map) {
                case "YONGSAN_MARKET": return new[]{"JUNK20","JUNK23","JUNK24","JUNK25","JUNK26"};
                case "GURO_FACTORY": return new[]{"JUNK03","JUNK04","JUNK15","JUNK16","JUNK28","JUNK29"};
                case "HAN_RIVER": return new[]{"JUNK20","JUNK27","JUNK32"};
                case "NAMSAN_WOODS": return new[]{"JUNK18","JUNK34","JUNK09"};
                case "GANGNAM_STREETS": return new[]{"JUNK07","JUNK09","JUNK31"};
                case "YONGSAN_BASE": return new[]{"JUNK32","JUNK37","JUNK38"};
                case "MYEONGDONG": return new[]{"JUNK34","JUNK35","JUNK36"};
                default: return new[]{"JUNK03","JUNK16","JUNK22"};
            }
        }
        public static int LootWeight(string map, ItemDef item)
        {
            if(item==null) return 1;
            if(Array.IndexOf(Materials(map),item.Id)>=0) return 7;
            if(map=="NAMSAN_WOODS" && (item.Category=="Food" || item.Category=="Medical")) return 3;
            if(map=="GANGNAM_STREETS" && item.Category=="Medical") return 4;
            if((map=="YONGSAN_BASE" || map=="UIJEONGBU" || map=="HAN_RIVER") && item.Category=="Ammo") return 4;
            return 1;
        }
        public static string ScavLine(string map)
        {
            switch(map) {
                case "YONGSAN_MARKET": return "전자상가 부품 찾으러 왔어? 윗층은 PMC가 뒤지고 있어. 서로 총 내리고 얘기하자.";
                case "GURO_FACTORY": return "공장에 아직 쓸 만한 공구가 있어. 하역장 쪽은 발소리가 많으니 조심해.";
                case "HAN_RIVER": return "도깨비 구역인 건 알지? 부두 물건 함부로 건드리면 시끄러워져. 무슨 일이야?";
                case "NAMSAN_WOODS": return "능선에 저격수가 있어. 난 먹을 것만 찾는 중이야. 산장까지는 조용했어.";
                case "GANGNAM_STREETS": return "병원에 약이 남아 있더라. 금고 쪽은 무장한 놈들이 지켜. 여기서 서로 피 볼 필요 없잖아.";
                case "YONGSAN_BASE": return "기지 안에서는 총부터 들이대지 마. 난 보급품만 챙겨서 나갈 거야.";
                case "MYEONGDONG": return "가게는 많아도 멀쩡한 건 별로 없어. 옷감 필요하면 안쪽 매장을 뒤져 봐.";
                default: return "여기까지 왔다고? 전방엔 무장한 놈들이 많아. 탄약 아껴. 돌아갈 길도 멀잖아.";
            }
        }
    }
}
