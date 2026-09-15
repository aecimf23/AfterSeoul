using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using AfterSeoul.Unity.UI;

namespace AfterSeoul.Tests
{
    public class ThemeSettingsTests
    {
        private const string Key = "AfterSeoul.UI.Theme";
        private bool _had;
        private string _old;
        [SetUp] public void Setup() { _had = PlayerPrefs.HasKey(Key); _old = PlayerPrefs.GetString(Key); }
        [TearDown] public void Cleanup()
        {
            if (_had) PlayerPrefs.SetString(Key, _old); else PlayerPrefs.DeleteKey(Key);
            typeof(Theme).GetMethod("Reload")?.Invoke(null, null);
            PlayerPrefs.Save();
        }
        private static void Reload()
        {
            var method = typeof(Theme).GetMethod("Reload");
            Assert.IsNotNull(method, "Theme must load persisted selection without rebuilding game state.");
            method.Invoke(null, null);
        }
        [Test] public void MissingOrUnknownThemeFallsBackToSeoulNight()
        {
            PlayerPrefs.DeleteKey(Key); Reload();
            var night = Theme.Accent;
            Assert.Greater(night.r, night.g, "Default is warm amber against navy.");
            PlayerPrefs.SetString(Key, "invalid"); Reload();
            Assert.AreEqual(night, Theme.Accent);
        }
        [Test] public void ThreeThemesPersistAndKeepDangerMeaning()
        {
            var select = typeof(Theme).GetMethod("Select");
            Assert.IsNotNull(select);
            var danger = Theme.Danger;
            Color? last = null;
            foreach (var id in new[] { "night", "military", "shelter" })
            {
                select.Invoke(null, new object[] { id });
                Assert.AreEqual(id, PlayerPrefs.GetString(Key));
                var c = Theme.Accent; Reload();
                Assert.AreEqual(c, Theme.Accent);
                Assert.AreEqual(danger, Theme.Danger);
                if (last.HasValue) Assert.AreNotEqual(last.Value, c);
                last = c;
            }
        }
    }
}
