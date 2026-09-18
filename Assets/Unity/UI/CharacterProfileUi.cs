using AfterSeoul.Core;
using AfterSeoul.Inventory;
using AfterSeoul.Exploration;
using UnityEngine;
using UnityEngine.UI;
namespace AfterSeoul.Unity.UI
{
    internal static class CharacterProfileUi
    {
        internal static void Draw(Transform body,GameSession session)
        {
            var p=session.Save.Player;
            var name=Ui.Paragraph("PlayerName",body,string.IsNullOrWhiteSpace(p.Name)?Loc.Text("이름 미등록 · 첫 출발 전에 알려주세요"):p.Name,38,Theme.Accent);
            name.supportRichText=false; Ui.Size(name.gameObject,90);
            Line(body,"PlayerLevel",Loc.Text("캐릭터 레벨 {0} · 경험치 {1}",p.CharacterLevel,p.CharacterExp));
            Line(body,"BaseLevel",Loc.Text("기지 레벨 {0} · 경험치 {1}",p.Level,p.Exp));
            Line(body,"PlayerVitals",Loc.Text("HP {0:0} · 수분 {1:0} · 에너지 {2:0}",p.Hp,p.Hydration,p.Energy));
            Line(body,"Employer",Loc.TraderName(p.EmployerNpcId));
            Line(body,"EquipmentHeading",Loc.Text("장착 장비"));
            foreach(var slot in PlayerEquipment.Slots) {
                string id=PlayerEquipment.Equipped(session.Save,slot);
                string key=slot=="Weapon"?"총기":slot=="Melee"?"근접 무기":slot=="Headwear"?"머리":slot=="BodyArmor"?"방탄복":slot=="Earpiece"?"헤드셋":slot=="TacticalRig"?"전술 조끼":"가방";
                Line(body,"ProfileSlot_"+slot,Loc.Text(key)+" · "+(id==null?"—":ItemPresentation.Name(session.Data,id)));
                if(id!=null) Line(body,"ProfileStats_"+slot,ItemPresentation.Stats(session.Data,id));
            }
        }
        private static void Line(Transform parent,string name,string value)
        {var text=Ui.Paragraph(name,parent,value,28,Theme.Text);Ui.Size(text.gameObject,flexHeight:0);}
        internal static InputField NameInput(Transform body)
        {
            var panel=Ui.Panel("PlayerNameInput",body,Theme.PanelAlt);Ui.Size(panel.gameObject,88);
            var text=Ui.Label("InputText",panel.transform,"",32,TextAnchor.MiddleLeft,Theme.Text);
            Ui.Stretch(text.rectTransform,18,18,8,8);text.supportRichText=false;
            var hint=Ui.Label("Placeholder",panel.transform,Loc.Text("이름을 입력하세요 (1~16자)"),28,TextAnchor.MiddleLeft,Theme.TextDim);
            Ui.Stretch(hint.rectTransform,18,18,8,8);
            var input=panel.gameObject.AddComponent<InputField>(); input.targetGraphic=panel;
            input.textComponent=text;input.placeholder=hint;input.lineType=InputField.LineType.SingleLine;
            input.characterLimit=32;input.keyboardType=TouchScreenKeyboardType.Default;
            return input;
        }
        internal static string NameQuestion(string npc)
        {
            switch(npc) {
                case "HWANG":return Loc.Text("출발하기 전에 확인하지. 네 이름이 뭔가?");
                case "DR_CHOI":return Loc.Text("잠깐만요. 당신의 이름이 뭐죠? 돌아오시면 이름으로 불러 드릴게요.");
                case "YONGSAN_KIM":return Loc.Text("용산에 나가기 전에 통성명은 해야지. 자네 이름이 뭔가?");
                default:return Loc.Text("떠나기 전에 이름부터 알려 줘. 뭐라고 부르면 될까?");
            }
        }
    }
}
