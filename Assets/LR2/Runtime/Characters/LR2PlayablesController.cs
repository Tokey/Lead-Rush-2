using KINEMATION.FPSAnimationFramework.Runtime.Playables;

namespace LR2.Characters
{
    /// <summary>
    /// D57: Kinemation's playables controller with two Animator curves gated by the soldier.
    ///
    /// Kinemation weights most of its weapon layers by Animator curves: FullBodyWeight masks the view, pose sampler,
    /// additive and collision layers (and on the pistol the look and support-hand layers), ProneWeight drives the
    /// blending layer. The curves live inside the locomotion clips, so script writes cannot change them, and they
    /// misfire in LR2: the rifle jog clips carry about 0.72 and the walk stride blend adds the sprint clip's 1.0, so
    /// every walk (and every push against a wall) handed the gun to the leg animation and dropped it into a low carry;
    /// a death while prone left ProneWeight at 1 for the whole next life, because the curve only exists in prone clips
    /// (C17-C20). The soldier decides when the arms really follow the body and passes the gated values here; Kinemation
    /// reads every curve through <see cref="GetCurveValue"/>.
    /// </summary>
    public class LR2PlayablesController : FPSPlayablesController
    {
        public const string FullBodyCurve = "FullBodyWeight";
        public const string ProneCurve = "ProneWeight";

        /// <summary>Value Kinemation's layers get for FullBodyWeight; negative = the raw Animator curve.</summary>
        public float FullBodyOverride { get; set; } = -1f;

        /// <summary>Value Kinemation's layers get for ProneWeight; negative = the raw Animator curve.</summary>
        public float ProneOverride { get; set; } = -1f;

        public override float GetCurveValue(string curveName, bool isAnimator = false)
        {
            if (isAnimator)
            {
                if (FullBodyOverride >= 0f && curveName == FullBodyCurve) return FullBodyOverride;
                if (ProneOverride >= 0f && curveName == ProneCurve) return ProneOverride;
            }
            return base.GetCurveValue(curveName, isAnimator);
        }
    }
}
