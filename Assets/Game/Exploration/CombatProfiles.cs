using System;
using System.Collections.Generic;

namespace AfterSeoul.Exploration
{
    public sealed class WeaponProfile
    {
        public FireMode[] Modes;
        public double Damage, Accuracy;
        public int Magazine;
        public string Caliber;
    }

    public sealed class ConsumableProfile
    {
        public double Hp, Energy, Hydration, Seconds;
    }

    public static class CombatProfiles
    {
        // Magazine/caliber from SeoulLogic/ItemDatabase.cs; modes, damage and accuracy are mobile combat tuning.
        static readonly Dictionary<string, WeaponProfile> Weapons = new Dictionary<string, WeaponProfile>();
        static CombatProfiles()
        {
            Add("WPN01", 30, "5.45x39", 24, true);
            Add("WPN02", 30, "5.56x45", 25, true);
            Add("WPN03", 5, "7.62x54R", 58);
            Add("WPN04", 17, "9x19", 22);
            Add("WPN05", 30, "5.56x45", 27, true);
            Add("WPN06", 20, "9x39", 28, true);
            Add("WPN07", 33, "9x19", 20, true);
            Add("WPN08", 30, "9x19", 21, true, true);
            Add("WPN09", 8, "12/70", 44);
            Add("WPN10", 20, "7.62x51", 40);
            Add("WPN11", 10, "7.62x39", 35);
            Add("WPN12", 10, "7.62x54R", 47);
            Add("WPN13", 50, "5.7x28", 21, true);
            Add("WPN14", 30, "4.6x30", 22, true);
            Add("WPN15", 30, "7.62x51", 31, true);
            Add("WPN16", 20, "7.62x51", 39);
            Add("WPN17", 30, "7.62x39", 30, true);
            Add("WPN18", 10, "7.62x51", 55);
            Add("WPN19", 4, "23x75", 60);
            Add("WPN20", 30, "5.56x45", 26, true, true);
            Add("WPN21", 7, ".50 AE", 50);
            Add("WPN22", 5, "MXMR", 70);
            Add("WPN23", 6, "STUN", 35);
            Add("WPN26", 20, "12/70", 38, true);
            Add("WPN27", 95, "5.45x39", 25, true);
        }

        static void Add(string id, int mag, string caliber, double damage, bool auto = false, bool burst = false)
        {
            Weapons[id] = new WeaponProfile{Magazine = mag, Caliber = caliber, Damage = damage, Accuracy = .88, Modes = burst ? new[]{FireMode.Single, FireMode.Burst, FireMode.Auto} : auto ? new[]{FireMode.Single, FireMode.Auto} : new[]{FireMode.Single}};
        }

        public static WeaponProfile For(string id)
        {
            WeaponProfile p;
            return id != null && Weapons.TryGetValue(id, out p) ? p : null;
        }

        // Food restores exactly match mainline ItemDatabase. Medical healing amounts are mobile tuning, not mainline charge counts.
        public static ConsumableProfile ConsumableFor(string id)
        {
            switch (id)
            {
                case "FOOD01":
                    return Food(50, -5);
                case "FOOD02":
                    return Food(15, 40);
                case "FOOD03":
                    return Food(60, -10);
                case "FOOD04":
                    return Food(70, -5);
                case "FOOD05":
                    return Food(10, 100);
                case "FOOD06":
                    return Food(20, -50);
                case "FOOD07":
                    return Food(20, 30);
                case "FOOD08":
                    return Food(30, -15);
                case "FOOD09":
                    return Food(50, 5);
                case "FOOD10":
                    return Food(40, -15);
                case "FOOD11":
                    return Food(45, -10);
                case "FOOD12":
                    return Food(25, 35);
                case "FOOD13":
                    return Food(75, -25);
                case "MED01":
                case "MED02":
                case "MED15":
                    return new ConsumableProfile{Hp = 65, Seconds = 3};
                case "MED03":
                    return new ConsumableProfile{Hp = 100, Seconds = 4};
                case "MED05":
                    return new ConsumableProfile{Hp = 35, Seconds = 2};
                case "MED16":
                    return new ConsumableProfile{Hp = 20, Seconds = 1.8};
                default:
                    return null;
            }
        }

        static ConsumableProfile Food(double energy, double water)
        {
            return new ConsumableProfile{Energy = energy, Hydration = water, Seconds = 2};
        }
    }
}
