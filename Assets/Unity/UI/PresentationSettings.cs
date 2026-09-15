using UnityEngine;

namespace AfterSeoul.Unity.UI
{
    public static class PresentationSettings
    {
        public const string MotionKey = "AfterSeoul.UI.ReducedMotion";
        public static bool ReducedMotion
        {
            get => PlayerPrefs.GetInt(MotionKey, 0) != 0;
            set { PlayerPrefs.SetInt(MotionKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }
        public static void Reset()
        {
            ReducedMotion = false;
            Sfx.EffectsVolume = .55f;
            Sfx.MusicVolume = .18f;
            Sfx.EffectsMuted = false;
            Sfx.MusicMuted = false;
            Theme.Select("night");
        }
    }
}
