using System;
using System.Runtime.CompilerServices;
using KINEMATION.FPSAnimationFramework.Runtime.Core;
using LR2.Characters;
using LR2.Core;
using LR2.Weapons;
using UnityEngine;

namespace LR2.World
{
    /// <summary>Focused runtime assertions for regressions found after the original QA bot was written.</summary>
    public static class LR2QAExtraChecks
    {
        private const float MaxGunAngle = 50f;
        private const float MaxFootSlideSpeed = 0.5f;
        private const float MaxHandError = 0.03f;
        private const float MaxDeathAimBlend = 0.001f;
        private const float MinWeaponForwardDot = -0.08f;
        private const float MaxMotionFootSlideSpeed = 0.35f;
        private const int MaxMusicPriority = 32;
        private sealed class ReloadIdlePose
        {
            public Vector3 Forward;
            public Vector3 Up;
        }
        private static readonly ConditionalWeakTable<SoldierController, ReloadIdlePose> ReloadIdlePoses =
            new ConditionalWeakTable<SoldierController, ReloadIdlePose>();

        public static void CaptureReloadIdlePose(SoldierController soldier)
        {
            if (soldier == null || soldier.ActiveFirearm == null) return;
            ReloadIdlePoses.Remove(soldier);
            ReloadIdlePoses.Add(soldier, new ReloadIdlePose
            {
                Forward = soldier.ActiveFirearm.transform.forward,
                Up = soldier.ActiveFirearm.transform.up
            });
        }

        public static string CheckAbsoluteReloadPose(SoldierController soldier)
        {
            if (soldier == null || soldier.ActiveFirearm == null || soldier.ViewTransform == null)
                return "FAIL Reload.AbsolutePose: missing soldier, firearm, or view transform";

            var gun = soldier.ActiveFirearm.transform;
            var view = soldier.ViewTransform;
            float forwardAngle = Vector3.Angle(gun.forward, view.forward);
            float idleDelta = forwardAngle;
            if (ReloadIdlePoses.TryGetValue(soldier, out var idle))
            {
                idleDelta = Vector3.Angle(gun.forward, idle.Forward);
                float idleRoll = Vector3.SignedAngle(
                    Vector3.ProjectOnPlane(gun.up, idle.Forward),
                    Vector3.ProjectOnPlane(idle.Up, idle.Forward),
                    idle.Forward);
                bool baselinePass = idleDelta <= MaxGunAngle && Mathf.Abs(idleRoll) <= MaxGunAngle;
                return $"{(baselinePass ? "PASS" : "FAIL")} Reload.AbsolutePose: idleDelta={idleDelta:0.0}deg rollIdleDelta={idleRoll:0.0}deg max={MaxGunAngle:0.0}";
            }
            Vector3 gunUp = Vector3.ProjectOnPlane(gun.up, view.forward);
            Vector3 viewUp = Vector3.ProjectOnPlane(view.up, view.forward);
            float roll = gunUp.sqrMagnitude > 1e-6f && viewUp.sqrMagnitude > 1e-6f
                ? Vector3.SignedAngle(viewUp, gunUp, view.forward)
                : 0f;
            bool pass = forwardAngle <= MaxGunAngle && Mathf.Abs(roll) <= MaxGunAngle;
            return $"{(pass ? "PASS" : "FAIL")} Reload.AbsolutePose: forward={forwardAngle:0.0}deg roll={roll:0.0}deg max={MaxGunAngle:0.0}";
        }

        public static string CheckTacticalReload(LR2Firearm gun, int ammoAfter, float maxChargeTravel)
        {
            if (gun == null)
                return "FAIL Reload.TacticalInvariant: missing firearm";

            bool chargeStable = maxChargeTravel < 0.02f;
            bool ammoValid = ammoAfter == gun.MagazineSize + 1;
            return $"{(chargeStable && ammoValid ? "PASS" : "FAIL")} Reload.TacticalInvariant: charge={maxChargeTravel:0.000}m ammo={ammoAfter}/{gun.MagazineSize + 1}";
        }

        public static string CheckFootSlide(Vector3 previousFootPosition, Vector3 currentFootPosition, float deltaTime)
        {
            if (deltaTime <= 0f)
                return "FAIL Movement.FootSlide: non-positive sample interval";

            Vector3 delta = currentFootPosition - previousFootPosition;
            delta.y = 0f;
            float speed = delta.magnitude / deltaTime;
            return $"{(speed < MaxFootSlideSpeed ? "PASS" : "FAIL")} Movement.FootSlide: speed={speed:0.000}m/s max={MaxFootSlideSpeed:0.000}";
        }

