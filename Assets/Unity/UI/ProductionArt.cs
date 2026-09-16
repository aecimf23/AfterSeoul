namespace AfterSeoul.Unity.UI
{
    /// <summary>
    /// Original mobile silhouettes of the mainline weapon catalog (not imported mainline art).
    /// Render with Theme.ArtFont, rich text disabled, and horizontal wrapping disabled.
    /// Weapons fit 36 columns by 5 lines; worker stations fit 20 columns by 8 lines.
    /// </summary>
    public static class ProductionArt
    {
        public static string Weapon(string weaponId)
        {
            switch (weaponId)
            {
                case "WPN01": // AV-74M: curved magazine, gas tube, solid stock.
                    return @"               ______________
     ____ ____/___|_|||||____|===-
    /____|______[___]___/
             /_/  \  \
                   \__\";
                case "WPN02": // C4-A1: carry handle and telescoping stock.
                    return @"             __--__
     [==]___[______]__|||||=====+
     [__]===|_____[___]___/
              /_/ |  |
                  |__|";
                case "WPN03": // Morzin: full-length stock and exposed bolt.
                    return @"                   o
      _____________|____________
    _/_________[___]____________===-
   /____/     (___)
   \___/";
                case "WPN04": // P17: compact squared slide, angled grip.
                    return @"          ______________
         |__|___________|=
         |____  ___/
        /    /(_)
       /____/";
                case "WPN05": // AR-416: full rail and vented handguard.
                    return @"           _|_|_______________
    [===]_|___|_____|: : : :|===+
    [___]===|____[__]______/
              /_/ |  |
                  |__|";
                case "WPN06": // AS-39: wire stock and integral suppressor.
                    return @"      _____      _____________
     /____/=====|_____|========|==
     \___/      |___|_|________|
                 /_/ \  \
                      \__\";
                case "WPN07": // V9-33: compact receiver and long stick magazine.
                    return @"          ______________
    [==]==|_____|: : : :|==-
         |___[___]____/
          /_/ | |
              |_|";
                case "WPN08": // MP-5R: ring sight and curved magazine.
                    return @"              ____       o
     /===\___|____|______|===-
     \___/===|____[___]__/
               /_/ \ \
                    \_\";
                case "WPN09": // SG-153: sporting stock and long tube magazine.
                    return @"                 _______________
      __________|_______________==
    _/_____[___]______======
   /____/   (___)
   \___/";
                case "WPN10": // RSAS: scope, box magazine, tactical stock.
                    return @"               [==========]
     [===]______[__]__|_________
     [___]====|____[__]||||||=====+
               /_/ |__|
                           / \\";
                case "WPN11": // SC-45: wood stock and fixed short magazine.
                    return @"                ______________
     __________|___|__________====-
   _/_________[____]___/
  /____/       (__)\_/
  \___/";
                case "WPN12": // SV-D: scope and skeleton stock.
                    return @"                [=======]
      ______ ____|___|____________
     / /___/|____[___]__|||||=====+
     \____/    /_/ |  |
                   |__|";
                case "WPN13": // PD-90: top-mounted magazine and bullpup body.
                    return @"        ___________________
       /___________________|_
      |  ___   ____|_________|==
      | (___) (___)   ____/
      |______________/";
                case "WPN14": // MPX-7: compact PDW with grip magazine.
                    return @"              _|_|______
     [==]====|___|______|==+
             |___  ____|
                | |  | |
                |_|  |_|";
                case "WPN15": // BR-58: long battle rifle and straight magazine.
                    return @"                 ______________
     ______ ____|___|: : : : : |===
    /______|_____[__]__________/
              /_/ |   |
                  |___|";
                case "WPN16": // M1R: sporting stock, scope, box magazine.
                    return @"                [======]
      ___________|__|___________
    _/__________[___]____________==
   /____/    (__) |__|
   \___/";
                case "WPN17": // MX-47: AR receiver with curved magazine.
                    return @"              _|_|____________
    [====]___|____|___|||||||||===+
    [____]===|____[___]______/
               /_/ \   \
                    \___\";
                case "WPN18": // R700: scope, bolt handle, tapered stock.
                    return @"               [=========]
      __________|__|_o__________
    _/_________[_____|]_________===-
   /____/      (___)
   \___/";
                case "WPN19": // SG-23M: heavy barrel and ribbed pump.
                    return @"                ________________
      _________|________________|==
    _/____[____]====[|||||||]===
   /____/  (___)
   \___/";
                case "WPN20": // KR-2: folding stock and raised front sight.
                    return @"               ____         |_
     /===\____|____|_________|===+
     \___/====|___[___]|||||/
                /_/ |  |
                    |__|";
                case "WPN21": // Desert Hawk: large angular slide and grip.
                    return @"       ___________________
      /___|_______________|==
      |_____   ______/
       /   /(___)
      /___/";
                case "WPN22": // MXMR: large scope, bolt and skeletal chassis.
                    return @"              [============]
     [___]_____|__|_o_____________
     [_/]====|______[|]_________===+
               /_/ |__|    / \
                         /   \\";
                case "WPN23": // K-ESG: paired contact points on a short sidearm.
                    return @"          _____________
         |___|_________|==:
         |____  ___/   ==:
          /  /(_)
         /__/";
                case "WPN26": // SA-12K: short box-fed shotgun, broad magazine.
                    return @"                 ___________
     /===\______|___||||||||_|==
     \___/======|___[___]___/
                  /_/ |    |
                      |____|";
                case "WPN27": // RKP-16: drum magazine and bipod.
                    return @"                _______________
     [===]_____|___|_|||||||||_|===+
     [___]=====|___[___]_____/  / \
                 /_/ (    )   /   \
                      (__)";
                default:
                    return @"          _____________
         |  ?  [___]   |
         |_____________|
          |           |";
            }
        }

        public static string Worker(bool striking)
        {
            return WorkerFrame(striking ? 2 : 0);
        }

        /// <summary>
        /// Rest (0), lift (1), impact (2), recovery (3). Other values rest.
        /// Each station fits 20 columns by eight lines with an anchored bench.
        /// </summary>
        public static string WorkerFrame(int frame)
        {
            switch (frame)
            {
                case 1:
                    return @"   .---.     [##]
  /[o=o]\     |
  \_ -_/  ___o
  /|::|\_/
 o_|__|_ [==]===-
 __|__|__|_|_|___
 | /  \  [___]  |
 |/_||_\________|";
                case 2:
                    return @"   .---.
  /[o=o]\
  \_ -_/ __o   *
  /|::|\/ [##]
 o_|__|_ [==]===-
 __|__|__|_|_|___
 | /  \  [___]  |
 |/_||_\________|";
                case 3:
                    return @"   .---.
  /[o=o]\    [##]
  \_ -_/      |
  /|::|\____o/
 o_|__|_ [==]===-
 __|__|__|_|_|___
 | /  \  [___]  |
 |/_||_\________|";
                default:
                    return @"   .---.
  /[o=o]\
  \_ -_/
  /|::|\_o=[##]
 o_|__|_ [==]===-
 __|__|__|_|_|___
 | /  \  [___]  |
 |/_||_\________|";
            }
        }

        /// <summary>Assigned scavenger with a distinct cap, wrap or helmet.</summary>
        public static string ScavFrame(int frame, int variant = 0)
        {
            string headgear;
            switch (variant)
            {
                case 1: headgear = "  _/===\\"; break;
                case 2: headgear = "   /^^^\\"; break;
                default: headgear = "   .===."; break;
            }
            return WorkerFrame(frame).Replace("   .---.", headgear)
                .Replace("/[o=o]\\", "|[o_o]|");
        }

        /// <summary>Vacant station with the same bench and vise anchors.</summary>
        public static string EmptyBench()
        {
            return @"



         [==]===-
 ________|_|_|___
 |       [___]  |
 |______________|";
        }
    }
}