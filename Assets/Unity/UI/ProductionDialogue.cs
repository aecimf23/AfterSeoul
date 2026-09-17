using AfterSeoul.Core;

namespace AfterSeoul.Unity.UI
{
    /// <summary>Original mobile commission dialogue; mainline voice references are in PRODUCTION_ART.md.</summary>
    public static class ProductionDialogue
    {
        public static string Offer(string weaponName, int quantity, long reward)
        {
            return Loc.Text("운영 자금은 모았군. 다음 납품처에서 {0}을 찾고 있어. 우선 {1}정만 만들어 봐. 재료는 내가 댄다.\n\n마감이 좀 거칠어도 괜찮아. 완성품을 가져와. 작업을 끝까지 맡길 수 있는지 보려는 거야.\n\n물량을 채워 납품하면 {2:N0}원 지급하지. 그다음부터는 이 총도 정식으로 맡기겠다.", weaponName, quantity, reward);
        }

        public static string Accepted(string weaponName)
        {
            return Loc.Text("좋아. {0} 시험 제작부터 시작해. 다른 일을 하다 와도 완성한 수량은 남겨 두겠다. 다 채우면 나한테 가져와.", weaponName);
        }

        public static string Completed(string weaponName)
        {
            return Loc.Text("{0}, 약속한 수량은 다 왔군. 마감은 좀 거칠어도 작업은 끝냈어. 보수 챙겨. 다음 물량부터는 정식으로 맡기지. 재료는 계속 내가 댄다.", weaponName);
        }
    }
}
