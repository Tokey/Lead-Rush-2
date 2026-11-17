using System;
using UnityEngine;

namespace LR2.Core
{
    /// <summary>
    /// Player preferences (persisted in PlayerPrefs). UI menus write here; gameplay systems read and listen to
    /// <see cref="Changed"/>. Call <see cref="Apply"/> after editing to push engine-level settings.
    /// </summary>
    public static class LR2Settings
    {
        public static event Action Changed;

        private const string Prefix = "LR2.";

        public static float MouseSensitivity { get => GetF("v2.MouseSensitivity", 1.0f); set => SetF("v2.MouseSensitivity", Mathf.Clamp(value, 0.05f, 10f)); }
        public static float AdsSensitivityMultiplier { get => GetF("v2.AdsSens", 0.75f); set => SetF("v2.AdsSens", Mathf.Clamp(value, 0.1f, 2f)); }
        public static float FieldOfView { get => GetF("FOV", 90f); set => SetF("FOV", Mathf.Clamp(value, 65f, 120f)); }
        public static bool InvertY { get => GetB("InvertY", false); set => SetB("InvertY", value); }
        public static float MasterVolume { get => GetF("MasterVol", 0.9f); set => SetF("MasterVol", Mathf.Clamp01(value)); }
        public static float SfxVolume { get => GetF("SfxVol", 1f); set => SetF("SfxVol", Mathf.Clamp01(value)); }
        public static float MusicVolume { get => GetF("MusicVol", 0.55f); set => SetF("MusicVol", Mathf.Clamp01(value)); }
        public static float UiVolume { get => GetF("UiVol", 0.8f); set => SetF("UiVol", Mathf.Clamp01(value)); }
        public static int QualityLevel { get => GetI("Quality", -1); set => SetI("Quality", value); }
        public static bool VSync { get => GetB("VSync", false); set => SetB("VSync", value); }
        public static int FrameRateLimit { get => GetI("FpsLimit", 0); set => SetI("FpsLimit", Mathf.Max(0, value)); }
        public static bool ShowFps { get => GetB("ShowFps", false); set => SetB("ShowFps", value); }
        public static bool MotionBlur { get => GetB("v2.MotionBlur", false); set => SetB("v2.MotionBlur", value); }
        public static bool FilmGrain { get => GetB("FilmGrain", true); set => SetB("FilmGrain", value); }
        public static bool HitmarkerSound { get => GetB("HitmarkerSound", true); set => SetB("HitmarkerSound", value); }
        public static int BotDifficulty { get => GetI("BotDifficulty", 1); set => SetI("BotDifficulty", Mathf.Clamp(value, 0, 3)); }
        /// <summary>Bodycam lens look (distortion, grain, aberration, grade).</summary>
        public static bool BodycamLook { get => GetB("v2.BodycamLook", false); set => SetB("v2.BodycamLook", value); }
        /// <summary>Bodycam camera motion (head bob, inertia sway, handheld shake). Default 0 for snappy Kinemation FPS camera.</summary>
        public static float BodycamMotion { get => GetF("v2.BodycamMotion", 0f); set => SetF("v2.BodycamMotion", Mathf.Clamp(value, 0f, 1.5f)); }
        public static string PlayerName { get => PlayerPrefs.GetString(Prefix + "PlayerName", "Operator"); set { PlayerPrefs.SetString(Prefix + "PlayerName", value); Notify(); } }

        /// <summary>Pushes engine-level settings (quality, vsync, frame cap, master volume).</summary>
        public static void Apply()
        {
            int q = QualityLevel;
            if (q >= 0 && q < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != q)
                QualitySettings.SetQualityLevel(q, true);
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = FrameRateLimit > 0 ? FrameRateLimit : -1;
            AudioListener.volume = MasterVolume;
        }

        public static void ResetToDefaults()
        {
            foreach (var key in new[] { "MouseSensitivity", "v2.MouseSensitivity", "AdsSens", "v2.AdsSens", "FOV", "InvertY",
                         "MasterVol", "SfxVol", "MusicVol", "UiVol", "Quality", "VSync", "FpsLimit", "ShowFps",
                         "MotionBlur", "v2.MotionBlur", "FilmGrain", "HitmarkerSound", "BotDifficulty",
                         "BodycamLook", "v2.BodycamLook", "BodycamMotion", "v2.BodycamMotion" })
                PlayerPrefs.DeleteKey(Prefix + key);
            Notify();
        }

        public static void Save() => PlayerPrefs.Save();

        private static float GetF(string k, float d) => PlayerPrefs.GetFloat(Prefix + k, d);
        private static int GetI(string k, int d) => PlayerPrefs.GetInt(Prefix + k, d);
        private static bool GetB(string k, bool d) => PlayerPrefs.GetInt(Prefix + k, d ? 1 : 0) != 0;
        private static void SetF(string k, float v) { PlayerPrefs.SetFloat(Prefix + k, v); Notify(); }
        private static void SetI(string k, int v) { PlayerPrefs.SetInt(Prefix + k, v); Notify(); }
        private static void SetB(string k, bool v) { PlayerPrefs.SetInt(Prefix + k, v ? 1 : 0); Notify(); }
        private static void Notify() => Changed?.Invoke();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Changed = null;
    }
}
