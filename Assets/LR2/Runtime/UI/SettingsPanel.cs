using System.Collections.Generic;
using LR2.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LR2.UI
{
    /// <summary>Settings screen bound to LR2Settings (used in the main menu and the pause menu).</summary>
    public class SettingsPanel : MonoBehaviour
    {
        [Header("Controls")]
        public Slider sensitivity;
        public Slider adsSensitivity;
        public Toggle invertY;

        [Header("View")]
        public Slider fieldOfView;
        public Toggle bodycamLook;
        public Slider bodycamMotion;
        public Toggle motionBlur;
        public Toggle filmGrain;

        [Header("Audio")]
        public Slider masterVolume;
        public Slider sfxVolume;
        public Slider musicVolume;
        public Slider uiVolume;
        public Toggle hitmarkerSound;

        [Header("Graphics")]
        public TMP_Dropdown quality;
        public Toggle vsync;
        public TMP_Dropdown frameLimit;
        public Toggle showFps;

        [Header("Gameplay")]
        public TMP_Dropdown botDifficulty;

        [Header("Actions (optional)")]
        public Button resetDefaultsButton;

        [Header("Value labels (optional)")]
        public TMP_Text sensitivityValue;
        public TMP_Text adsSensitivityValue;
        public TMP_Text fovValue;
        public TMP_Text bodycamMotionValue;
        public TMP_Text masterVolumeValue;
        public TMP_Text sfxVolumeValue;
        public TMP_Text musicVolumeValue;
        public TMP_Text uiVolumeValue;

        private static readonly int[] FrameLimits = { 0, 60, 120, 144, 165, 240 };
        private bool _loading;

        private void OnEnable() => Load();

        public void Load()
        {
            _loading = true;
            Set(sensitivity, 0.1f, 5f, LR2Settings.MouseSensitivity);
            Set(adsSensitivity, 0.3f, 1.5f, LR2Settings.AdsSensitivityMultiplier);
            Set(fieldOfView, 70f, 115f, LR2Settings.FieldOfView, true);
            Set(bodycamMotion, 0f, 1.5f, LR2Settings.BodycamMotion);
            Set(masterVolume, 0f, 1f, LR2Settings.MasterVolume);
            Set(sfxVolume, 0f, 1f, LR2Settings.SfxVolume);
            Set(musicVolume, 0f, 1f, LR2Settings.MusicVolume);
            Set(uiVolume, 0f, 1f, LR2Settings.UiVolume);
            if (invertY != null) invertY.isOn = LR2Settings.InvertY;
            if (bodycamLook != null) bodycamLook.isOn = LR2Settings.BodycamLook;
            if (motionBlur != null) motionBlur.isOn = LR2Settings.MotionBlur;
            if (filmGrain != null) filmGrain.isOn = LR2Settings.FilmGrain;
            if (hitmarkerSound != null) hitmarkerSound.isOn = LR2Settings.HitmarkerSound;
            if (vsync != null) vsync.isOn = LR2Settings.VSync;
            if (showFps != null) showFps.isOn = LR2Settings.ShowFps;

            if (quality != null)
            {
                quality.ClearOptions();
                quality.AddOptions(new List<string>(QualitySettings.names));
                quality.value = LR2Settings.QualityLevel >= 0 ? LR2Settings.QualityLevel : QualitySettings.GetQualityLevel();
            }
            if (frameLimit != null)
            {
                frameLimit.ClearOptions();
                frameLimit.AddOptions(new List<string> { "Unlimited", "60", "120", "144", "165", "240" });
                frameLimit.value = Mathf.Max(0, System.Array.IndexOf(FrameLimits, LR2Settings.FrameRateLimit));
            }
            if (botDifficulty != null)
            {
                botDifficulty.ClearOptions();
                botDifficulty.AddOptions(new List<string> { "Recruit", "Regular", "Hardened", "Veteran" });
                botDifficulty.value = LR2Settings.BotDifficulty;
            }
            UpdateLabels();
            _loading = false;
        }

        private void Awake()
        {
            Hook(sensitivity, v => LR2Settings.MouseSensitivity = v);
            Hook(adsSensitivity, v => LR2Settings.AdsSensitivityMultiplier = v);
            Hook(fieldOfView, v => LR2Settings.FieldOfView = v);
            Hook(bodycamMotion, v => LR2Settings.BodycamMotion = v);
            Hook(masterVolume, v => { LR2Settings.MasterVolume = v; AudioListener.volume = v; });
            Hook(sfxVolume, v => LR2Settings.SfxVolume = v);
            Hook(musicVolume, v => LR2Settings.MusicVolume = v);
            Hook(uiVolume, v => LR2Settings.UiVolume = v);
            Hook(invertY, v => LR2Settings.InvertY = v);
            Hook(bodycamLook, v => LR2Settings.BodycamLook = v);
            Hook(motionBlur, v => LR2Settings.MotionBlur = v);
            Hook(filmGrain, v => LR2Settings.FilmGrain = v);
            Hook(hitmarkerSound, v => LR2Settings.HitmarkerSound = v);
            Hook(vsync, v => { LR2Settings.VSync = v; LR2Settings.Apply(); });
            Hook(showFps, v => LR2Settings.ShowFps = v);
            if (quality != null) quality.onValueChanged.AddListener(i => { if (_loading) return; LR2Settings.QualityLevel = i; LR2Settings.Apply(); });
            if (frameLimit != null) frameLimit.onValueChanged.AddListener(i => { if (_loading) return; LR2Settings.FrameRateLimit = FrameLimits[Mathf.Clamp(i, 0, FrameLimits.Length - 1)]; LR2Settings.Apply(); });
            if (botDifficulty != null) botDifficulty.onValueChanged.AddListener(i => { if (!_loading) LR2Settings.BotDifficulty = i; });
            if (resetDefaultsButton != null) resetDefaultsButton.onClick.AddListener(ResetDefaults);
        }

        private void OnDisable() => LR2Settings.Save();

        private static void Set(Slider s, float min, float max, float value, bool whole = false)
        {
            if (s == null) return;
            s.minValue = min;
            s.maxValue = max;
            s.wholeNumbers = whole;
            s.value = value;
        }

        private void Hook(Slider s, System.Action<float> apply)
        {
            if (s == null) return;
            s.onValueChanged.AddListener(v =>
            {
                if (_loading) return;
                apply(v);
                UpdateLabels();
            });
        }

        private void Hook(Toggle t, System.Action<bool> apply)
        {
            if (t == null) return;
            t.onValueChanged.AddListener(v => { if (!_loading) apply(v); });
        }

        private void UpdateLabels()
        {
            if (sensitivityValue != null) sensitivityValue.text = LR2Settings.MouseSensitivity.ToString("0.00");
            if (adsSensitivityValue != null) adsSensitivityValue.text = LR2Settings.AdsSensitivityMultiplier.ToString("0.00x");
            if (fovValue != null) fovValue.text = Mathf.RoundToInt(LR2Settings.FieldOfView).ToString();
            if (bodycamMotionValue != null) bodycamMotionValue.text = Mathf.RoundToInt(LR2Settings.BodycamMotion * 100f) + "%";
            if (masterVolumeValue != null) masterVolumeValue.text = Mathf.RoundToInt(LR2Settings.MasterVolume * 100f) + "%";
            if (sfxVolumeValue != null) sfxVolumeValue.text = Mathf.RoundToInt(LR2Settings.SfxVolume * 100f) + "%";
            if (musicVolumeValue != null) musicVolumeValue.text = Mathf.RoundToInt(LR2Settings.MusicVolume * 100f) + "%";
            if (uiVolumeValue != null) uiVolumeValue.text = Mathf.RoundToInt(LR2Settings.UiVolume * 100f) + "%";
        }

        public void ResetDefaults()
        {
            LR2Settings.ResetToDefaults();
            LR2Settings.Apply();
            Load();
        }
    }
}