        public static string CheckFootSlide(Vector3 previousFootPosition, Vector3 currentFootPosition, float deltaTime, bool footIsPlanted)
        {
            if (!footIsPlanted)
                return "PASS Movement.FootSlide: planted=false (swing frame excluded)";
            return CheckFootSlide(previousFootPosition, currentFootPosition, deltaTime);
        }

        public static string CheckHandsOnGun(RetargetProxy proxy)
        {
            if (proxy == null)
                return "FAIL Hands.OnGun: missing retarget proxy";

            float left = proxy.HandErrorLeft;
            float right = proxy.HandErrorRight;
            bool pass = left >= 0f && right >= 0f && left < MaxHandError && right < MaxHandError;
            return $"{(pass ? "PASS" : "FAIL")} Hands.OnGun: left={left:0.000}m right={right:0.000}m max={MaxHandError:0.000}m";
        }

        public static string CheckHandsOnGun(RetargetProxy proxy, LR2Firearm gun)
        {
            if (proxy == null) return "FAIL Hands.OnGun: missing retarget proxy";
            if (gun == null) return "FAIL Hands.OnGun: missing firearm";
            var visual = proxy.Visual;
            if (visual == null) return "FAIL Hands.OnGun: missing visual animator";

            Transform leftPalm = visual.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform rightPalm = visual.GetBoneTransform(HumanBodyBones.RightHand);
            if (leftPalm == null || rightPalm == null)
                return "FAIL Hands.OnGun: missing palm transform";

            Transform handguard = FindChildNamed(gun.transform, "Handguard");
            Transform grip = FindChildNamed(gun.transform, "Grip");
            float left, right;
            if (handguard != null && grip != null)
            {
                left = Vector3.Distance(leftPalm.position, handguard.position);
                right = Vector3.Distance(rightPalm.position, grip.position);
            }
            else
            {
                // Measure distance from visual palms to closest point on gun bounds (works for all 5 gun prefabs)
                Bounds b = new Bounds(gun.transform.position, Vector3.zero);
                bool hasB = false;
                foreach (var r in gun.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
                    if (!hasB) { b = r.bounds; hasB = true; }
                    else b.Encapsulate(r.bounds);
                }
                if (!hasB) b = new Bounds(gun.transform.position, Vector3.one * 0.1f);
                left = Vector3.Distance(leftPalm.position, b.ClosestPoint(leftPalm.position));
                right = Vector3.Distance(rightPalm.position, b.ClosestPoint(rightPalm.position));
            }
            bool pass = left < MaxHandError && right < MaxHandError;
            return $"{(pass ? "PASS" : "FAIL")} Hands.OnGun: visual leftPalm={left:0.000}m rightPalm={right:0.000}m max={MaxHandError:0.000}m";
        }

        public static string CheckDeathAimReset(SoldierController soldier)
        {
            if (soldier == null)
                return "FAIL Death.AimReset: missing soldier";

            bool hasInput = soldier.UserInput != null;
            bool inputAiming = hasInput && soldier.UserInput.GetValue<bool>(FPSANames.IsAiming);
            bool hasRecoil = soldier.RecoilAnimation != null;
            bool recoilAiming = hasRecoil && soldier.RecoilAnimation.isAiming;
            return CheckDeathAimResetState(soldier.IsDead, soldier.IsAiming, soldier.AimBlend,
                hasInput, inputAiming, hasRecoil, recoilAiming);
        }

        public static string CheckDeathAimResetState(bool isDead, bool controllerAiming, float aimBlend,
            bool hasInput, bool inputAiming, bool hasRecoil, bool recoilAiming)
        {
            bool pass = isDead && !controllerAiming && aimBlend >= 0f && aimBlend <= MaxDeathAimBlend &&
                        hasInput && !inputAiming && hasRecoil && !recoilAiming;
            return $"{(pass ? "PASS" : "FAIL")} Death.AimReset: dead={isDead} controller={controllerAiming} blend={aimBlend:0.000} input={inputAiming} recoil={recoilAiming} components={hasInput}/{hasRecoil} maxBlend={MaxDeathAimBlend:0.000}";
        }

