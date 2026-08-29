namespace RavenIron.Undertow.Core
{
    /// <summary>
    /// What the current does to a floating body. Pure arithmetic, no Unity, no game types, so
    /// the harness tests the shipping source.
    ///
    /// THE MODEL IS ENTRAINMENT, NOT DRAG, AND THE DIFFERENCE IS THE WHOLE FILE.
    ///
    /// The obvious model is drag toward the water: force proportional to (water - hull), so a
    /// boat asymptotically takes up the speed of the water it sits in. It is wrong here, and
    /// wrong in a way that only shows up under sail. Vanilla ALREADY models hull-water
    /// resistance — `m_damping`, `m_dampingForward`, `m_dampingSideway` in the method this
    /// feeds — computed against the hull's absolute velocity, i.e. assuming the water is still.
    /// Adding a second drag term against the hull's full velocity double-counts it. A karve
    /// making 6 m/s through water moving at 0.3 would receive
    /// `0.6 * (0.3 - 6) = -3.4 m/s^2` — the sea as a brake on every boat under way, which is
    /// both unphysical and would read to a player as the mod breaking sailing.
    ///
    /// So the current is applied as a PUSH along the water's own direction, saturating as the
    /// hull takes up the water's speed. It carries a boat and never fights the sail.
    ///
    /// WHY SATURATION RATHER THAN A CALIBRATED PUSH — measured on a live server 2026-08-28, and
    /// this killed two earlier models in one reading. The first attempt matched a push against
    /// vanilla's quadratic damping so the two would balance at the water's speed. That requires
    /// knowing the hull's damping coefficient, and `m_dampingForward` is a SERIALIZED UNITY
    /// FIELD — every boat prefab overrides it. The class default read off the decompile (0.01)
    /// applies to no actual boat: a VikingShip measured ~0.0053 effective, and settled at 1.38x
    /// the water's speed instead of 1.0. A raft, a karve and a longship would each land on a
    /// different multiple, so NO single constant can be right.
    ///
    /// Fading the push out as the hull approaches the water's speed sets the equilibrium
    /// directly, by construction, without knowing anything about how the hull damps. Every boat
    /// type converges on the same answer, and the number `wake here` prints is the speed a
    /// sailor will actually drift, on any hull.
    ///
    /// The hull's velocity is a parameter again — but only its component ALONG the current, and
    /// only through a term clamped to [0,1]. That clamp is the anti-braking guarantee that the
    /// earlier signature enforced by omission: the result can never oppose the current, so a
    /// boat under sail is never slowed, it merely stops being helped.
    ///
    ///
    /// The result is a VELOCITY CHANGE per tick — an impulse per unit mass. The caller
    /// multiplies by the body's mass and hands it to ForceMode.Impulse, which is the convention
    /// vanilla itself uses in the method this patches.
    /// </summary>
    public static class DriftForce
    {
        /// <summary>
        /// Where the current starts fading out near the world edge.
        ///
        /// Vanilla's own <c>Ship.ApplyEdgeForce</c> pushes hulls back inland from 10420m, ramping
        /// to 10500m. Undertow is fully faded by 10420 so the two never argue: a mod current
        /// fighting the game's own boundary force is a boat juddering on the rim of the world,
        /// and whichever won would look like a bug.
        /// </summary>
        public const float EdgeFadeStart = 10200f;
        public const float EdgeFadeEnd = 10420f;

        /// <summary>1 well inside the world, 0 at and beyond the boundary force's start.</summary>
        public static float EdgeFade(float distanceFromCentre)
        {
            if (distanceFromCentre <= EdgeFadeStart) return 1f;
            if (distanceFromCentre >= EdgeFadeEnd) return 0f;
            return 1f - (distanceFromCentre - EdgeFadeStart) / (EdgeFadeEnd - EdgeFadeStart);
        }

        /// <summary>
        /// Velocity change to apply this tick, horizontal only.
        /// </summary>
        /// <param name="hullAlongCurrent">
        /// The hull's velocity component ALONG the current, m/s. Used only to fade the push out
        /// as the hull takes up the water's speed, through a term clamped to [0,1] - so it can
        /// never turn the push into a brake. Negative when the hull drives upstream.
        /// </param>
        /// <param name="strength">
        /// How hard the water grips, per second. Sets how QUICKLY a hull takes up the water's
        /// speed; WHERE it settles is set by the saturation term, not by this.
        /// </param>
        /// <param name="crewFactor">
        /// Scales the whole effect. 1 for a crewed boat; the configured unattended fraction,
        /// DEFAULT ZERO, for an empty one — vanilla already damps an unmanned hull's horizontal
        /// velocity to a tenth per tick and forces it to Stop, and losing a moored longship to a
        /// mod is a one-star review.
        /// </param>
        public static void Compute(
            float waterX, float waterZ,
            float hullAlongCurrent,
            float strength, float dt,
            float crewFactor, float edgeFade,
            out float dvx, out float dvz)
        {
            dvx = 0f;
            dvz = 0f;

            float waterSpeed = (float)System.Math.Sqrt(waterX * waterX + waterZ * waterZ);
            if (waterSpeed <= 1e-5f) return;

            // SATURATION, and it is what makes the mod hull-independent.
            //
            // Full push at rest, fading to nothing as the hull's speed ALONG THE CURRENT reaches
            // the water's own. The equilibrium is therefore set by this term rather than by a
            // race between our push and vanilla's damping, which is the only way to get every
            // hull to the same answer — see the class summary for why calibrating against
            // damping cannot work.
            //
            // CLAMPED TO [0,1], which is the anti-braking guarantee. The result can never be
            // negative, so this can never oppose the current and never slows a boat under sail;
            // at worst it stops helping. A hull moving AGAINST the current gets a value above 1
            // before clamping and is capped at a full push rather than an amplified one.
            float head = 1f - (hullAlongCurrent / waterSpeed);
            if (head > 1f) head = 1f;
            else if (head < 0f) head = 0f;

            float k = strength * dt * head;

            // NEVER HAND OVER MORE THAN THE WATER'S OWN SPEED IN ONE TICK. Without this, a long
            // frame — a lag spike, a loading hitch, a breakpoint — multiplies coupling by a large
            // dt and delivers a velocity change many times the current itself, launching the
            // hull. Clamping before the other factors keeps one tick bounded by the thing it is
            // modelling.
            if (k > 1f) k = 1f;
            else if (k < 0f) k = 0f;

            k *= crewFactor * edgeFade;

            dvx = waterX * k;
            dvz = waterZ * k;
        }
    }
}
