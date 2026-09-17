using AfterSeoul.Core;
namespace AfterSeoul.Unity.UI
{
    internal static class EmployerPortrait
    {
        internal static string Role(string id)
        {
            switch (id)
            {
                case "HWANG": return Loc.Text("군수 물자 / 거래와 보급");
                case "DR_CHOI": return Loc.Text("의료 지원 / 생존자 보호");
                case "YONGSAN_KIM": return Loc.Text("전자 장비 / 정보와 회수");
                default: return Loc.Text("현장 배속 대기 중");
            }
        }
    }
}
