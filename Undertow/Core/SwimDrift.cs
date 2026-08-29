using System;

namespace RavenIron.Undertow.Core
{
    /// <summary>
    /// What the current does to a swimmer. Pure arithmetic â€” no Unity, no game types â€” so the
    /// harness tests the shipping source.
    ///
    /// A SWIMMER IS NOT A BOAT, AND NEITHER OF THE OBVIOUS MECHANISMS WORKS.
    ///
    /// `AddForce` is erased. `Character.UpdateSwimming` servos the body every frame with
    /// `force = m_currentVel - m_body.linearVelocity` applied as a VelocityChange, so any
    /// external velocity is corrected away on the next tick.
    ///
    /// `AddPushbackForce` â€” vanilla's own "fold an external push into the target" helper, and
    /// the mechanism this file's own backlog entry originally prescribed â€” is WORSE. Read it:
    /// it ignores the magnitude of `m_pushForce` completely and drives velocity to a flat 20 m/s
    /// along its direction (halved to 10 while swimming). It exists to shove a body out of a
    /// creature it is clipping through. Routing a 0.3 m/s current through it would launch a
    /// swimmer at five times swim speed. Verified by decompile 2026-08-28, after the plan said
    /// otherwise.
    ///
    /// So the current is added to `m_currentVel` directly â€” but SCALED BY THE SWIM ACCELERATION,
    /// which is the part that is easy to get wrong by a factor of twenty.
    ///
    /// Vanilla lerps the target toward the swimmer's intent every frame:
    ///     m_currentVel = Lerp(m_currentVel, moveDir * speed, m_swimAcceleration)
    /// Adding `d` each frame after that reaches a steady state where the addition is AMPLIFIED
    /// by the reciprocal of the lerp factor:
    ///     v* = target + d / m_swimAcceleration
    /// At vanilla's `m_swimAcceleration = 0.05` a naive `d = current` would settle at TWENTY
    /// TIMES the current. Multiplying by the acceleration first cancels it exactly, leaving
    /// `v* = target + current` â€” the swimmer's own intent plus the water, which is what a
    /// current is.
    /// </summary>
    public static class SwimDrift
    {
        /// <summary>
        /// Per-frame addition to <c>m_currentVel</c>, already scaled so the steady state equals
        /// the intended drift.
        /// </summary>
        /// <param name="driftFactor">Share of the water's speed a swimmer feels, 0..1.</param>
        /// <param name="swimSpeed">The character's own swim speed (vanilla: 2 m/s).</param>
        /// <param name="maxShareOfSwimSpeed">
        /// THE DROWNING GUARD. Hard ceiling on drift as a share of the swimmer's own speed, so a
        /// swimmer can always out-swim the water and reach shore. If a player can be held
        /// offshore until they drown, the feature is wrong â€” not the tuning â€” so this cap is not
        /// a balance dial, it is a safety property, and the harness sweeps the whole config range
        /// to prove it holds.
        /// </param>
        /// <param name="swimAcceleration">Vanilla's lerp factor toward the swimmer's intent.</param>
        public static void Compute(
            float waterX, float waterZ,
            float driftFactor, float swimSpeed, float maxShareOfSwimSpeed,
            float swimAcceleration,
            out float dvx, out float dvz)
        {
            dvx = 0f;
            dvz = 0f;

            if (driftFactor <= 0f || swimAcceleration <= 0f) return;

            float desiredX = waterX * driftFactor;
            float desiredZ = waterZ * driftFactor;

            float speed = (float)Math.Sqrt(desiredX * desiredX + desiredZ * desiredZ);
            if (speed <= 1e-5f) return;

            // The cap, applied to the DRIFT and not to the per-frame delta, so it means what it
            // says regardless of how vanilla tunes its acceleration.
            float ceiling = swimSpeed * Clamp01(maxShareOfSwimSpeed);
            if (ceiling <= 0f) return;

            if (speed > ceiling)
            {
                float scale = ceiling / speed;
                desiredX *= scale;
                desiredZ *= scale;
            }

            // Cancel the 1/acceleration amplification of the lerp. See the class summary.
            dvx = desiredX * swimAcceleration;
            dvz = desiredZ * swimAcceleration;
        }

        /// <summary>
        /// Where a swimmer's drift settles, given a per-frame addition. The inverse of the
        /// scaling above, and the thing the harness checks rather than trusting the algebra.
        /// </summary>
        public static float SteadyStateDrift(float perFrameDelta, float swimAcceleration)
        {
            if (swimAcceleration <= 0f) return 0f;
            return perFrameDelta / swimAcceleration;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
