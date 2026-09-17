using System;
using System.Collections.Generic;
using AfterSeoul.Core;
namespace AfterSeoul.Unity.UI
{
    public sealed class NpcBanter
    {
        readonly Dictionary<string,int> _next = new Dictionary<string,int>();
        float _idle;
        public void Activity() => _idle=0;
        public bool Tick(float delta,bool allowed)
        {
            if(!allowed) { Activity(); return false; }
            if(delta<=0 || float.IsNaN(delta) || float.IsInfinity(delta))return false;
            _idle+=Math.Min(delta,1);
            if(_idle<45)return false;
            Activity(); return true;
        }
        public string Next(string npc)
        {
            Activity(); var lines=Lines(npc);
            if(lines.Length==0)return "";
            _next.TryGetValue(npc,out int index); _next[npc]=(index+1)%lines.Length;
            return Loc.Text(lines[index]);
        }
        public static string[] Lines(string npc)
        {
            switch(npc) {
                case "HWANG": return new[] {
                    "완성품만 세지 마. 오늘 돌아온 사람 수도 세어 둬.",
                    "서울 밖으로 나갈 길을 찾는 사람이 있다. 네가 만든 물건이 그 손에 갈지도 모르지.",
                    "한강 쪽 보급로는 아직 살아 있다. 지도에 안 보인다고 길이 없는 건 아니야.",
                    "도깨비를 만나면 말보다 장부를 봐. 저놈은 아는 길이 많아.",
                    "남산 숲으로 가는 짐이 늘었군. 누군가는 다음 일을 준비하고 있다.",
                    "조용하군. 이럴 때 공구도 사람도 좀 쉬게 해." };
                case "DR_CHOI": return new[] {
                    "손은 괜찮으세요? 총보다 먼저 고쳐야 할 건 사람의 몸이에요.",
                    "얼마 전에도 서울을 나갈 길을 묻는 분이 왔어요. 무사히 다음 사람을 만났으면 좋겠네요.",
                    "용산 의료 보급로에 표식을 남겨 준 사람이 있어요. 그 작은 표시가 여러 사람을 살렸죠.",
                    "명동의 멈춘 구급차에도 약품이 남아 있을 거예요. 찾아갈 수 있도록 신호가 필요해요.",
                    "도시를 떠나는 사람도, 남는 사람도 있죠. 둘 다 살아 있어야 다음 이야기가 있어요.",
                    "잠깐 쉬고 계신가요? 좋아요. 물 한 모금 마시고 천천히 하세요." };
                case "YONGSAN_KIM": return new[] {
                    "세게 두드리는 것보다 맞는 자리를 두드리는 게 중요해. 총도 사람도 그렇지.",
                    "P17을 들고 찾아올 사람이 있어. 서울 밖으로 나가려면 먼저 총 상태부터 봐야지.",
                    "폐쇄된 수리 부스라고 다 비어 있는 건 아니야. 바닥에 난 상자 자국을 봐.",
                    "한강 표식이 찍힌 상자가 여기까지 왔더군. 물건을 따라가면 사람도 만나게 돼.",
                    "이름은 몰라도 손본 총은 기억하지. 네가 만든 총도 다시 이 작업대에 올라올지 몰라.",
                    "기계 소리가 끊기니 바깥 소리가 들리는군. 쉬어 둬. 다음 물량은 도망 안 가." };
                default:return Array.Empty<string>();
            }
        }
    }
}