        public static string CheckCloseWeaponSafety(Vector3 weaponPosition, Vector3 eyePosition,
            Vector3 aimForward, int collisionLayerMask)
        {
            if (aimForward.sqrMagnitude <= 1e-6f)
                return "FAIL Death.CloseWeaponSafety: zero aim direction";

            float forwardDot = Vector3.Dot(weaponPosition - eyePosition, aimForward.normalized);
            int characterBits = LR2Layers.ToMask(LR2Layers.Character, LR2Layers.Hitbox, LR2Layers.Ragdoll);
            int forbiddenBits = collisionLayerMask & characterBits;
            bool pass = forwardDot >= MinWeaponForwardDot && forbiddenBits == 0;
            return $"{(pass ? "PASS" : "FAIL")} Death.CloseWeaponSafety: eyePlane={forwardDot:0.000}m min={MinWeaponForwardDot:0.000}m forbiddenMask=0x{forbiddenBits:X}";
        }

        public static string CheckMusicSource(AudioSource source)
        {
            if (source == null)
                return "FAIL Music.SourcePriority: missing source";

            bool safe = source.priority <= MaxMusicPriority && source.bypassEffects &&
                        source.bypassListenerEffects && source.bypassReverbZones;
            return $"{(safe ? "PASS" : "FAIL")} Music.SourcePriority: name={source.name} priority={source.priority} max={MaxMusicPriority} bypass={source.bypassEffects}/{source.bypassListenerEffects}/{source.bypassReverbZones}";
        }

        public static string CheckMusicSources(AudioSource[] sources)
        {
            if (sources == null || sources.Length == 0)
                return "FAIL Music.SourcePriority: no music sources";

            int failures = 0;
            int worstPriority = 0;
            foreach (var source in sources)
            {
                if (source == null)
                {
                    failures++;
                    continue;
                }
                worstPriority = Mathf.Max(worstPriority, source.priority);
                if (source.priority > MaxMusicPriority || !source.bypassEffects ||
                    !source.bypassListenerEffects || !source.bypassReverbZones)
                    failures++;
            }
            return $"{(failures == 0 ? "PASS" : "FAIL")} Music.SourcePriority: sources={sources.Length} failures={failures} worst={worstPriority} max={MaxMusicPriority}";
        }

        public static string CheckMusicGainDuringGunfire(float calmGain, float gunfireGain)
        {
            bool pass = calmGain >= 0f && gunfireGain > calmGain;
            return $"{(pass ? "PASS" : "FAIL")} Music.GunfireGain: calm={calmGain:0.000} gunfire={gunfireGain:0.000} required=gunfire>calm";
        }

        public static string CheckMotionFootSlide(float plantedFootSpeed)
        {
            bool pass = plantedFootSpeed >= 0f && plantedFootSpeed < MaxMotionFootSlideSpeed;
            return $"{(pass ? "PASS" : "FAIL")} Motion.FootSlide: plantedSpeed={plantedFootSpeed:0.000}m/s max={MaxMotionFootSlideSpeed:0.000}m/s";
        }

        public static string[] CheckB57KnownBadFixtures()
        {
            string deathBad = CheckDeathAimResetState(true, true, 1f, true, true, true, true);
            string deathGood = CheckDeathAimResetState(true, false, 0f, true, false, true, false);
            string weaponBad = CheckCloseWeaponSafety(Vector3.back * 0.20f, Vector3.zero,
                Vector3.forward, LR2Layers.EnvironmentMask | (1 << LR2Layers.Character));
            string weaponGood = CheckCloseWeaponSafety(Vector3.forward * 0.05f, Vector3.zero,
                Vector3.forward, LR2Layers.EnvironmentMask);
            string gainBad = CheckMusicGainDuringGunfire(0.30f, 0.20f);
            string gainGood = CheckMusicGainDuringGunfire(0.20f, 0.30f);
            string slideBad = CheckMotionFootSlide(1.50f);
            string slideGood = CheckMotionFootSlide(0.10f);

            return new[]
            {
                $"{(deathBad.StartsWith("FAIL", StringComparison.Ordinal) && deathGood.StartsWith("PASS", StringComparison.Ordinal) ? "PASS" : "FAIL")} QA.KnownBad.DeathAimReset: bad={deathBad.StartsWith("FAIL", StringComparison.Ordinal)} good={deathGood.StartsWith("PASS", StringComparison.Ordinal)}",
                $"{(weaponBad.StartsWith("FAIL", StringComparison.Ordinal) && weaponGood.StartsWith("PASS", StringComparison.Ordinal) ? "PASS" : "FAIL")} QA.KnownBad.CloseWeaponSafety: bad={weaponBad.StartsWith("FAIL", StringComparison.Ordinal)} good={weaponGood.StartsWith("PASS", StringComparison.Ordinal)}",
                $"{(gainBad.StartsWith("FAIL", StringComparison.Ordinal) && gainGood.StartsWith("PASS", StringComparison.Ordinal) ? "PASS" : "FAIL")} QA.KnownBad.MusicGain: bad={gainBad.StartsWith("FAIL", StringComparison.Ordinal)} good={gainGood.StartsWith("PASS", StringComparison.Ordinal)}",
                $"{(slideBad.StartsWith("FAIL", StringComparison.Ordinal) && slideGood.StartsWith("PASS", StringComparison.Ordinal) ? "PASS" : "FAIL")} QA.KnownBad.FootSlide: bad={slideBad.StartsWith("FAIL", StringComparison.Ordinal)} good={slideGood.StartsWith("PASS", StringComparison.Ordinal)}"
            };
        }

