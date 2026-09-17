using System;
using AfterSeoul.Core;
using UnityEngine;
using UnityEngine.UI;
namespace AfterSeoul.Unity.UI
{
    internal sealed class EmployerSceneView
    {
        internal RectTransform Root { get; }
        readonly Text _location, _greeting;
        readonly Image _background, _portrait;
        readonly RectTransform _portraitFrame, _greetingRoot;
        string _shown;
        bool _initialized;
        NpcSpeech _speech;
        float _greetingAge;
        int _greetingVariant;
        internal EmployerSceneView(Transform parent)
        {
            Root=Ui.Surface("EmployerScene",parent,Theme.Bg,Theme.AccentDim);
            _background=Picture("LocationImage",Root);
            Ui.Stretch(_background.rectTransform,2,2,2,2); _background.preserveAspect=false;
            var shade=Ui.Panel("LocationShade",Root,new Color(0,0,0,.24f));
            Ui.Stretch(shade.rectTransform); shade.raycastTarget=false;
            _location=Ui.Label("Location",Root,"",22,TextAnchor.MiddleLeft,Color.white);
            Ui.Top(_location.rectTransform,32,16);
            _location.resizeTextForBestFit=true; _location.resizeTextMinSize=16; _location.resizeTextMaxSize=22;
            _portraitFrame=Ui.Surface("PortraitFrame",Root,Theme.Panel,Theme.AccentDim);
            _portraitFrame.anchorMin=new Vector2(.76f,0); _portraitFrame.anchorMax=Vector2.one;
            _portraitFrame.offsetMin=new Vector2(4,10); _portraitFrame.offsetMax=new Vector2(-12,-10);
            _portrait=Picture("EmployerPortrait",_portraitFrame); Ui.Stretch(_portrait.rectTransform,2,2,2,2);
            _greetingRoot=Ui.Surface("EmployerGreeting",Root,Theme.Panel,Theme.AccentDim);
            _greetingRoot.anchorMin=new Vector2(.025f,.09f); _greetingRoot.anchorMax=new Vector2(.735f,.79f);
            _greetingRoot.offsetMin=_greetingRoot.offsetMax=Vector2.zero;
            _greeting=Ui.Label("GreetingText",_greetingRoot,"",29,TextAnchor.MiddleLeft,Theme.Text);
            Ui.Stretch(_greeting.rectTransform,18,18,10,10);
            _greeting.resizeTextForBestFit=true; _greeting.resizeTextMinSize=21; _greeting.resizeTextMaxSize=29;
            _greetingRoot.gameObject.SetActive(false);
        }
        static Image Picture(string name,Transform parent)
        {
            var rt=Ui.Rect(name,parent); var image=rt.gameObject.AddComponent<Image>();
            image.preserveAspect=true; image.raycastTarget=false; return image;
        }
        internal void EnableTalk(Action talk)
        {
            var button=Root.gameObject.AddComponent<Button>();
            button.targetGraphic=Root.GetComponent<Image>(); button.transition=Selectable.Transition.None;
            button.onClick.AddListener(()=>talk());
            if(button.targetGraphic!=null)button.targetGraphic.raycastTarget=true;
        }
        internal void BeginGreeting(string npc)=>BeginLine(npc,EmployerGreeting.Line(npc,_greetingVariant++));
        internal void BeginLine(string npc,string line)
        {
            if(string.IsNullOrEmpty(line))return;
            _speech=new NpcSpeech(_greeting,npc,line); _greetingAge=0; _greetingRoot.gameObject.SetActive(true);
        }
        internal void TickGreeting(float delta)
        {
            if(_speech==null)return;
            _speech.Tick(delta); _greetingAge+=Mathf.Min(delta,.25f);
            if(_greetingAge>8 && _speech.IsComplete)StopGreeting();
        }
        internal void StopGreeting() { _speech=null; _greetingRoot.gameObject.SetActive(false); }
        internal void SetEmployer(string id)
        {
            if(_initialized && _shown==id)return;
            _initialized=true; _shown=id;
            bool chosen=id=="HWANG" || id=="DR_CHOI" || id=="YONGSAN_KIM";
            _portraitFrame.gameObject.SetActive(chosen);
            _background.rectTransform.anchorMax=new Vector2(chosen ? .75f : 1,1);
            _location.rectTransform.anchorMax=new Vector2(chosen ? .75f : 1,1);
            _portrait.sprite=chosen ? GameArt.Portrait(id) : null;
            _background.sprite=chosen ? GameArt.Location(id) : GameArt.City();
            _location.text=Location(id);
        }
        internal static string Location(string id)
        {
            switch(id) {
                case "HWANG":return Loc.Text("군수 창고 / SUPPLY DEPOT");
                case "DR_CHOI":return Loc.Text("작은 진료소 / FIELD CLINIC");
                case "YONGSAN_KIM":return Loc.Text("용산 작업실 / ELECTRONICS");
                default:return Loc.Text("서울 / 아직 불빛이 남아 있는 도시");
            }
        }
    }
}