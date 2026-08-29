using System;
using HarmonyLib;
using UnityEngine;
using RavenIron.Undertow.Config;
using RavenIron.Undertow.Core;

namespace RavenIron.Undertow.Patches
{
    /// <summary>
    /// The current carries a swimmer, gently.
    ///
    /// POSTFIX, NOT PREFIX, and not by preference. `m_currentVel` is REASSIGNED near the top of
    /// `UpdateSwimming` by a lerp toward the swimmer's intent, so anything a prefix wrote would
    /// be erased before the servo ever saw it. Running after means the addition lands on the
    /// value the NEXT frame's lerp starts from, which is exactly the channel that needs feeding.
    /// The one-frame lag is imperceptible at 50Hz.
    ///
    /// PLAYERS ONLY, deliberately. Vanilla lerps a creature's swim velocity with a factor of 0.5
    /// rather than `m_swimAcceleration`, so the scaling below would be wrong for them — and
    /// dragging swimming creatures around is a change to AI pathing nobody asked this mod for.
    ///
    /// OWNER-GATED, the same lesson as the ship postfix. A player's replica exists on every peer;
    /// only the machine that owns the character may drive it, or the drift is applied as many
    /// times as there are people online.
    ///
    /// `m_swimSpeed`, `m_swimAcceleration` and `IsPlayer()` are genuinely public in the shipping
    /// assembly; `m_currentVel`, `m_nview` and `UpdateSwimming` are not, so those come from
    /// Harmony's field injection rather than from the publicized reference. House rule 5.
    /// </summary>
    [HarmonyPatch(typeof(Character), "UpdateSwimming")]
    public static class Patch_Character_Swim
    {
        public static long PushCount;
        public static float LastWaterSpeed;
        public static float LastDrift;
        public static bool EverRan;

        private const float LogIntervalSeconds = 3f;
        private static float _nextLogTime;

        private static void Postfix(
            Character __instance,
            ZNetView ___m_nview,
            ref Vector3 ___m_currentVel)
        {
            try
            {
                if (!ModConfig.EnableSwimmers.Value) return;
                if (__instance == null || !__instance.IsPlayer()) return;
                if (___m_nview == null || !___m_nview.IsValid() || !___m_nview.IsOwner()) return;

                Vector3 position = __instance.transform.position;
                if (!SeaContext.TryEvaluate(position.x, position.z, out FieldSample sample)) return;
                if (sample.Speed <= 0f) return;

                float edgeFade = DriftForce.EdgeFade(
                    Mathf.Sqrt(position.x * position.x + position.z * position.z));
                if (edgeFade <= 0f) return;

                SwimDrift.Compute(
                    sample.X * edgeFade, sample.Z * edgeFade,
                    ModConfig.SwimmerDriftFactor.Value,
                    __instance.m_swimSpeed,
                    ModConfig.SwimmerMaxShareOfSwimSpeed.Value,
                    __instance.m_swimAcceleration,
                    out float dvx, out float dvz);

                if (dvx == 0f && dvz == 0f) return;

                ___m_currentVel.x += dvx;
                ___m_currentVel.z += dvz;

                EverRan = true;
                PushCount++;
                LastWaterSpeed = sample.Speed;
                LastDrift = SwimDrift.SteadyStateDrift(
                    Mathf.Sqrt(dvx * dvx + dvz * dvz), __instance.m_swimAcceleration);

                if (ModConfig.VerboseLogging.Value && Time.realtimeSinceStartup >= _nextLogTime)
                {
                    _nextLogTime = Time.realtimeSinceStartup + LogIntervalSeconds;
                    Vector3 v = __instance.GetVelocity();
                    float actual = Mathf.Sqrt(v.x * v.x + v.z * v.z);
                    Undertow.Log.LogInfo(
                        $"swim drift @ ({position.x:0},{position.z:0}) | water {sample.Speed:0.###} " +
                        $"drift {LastDrift:0.###} (cap {__instance.m_swimSpeed * ModConfig.SwimmerMaxShareOfSwimSpeed.Value:0.###}) " +
                        $"| swimmer {actual:0.###} m/s, swimSpeed {__instance.m_swimSpeed:0.##}");
                }
            }
            catch (Exception ex)
            {
                // This runs inside every swimming character's motion update. Never let it throw
                // into vanilla's physics.
                Undertow.Log.LogWarning($"swim postfix: {ex.Message}");
            }
        }
    }
}