        private static Transform FindChildNamed(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name.Contains(name)) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindChildNamed(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        public static string CheckCameraShake(float traumaBeforeFire, float traumaAfterFire, float traumaAfterRecovery, float recoverySeconds)
        {
            bool rises = traumaAfterFire > traumaBeforeFire;
            bool recovers = traumaAfterRecovery <= 0.001f && recoverySeconds <= 1.5f;
            return $"{(rises && recovers ? "PASS" : "FAIL")} CameraShake.Response: before={traumaBeforeFire:0.000} after={traumaAfterFire:0.000} recovered={traumaAfterRecovery:0.000} recovery={recoverySeconds:0.00}s";
        }

        public static string CheckHorizontalFov()
        {
            float standard = LR2Settings.VerticalFov(90f, 16f / 9f);
            float ultrawide = LR2Settings.VerticalFov(90f, 21f / 9f);
            bool pass = Mathf.Abs(standard - ultrawide) <= 0.001f;
            return $"{(pass ? "PASS" : "FAIL")} Camera.HorizontalFov: 16:9={standard:0.000} 21:9={ultrawide:0.000}";
        }

        public static bool TryGetCameraShakeTrauma(out float trauma)
        {
            trauma = CameraShake.Instance != null ? CameraShake.Instance.Trauma : 0f;
            return CameraShake.Instance != null;
        }

        public static string CheckMotionPoseJump(float poseJumpDegrees, float maxPoseJumpDegrees)
        {
            bool pass = poseJumpDegrees >= 0f && poseJumpDegrees <= maxPoseJumpDegrees;
            return $"{(pass ? "PASS" : "FAIL")} Motion.PoseJump: jump={poseJumpDegrees:0.00}deg max={maxPoseJumpDegrees:0.00}deg";
        }

        public static string CheckMotionTrajectoryMatch(float currentVelocityCost, float futurePositionCost, float futureFacingCost, float maxCost)
        {
            bool pass = currentVelocityCost <= maxCost && futurePositionCost <= maxCost && futureFacingCost <= maxCost;
            return $"{(pass ? "PASS" : "FAIL")} Motion.TrajectoryMatch: velocity={currentVelocityCost:0.000} futurePosition={futurePositionCost:0.000} futureFacing={futureFacingCost:0.000} max={maxCost:0.000}";
        }

        public static string CheckMotionSelectionStability(int clipChanges, float seconds, int maxChangesPerTenSeconds)
        {
            float normalized = seconds > 0f ? clipChanges * 10f / seconds : float.PositiveInfinity;
            bool pass = normalized <= maxChangesPerTenSeconds;
            return $"{(pass ? "PASS" : "FAIL")} Motion.SelectionStability: changes={clipChanges}/{seconds:0.00}s normalized={normalized:0.0}/10s max={maxChangesPerTenSeconds}/10s";
        }

        public static string CheckMotionQueryBudget(float averageMilliseconds, float p99Milliseconds)
        {
            bool pass = averageMilliseconds >= 0f && p99Milliseconds >= 0f &&
                        averageMilliseconds <= 0.10f && p99Milliseconds <= 0.25f;
            return $"{(pass ? "PASS" : "FAIL")} Motion.QueryBudget: average={averageMilliseconds:0.000}ms p99={p99Milliseconds:0.000}ms limits=0.100/0.250ms";
        }

        public static string[] CheckMotionMatchingKnownBadFixtures()
        {
            return new[]
            {
                CheckMotionPoseJump(18f, 8f),
                CheckMotionTrajectoryMatch(0.40f, 0.80f, 0.20f, 0.50f),
                CheckMotionSelectionStability(14, 10f, 10),
                CheckMotionQueryBudget(0.14f, 0.31f)
            };
        }
    }
}
