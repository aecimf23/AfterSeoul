using System;
using AfterSeoul.Core;
using AfterSeoul.Factory;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    internal sealed partial class ProductionFactoryView
    {
        private Button _automationHelp;
        private Text _guideError;

        public void MaybeShowGuide()
        {
            if (!_session.Save.FactoryTutorialSeen && _dialog == null) OpenGuide(0);
        }

        private bool FinishGuide()
        {
            try {
                if (!_session.Save.FactoryTutorialSeen && !_session.ExecuteSavedAction(s => { s.FactoryTutorialSeen=true; return true; })) return false;
                CloseDialog(); return true;
            } catch (Exception) {
                if (_guideError != null) _guideError.text=Loc.Text("안내 상태를 저장하지 못했습니다. 다시 눌러 주세요.");
                return false;
            }
        }

        private void GuideGo(Action action) { if (FinishGuide()) action(); }
        private void OpenCrewGuideDestination() { _equipmentPage=2; _equipment(); }

        private static void GuideButton(Transform parent,string id,string label,Action action)
        {
            var b=Ui.Button(id,parent,Loc.Text(label),action,Theme.PanelAlt,26);
            Ui.Size(b.gameObject,80,flexWidth:1,flexHeight:0);
        }

        private void OpenGuide(int page)
        {
            CloseDialog();
            string[] titles={"직접 조립해서 작업비 받기","업그레이드 고르는 방법","자동 생산 시작하기","번 돈으로 다음 목표 준비","부품 공급 설비","재료 선별 장치","동력 공구","추가 작업대"};
            string[] explanations={
                "용산킴이 부품을 제공합니다. 움직이는 부품이 중앙 판정 구간에 들어오면 조립 버튼을 누르세요.\n\n정식 제작 총기는 완성 즉시 자동 납품되어 작업비를 받습니다. 시제품 의뢰는 목표 수량을 만든 뒤 직접 납품해야 보수와 제작 권한을 받습니다.",
                "부품 공급 설비: 부품이 더 자주 오고, 배치 인원의 자동 제작도 빨라집니다.\n\n재료 선별 장치: 직접 조립할 때 작업량이 큰 상위 등급 부품이 등장합니다.\n\n동력 공구: 배치한 스캐브의 자동 제작 속도를 높입니다.\n\n추가 작업대: 자동 제작 인원을 최대 3명까지 배치합니다. 작업대만 사면 인원이 생기는 것은 아닙니다.",
                "자동 생산은 스캐브를 공장에 배치해야 시작됩니다. 동력 공구를 먼저 사더라도 배치 인원이 0명이면 자동 생산도 0입니다.\n\n인원 관리에서 고용 → 설비 관리 / 인원 배치에서 대기 스캐브 배치 → 생산 화면의 자동 작업량 확인 순서입니다.\n\n앱을 꺼도 오프라인 정산 한도 안에서 작업합니다. 시제품 목표를 채우면 직접 납품할 때까지 해당 시제품 제작은 멈춥니다. 파견하려면 공장 배치를 먼저 해제하세요.",
                "작업비는 다음 탐색과 성장을 준비하는 돈입니다. 아래 중 지금 필요한 목표 하나를 고르세요.\n\n① 탐색 준비: 장착한 총기에 맞는 탄약과 치료제·음식·물을 준비하세요. 출발 준비에서 기본 물자를 사고 장비를 교체할 수 있습니다.\n\n② 의뢰 수행: 기지의 목표에서 요구 물품과 보상을 확인하고 탐색하세요. 생존 귀환으로 회수한 재료는 기지 시설 개선과 장비 교환에도 쓰입니다.\n\n③ 수입 늘리기: 남은 돈으로 필요한 생산 설비를 강화하거나 인원을 고용·배치하세요. 새 총기는 제작 승인 의뢰의 조건과 납품 보수를 확인한 뒤 진행하세요. 돈을 전부 강화에 쓰기 전에 다음 탐색 물자를 먼저 챙기세요.",
                "직접 조립할 부품을 기다리는 시간이 길다면 강화하세요. 공급 간격이 짧아지고, 공장에 배치한 인원의 자동 작업량도 늘어납니다.\n\n강화 전 카드의 현재 → 다음 수치와 필요 금액을 확인하세요. 스캐브가 없을 때는 부품 공급만 빨라지며 자동 생산은 시작되지 않습니다.",
                "직접 조립으로 작업량을 더 많이 채우고 싶을 때 강화하세요. 상위 등급 부품의 등장 확률이 증가하며 일부 레벨에서는 새 등급이 열립니다.\n\n높은 등급도 중앙 판정 구간에 맞춰 눌러야 합니다. 이 장치는 자동 제작 인원을 대신하지 않습니다.",
                "먼저 스캐브를 배치한 뒤 자동 생산을 더 빠르게 하고 싶을 때 강화하세요.\n\n배치 인원이 0명이면 현재·다음 자동 작업량이 모두 0으로 보입니다. 먼저 인원 배치에서 대기 스캐브를 배치하고 효과를 확인하세요.",
                "기본 작업대에는 스캐브 1명을 배치할 수 있습니다. 더 많은 대기 스캐브를 함께 일하게 하려면 작업대를 늘리세요. 최대 배치 인원은 3명입니다.\n\n강화 후 인원 배치에서 추가 인원을 직접 배치해야 자동 작업량이 올라갑니다. 파견 중이거나 부상 중인 스캐브는 바로 배치할 수 없습니다."
            };
            _dialog=Ui.Modal("FactoryGuide",_shell.transform.GetChild(0),Loc.Text(page<4 ? "공장 안내 · {0}/4" : "설비 도움말",page+1),()=>{FinishGuide();},out var body);
            var heading=Ui.Paragraph("FactoryGuideTitle",body,Loc.Text(titles[page]),34,Theme.Accent);Ui.Size(heading.gameObject,flexHeight:0);
            var text=Ui.Paragraph("FactoryGuideText",body,Loc.Text(explanations[page]),28,Theme.Text);Ui.Size(text.gameObject,flexHeight:0);
            if(page==2 || page==6 || page==7) {
                double rate=ProductionWork.AutoWorkPerSecond(_session.Save,_session.Data);
                bool idle=_session.Save.Scavs.Exists(s=>s.Status==ScavStatus.Idle);
                string status=rate>0 ? Loc.Text("현재 자동 작업량 {0:0.###}/초 · 인원 배치를 관리할 수 있습니다.",rate)
                    : idle ? Loc.Text("대기 중인 스캐브가 있습니다. 인원 배치에서 선택하세요.")
                    : _session.Save.Scavs.Count==0 ? Loc.Text("아직 고용한 스캐브가 없습니다. 인원 관리에서 고용 조건을 확인하세요.")
                    : Loc.Text("현재 배치할 대기 인원이 없습니다. 파견·치료 상태를 인원 관리에서 확인하세요.");
                var state=Ui.Paragraph("FactoryGuideStatus",body,status,28,Theme.Info);Ui.Size(state.gameObject,flexHeight:0);
                if(idle || rate>0) GuideButton(body,"FactoryGuideAssign","인원 배치로 이동",()=>GuideGo(OpenCrewGuideDestination));
                else GuideButton(body,"FactoryGuidePersonnel","인원 관리로 이동",()=>GuideGo(()=>{if(_session.Save.Scavs.Count==0)_shell.OpenRecruitment();else _shell.SelectByName("인원");}));
            }
            if(page==3) {
                GuideButton(body,"FactoryGuideExplore","탐색 장비·물자 준비하기",()=>GuideGo(()=>_shell.OpenExploration()));
                GuideButton(body,"FactoryGuideGoals","기지에서 의뢰와 다음 목표 확인",()=>GuideGo(()=>_shell.SelectByName("기지")));
                GuideButton(body,"FactoryGuideUpgrades","공장 설비 비교하기",()=>GuideGo(()=>{_production();ScrollToUpgrades();}));
            }
            _guideError=Ui.Paragraph("FactoryGuideError",body,"",26,Theme.Warn);Ui.Size(_guideError.gameObject,flexHeight:0);
            var footer=Ui.Rect("FactoryGuideFooter",_dialog.Find("Panel"));Ui.Row(footer,12);Ui.Size(footer.gameObject,88,flexHeight:0);
            if(page>0 && page<4) GuideButton(footer,"FactoryGuideBack","이전",()=>OpenGuide(page-1));
            if(page<3) GuideButton(footer,"FactoryGuideNext","다음",()=>OpenGuide(page+1));
            else GuideButton(footer,"FactoryGuideDone",page==3?"이해했어요 · 생산 계속하기":"닫기",()=>{FinishGuide();});
        }
    }
}
