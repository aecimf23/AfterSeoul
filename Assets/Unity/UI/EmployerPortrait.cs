using AfterSeoul.Core;
namespace AfterSeoul.Unity.UI
{
    internal static class EmployerPortrait
    {
        internal static string Art(string id)
        {
            switch (id)
            {
                case "HWANG": return @"    _______
   /_______\
   | -   - |
   |   >   |
    \_____/
  __/|___|\__
 /__  [#]  __\
 |__|_____|__|";
                case "DR_CHOI": return @"     _____
    /_____\
   | [o-o] |
   |   >   |
    \_____/
  __/|   |\__
 / + |___|   \
 |___|___|___|";
                case "YONGSAN_KIM": return @"     _____
   _/_____\_
  [| o   o |]
   |   >   |_
    \_____/
  __/|___|\__
 /  / [=] \  \
 |__|_____|__|";
                default: return @"     .---.
    /     \
    | ? ? |
    |  -  |
     \___/
   __/   \__
  /         \
  |_________|";
            }
        }
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
