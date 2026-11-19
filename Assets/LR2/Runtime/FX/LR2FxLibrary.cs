using UnityEngine;

namespace LR2.FX
{
    /// <summary>
    /// D60: the materials runtime effects use, saved as assets by LR2ContentBuilder.BuildFx under Resources so player builds
    /// include them. Fire, smoke, mist and barrel heat used to build HDRP/Unlit transparent materials at runtime; a player
    /// build strips the shader variants no built material uses, so those effects rendered as opaque quads ("the flames and
    /// the smokes are literal not see through boxes", user 11-18). FireHazard also looked for M_FX_Spark/M_FX_Smoke under
    /// Resources, where they never were. These materials use LR2/ParticleUnlit (no keywords, nothing to strip) or a validated
    /// HDRP template, with the Unity Particle Pack flipbooks (fire 6x6, steam 8x8) when that pack is installed.
    /// </summary>
    public class LR2FxLibrary : ScriptableObject
    {
        public const string ResourcePath = "LR2/FXLibrary";

        [Header("Fire hazard")]
        public Material fire;
        public Vector2Int fireTiles = Vector2Int.one;
        public Material smoke;
        public Vector2Int smokeTiles = Vector2Int.one;
        public Material embers;
        [Tooltip("B87: soft additive glow at the base of a fire.")]
        public Material fireGlow;

        [Header("Capture zone (B100)")]
        [Tooltip("HDRP decal: crisp dashed outer capture ring, projected onto whatever ground is below.")]
        public Material zoneRingOuter;
        [Tooltip("HDRP decal: solid inner core ring.")]
        public Material zoneRingCore;

        [Header("Atmosphere")]
        public Material mist;
        public Vector2Int mistTiles = Vector2Int.one;

        [Header("Weapon heat")]
        public Material barrelSmoke;
        public Vector2Int barrelSmokeTiles = Vector2Int.one;
        public Material muzzleHeat;
        public Material heatHaze;

        [Header("Unity Particle Pack (B112, re-materialised by PackFx)")]
        public GameObject packExplosion;        // TinyExplosion: gas cylinders and other explosive props
        public GameObject packEnergyExplosion;  // EnergyExplosion: seeker drones
        public GameObject packSparks;           // ElectricalSparks: shorting light fittings
        public GameObject packSteam;            // PressurisedSteam: a punctured pipe vents
        public GameObject packSteamStatic;      // PressurizedSteamStatic: standing leaks on pipe runs

        private static LR2FxLibrary _instance;
        private static bool _loaded;

        public static LR2FxLibrary Instance
        {
            get
            {
                if (!_loaded)
                {
                    _loaded = true;
                    _instance = Resources.Load<LR2FxLibrary>(ResourcePath);
                }
                return _instance;
            }
        }

        /// <summary>Plays a flipbook sheet once over each particle's life; turns the module off for a single image.</summary>
        public static void ApplyFlipbook(ParticleSystem ps, Vector2Int tiles, float cycles = 1f)
        {
            if (ps == null) return;
            var sheet = ps.textureSheetAnimation;
            if (tiles.x * tiles.y <= 1)
            {
                sheet.enabled = false;
                return;
            }
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = Mathf.Max(1, tiles.x);
            sheet.numTilesY = Mathf.Max(1, tiles.y);
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.timeMode = ParticleSystemAnimationTimeMode.Lifetime;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 0.999f));
            sheet.cycleCount = Mathf.Max(1, Mathf.RoundToInt(cycles));
        }

        /// <summary>Sets a material's tint on whichever colour property its shader has.</summary>
        public static void SetTint(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_UnlitColor")) m.SetColor("_UnlitColor", c);
        }
    }
}
