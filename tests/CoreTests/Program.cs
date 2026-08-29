using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Configuration;
using RavenIron.Undertow.Config;
using RavenIron.Undertow.Core;

namespace Undertow.Tests
{
    /// <summary>
    /// Off-game harness for the pure-logic core. No test framework by design — a console
    /// program returning a nonzero exit code is enough, and adds no dependency to keep current.
    ///
    /// At 0.1.0 there is exactly one shipping file with logic in it, and the tests below are
    /// not filler: both failures they catch are silent in-game and arbitrarily delayed. A field
    /// declared but never bound is a NullReferenceException at whatever future moment something
    /// first reads it, and a duplicated section/key pair makes two config entries share one
    /// value with no error anywhere. Neither shows up in a build.
    ///
    /// This grows into the real harness at task 1, when CurrentField arrives — determinism,
    /// seasonal reversal, tide phase and magnitude bounds are all testable here without
    /// launching Valheim, and all four fail silently if they are wrong.
    /// </summary>
    public static class Program
    {
        private static int _passed;
        private static int _failed;

        public static int Main()
        {
            Console.WriteLine("Undertow — core tests\n");

            ModConfigTests();
            CurrentFieldTests();
            DriftForceTests();
            FlotsamMathTests();
            SwimDriftTests();

            Console.WriteLine($"\n{_passed} passed, {_failed} failed.");
            return _failed == 0 ? 0 : 1;
        }

        // ---- terrain fixtures ---------------------------------------------------------------

        /// <summary>Featureless deep ocean. Isolates the open-water stream function.</summary>
        private sealed class FlatSeabed : ITerrainProbe
        {
            private readonly float _height;
            public FlatSeabed(float height) { _height = height; }
            public float HeightAt(float x, float z) => _height;
        }

        /// <summary>
        /// A cone island at the origin: seabed rises linearly toward the middle and breaks the
        /// surface inside `shoreRadius`. Enough to exercise the shore gradient, the shelf ramp
        /// and the waterline fade with arithmetic a reader can check by hand.
        /// </summary>
        private sealed class ConeIsland : ITerrainProbe
        {
            private readonly float _shoreRadius, _slope, _deepHeight;
            public ConeIsland(float shoreRadius, float slope, float deepHeight)
            { _shoreRadius = shoreRadius; _slope = slope; _deepHeight = deepHeight; }

            public float HeightAt(float x, float z)
            {
                double r = Math.Sqrt((double)x * x + (double)z * z);
                float h = 30f + (float)((_shoreRadius - r) * _slope);
                return h < _deepHeight ? _deepHeight : h;
            }
        }

        /// <summary>Two banks either side of a north-south channel down x = 0.</summary>
        private sealed class Channel : ITerrainProbe
        {
            private readonly float _halfWidth;
            public Channel(float halfWidth) { _halfWidth = halfWidth; }
            public float HeightAt(float x, float z)
            {
                float over = Math.Abs(x) - _halfWidth;
                if (over <= 0f) return -40f;          // deep in the channel
                return -40f + over * 0.5f;            // banks rising away from it
            }
        }

        private static void CurrentFieldTests()
        {
            Section("CurrentField");

            FieldSettings s = FieldSettings.Defaults;
            var deep = new FlatSeabed(-60f);
            const int seed = 1337;

            // ---- determinism: the entire no-sync architecture rests on this ----------------
            // If two machines can disagree about the current at a point, every boat desyncs and
            // no amount of tuning saves it.
            FieldSample a = CurrentField.Evaluate(1500f, -800f, seed, 12345.0, 1, 1f, deep, s);
            FieldSample b = CurrentField.Evaluate(1500f, -800f, seed, 12345.0, 1, 1f, deep, s);
            Check(a.X == b.X && a.Z == b.Z, "same inputs give bit-identical output");

            FieldSample other = CurrentField.Evaluate(1500f, -800f, seed + 1, 12345.0, 1, 1f, deep, s);
            Check(other.X != a.X || other.Z != a.Z, "a different seed gives a different field");

            // ---- magnitude bounds ----------------------------------------------------------
            // Swept rather than spot-checked: a ceiling that holds at one point and not at
            // another is the failure mode that reaches a player as a boat flung across the map.
            float peak = 0f;
            bool everNegative = false;
            for (int ix = -20; ix <= 20; ix++)
            for (int iz = -20; iz <= 20; iz++)
            {
                FieldSample p = CurrentField.Evaluate(ix * 350f, iz * 350f, seed, 500.0, 2, 1f, deep, s);
                if (p.Speed > peak) peak = p.Speed;
                if (p.Speed < 0f) everNegative = true;
                float measured = (float)Math.Sqrt(p.X * p.X + p.Z * p.Z);
                if (Math.Abs(measured - p.Speed) > 0.002f) everNegative = true; // reuse as a fail flag
            }
            Check(peak <= s.MaxSpeed + 1e-4f, $"speed never exceeds MaxSpeed over 1681 points (peak {Fmt(peak)})");
            Check(!everNegative, "reported Speed always matches the vector magnitude, and is never negative");
            Check(peak > 0.05f, $"the open ocean is not dead calm everywhere (peak {Fmt(peak)})");

            // The sweep above does NOT exercise the ceiling: at default settings the natural
            // magnitude never reaches 1.2, so it passed with the clamp deleted. Measured, not
            // assumed — 2026-08-28. Re-run it against a ceiling low enough that the clamp MUST
            // engage, on terrain that also fires the coastal and race multipliers.
            FieldSettings tight = s;
            tight.MaxSpeed = 0.25f;
            var shelfIsland = new ConeIsland(shoreRadius: 300f, slope: 0.5f, deepHeight: -60f);
            float tightPeak = 0f;
            bool clampEngaged = false;
            for (int ix = -12; ix <= 12; ix++)
            for (int iz = -12; iz <= 12; iz++)
            {
                FieldSample p = CurrentField.Evaluate(ix * 90f, iz * 90f, seed, 700.0, 3, 1f, shelfIsland, tight);
                if (p.Speed > tightPeak) tightPeak = p.Speed;
                if (p.Speed > tight.MaxSpeed - 1e-4f) clampEngaged = true;
            }
            Check(clampEngaged, "the low-ceiling sweep actually reaches the ceiling (otherwise it proves nothing)");
            Check(tightPeak <= tight.MaxSpeed + 1e-4f,
                $"the ceiling holds where it is actually binding (peak {Fmt(tightPeak)} vs cap {Fmt(tight.MaxSpeed)})");

            // ---- land and the waterline ----------------------------------------------------
            var island = new ConeIsland(shoreRadius: 300f, slope: 0.5f, deepHeight: -60f);

            FieldSample onLand = CurrentField.Evaluate(0f, 0f, seed, 500.0, 0, 1f, island, s);
            Check(onLand.Speed == 0f && onLand.Dominant == CurrentTerm.Land, "no current on dry land");

            // Just outside the shoreline, inside the fade band: must be calm, so nothing is ever
            // shoved aground by the mod.
            FieldSample atWaterline = CurrentField.Evaluate(306f, 0f, seed, 500.0, 0, 1f, island, s);
            FieldSample offshore = CurrentField.Evaluate(600f, 0f, seed, 500.0, 0, 1f, island, s);
            Check(atWaterline.Depth > 0f && atWaterline.Depth < s.ShallowFadeDepth,
                $"the sample at the waterline is in the fade band (depth {Fmt(atWaterline.Depth)}m)");
            Check(atWaterline.Speed < offshore.Speed,
                "current fades toward the waterline rather than pushing hulls ashore");

            // ---- the shelf ramp reaches zero before the seabed does -------------------------
            // Pins the constant against the measured fact that Valheim's open ocean is a flat
            // floor at exactly 30m depth. If ShelfDepth ever creeps back above that, the coastal
            // term silently switches itself on across the entire sea.
            Check(s.ShelfDepth < 30f,
                $"ShelfDepth ({Fmt(s.ShelfDepth)}) is shallower than Valheim's 30m ocean floor");

            // A sloped seabed at open-ocean depth must produce no coastal steering at all.
            var deepSlope = new ConeIsland(shoreRadius: 300f, slope: 0.001f, deepHeight: -60f);
            FieldSample deepGradient = CurrentField.Evaluate(2000f, 0f, seed, 500.0, 0, 1f, deepSlope, s);
            Check(deepGradient.Dominant != CurrentTerm.Coastal,
                "a gradient in deep water does not read as a coast");

            // ---- the tide reverses the coastal stream --------------------------------------
            // Sampled on the shelf, at peak flood and peak ebb. This is the reversal a player
            // actually feels, and the reason a passage is not the same passage six hours later.
            // Isolated by SUBTRACTION rather than by hoping it dominates: at slack the tide term
            // is exactly zero, so the slack sample is pure open-ocean drift and everything left
            // after subtracting it is the coastal stream. Comparing absolute directions instead
            // made this test a hostage to the relative size of two unrelated constants — it
            // broke the moment ShelfDepth was corrected, while the code was right.
            FieldSettings coastalOnly = s;
            coastalOnly.TideAmplitude = 0f;   // hold the open-ocean term still across the cycle
            coastalOnly.MaxSpeed = 5f;        // lift the ceiling so no clamp confounds the sum
            float flood = 0.25f * s.TidePeriodSeconds;
            float ebb = 0.75f * s.TidePeriodSeconds;

            FieldSample atSlack = CurrentField.Evaluate(320f, 0f, seed, 0.0, 0, 1f, island, coastalOnly);
            FieldSample atFlood = CurrentField.Evaluate(320f, 0f, seed, flood, 0, 1f, island, coastalOnly);
            FieldSample atEbb = CurrentField.Evaluate(320f, 0f, seed, ebb, 0, 1f, island, coastalOnly);

            float fx = atFlood.X - atSlack.X, fz = atFlood.Z - atSlack.Z;
            float ex = atEbb.X - atSlack.X, ez = atEbb.Z - atSlack.Z;
            float dot = fx * ex + fz * ez;
            float coastalMag = (float)Math.Sqrt(fx * fx + fz * fz);

            Check(coastalMag > 0.01f, $"there is a coastal stream to reverse at all ({Fmt(coastalMag)} m/s)");
            Check(dot < 0f, $"the coastal stream reverses between flood and ebb (dot {Fmt(dot)})");

            Check(Math.Abs(CurrentField.TidePhase01(0.0, 3600f) - 0f) < 1e-5f, "tide phase starts at 0");
            Check(Math.Abs(CurrentField.TidePhase01(900.0, 3600f) - 0.25f) < 1e-5f, "tide phase is 0.25 a quarter through");
            Check(Math.Abs(CurrentField.TidePhase01(3600.0, 3600f) - 0f) < 1e-5f, "tide phase wraps at a full period");
            Check(Math.Abs(CurrentField.TidePhase01(7300.0, 3600f) - CurrentField.TidePhase01(100.0, 3600f)) < 1e-4f,
                "tide phase is periodic across many cycles");

            // ---- the tide swings open-ocean strength ---------------------------------------
            FieldSample deepFlood = CurrentField.Evaluate(1200f, 900f, seed, flood, 0, 1f, deep, s);
            FieldSample deepSlack = CurrentField.Evaluate(1200f, 900f, seed, 0.0, 0, 1f, deep, s);
            Check(deepFlood.Speed > deepSlack.Speed,
                $"open ocean runs harder at flood than at slack ({Fmt(deepFlood.Speed)} vs {Fmt(deepSlack.Speed)})");

            // ---- season shifts the field without erasing it --------------------------------
            // Deliberately NOT a reversal: a seasonal flip would invalidate every seamark a crew
            // had learned, four times a year, which destroys the thing the mod exists for.
            FieldSample spring = CurrentField.Evaluate(2000f, 1200f, seed, 500.0, 0, 1f, deep, s);
            FieldSample winter = CurrentField.Evaluate(2000f, 1200f, seed, 500.0, 3, 1f, deep, s);
            Check(spring.X != winter.X || spring.Z != winter.Z, "the season changes the field");

            float springSpeed = 0f, winterSpeed = 0f;
            for (int i = 0; i < 400; i++)
            {
                float px = (i % 20) * 500f - 5000f;
                float pz = (i / 20) * 500f - 5000f;
                springSpeed += CurrentField.Evaluate(px, pz, seed, 500.0, 0, 1f, deep, s).Speed;
                winterSpeed += CurrentField.Evaluate(px, pz, seed, 500.0, 3, 1f, deep, s).Speed;
            }
            Check(winterSpeed > springSpeed, "winter water runs harder than spring, world-wide");

            float angle = AngleBetween(spring, winter);
            Check(angle < 90f, $"the seasonal shift stays under a quarter turn ({Fmt(angle)} degrees)");

            // ---- a constriction accelerates the flow ---------------------------------------
            var openWater = new FlatSeabed(-40f);
            float inOpen = 0f;
            for (int i = 0; i < 40; i++)
                inOpen += CurrentField.Evaluate(0f, i * 200f, seed, 500.0, 0, 1f, openWater, s).Speed;

            // Both scales the probe looks at. The narrow one is a race between rocks; the wide
            // one is a strait between islands, and a single-scale probe was blind to it — the
            // failure that put two distances in RaceProbeDistances in the first place.
            float inNarrow = 0f, inWide = 0f;
            var narrow = new Channel(halfWidth: 40f);
            var wide = new Channel(halfWidth: 130f);
            for (int i = 0; i < 40; i++)
            {
                float pz = i * 200f;
                inNarrow += CurrentField.Evaluate(0f, pz, seed, 500.0, 0, 1f, narrow, s).Speed;
                inWide += CurrentField.Evaluate(0f, pz, seed, 500.0, 0, 1f, wide, s).Speed;
            }
            Check(inNarrow > inOpen,
                $"water runs faster in a narrow race than over the same open seabed ({Fmt(inNarrow)} vs {Fmt(inOpen)})");
            Check(inWide > inOpen,
                $"water runs faster in a wide strait too — the second probe scale earns its place ({Fmt(inWide)} vs {Fmt(inOpen)})");
            Check(inNarrow >= inWide,
                $"a tighter gap runs at least as fast as a broader one ({Fmt(inNarrow)} vs {Fmt(inWide)})");

            // ---- storm surge ---------------------------------------------------------------
            // Positional by construction: the CALLER decides where a storm is, so the field
            // cannot accidentally apply one globally. What is tested here is that the multiplier
            // does what it says and never breaches the world ceiling.
            FieldSample calm   = CurrentField.Evaluate(1200f, 900f, seed, 500.0, 0, 1f, deep, s);
            FieldSample stormy = CurrentField.Evaluate(1200f, 900f, seed, 500.0, 0, 1.6f, deep, s);
            Check(stormy.Speed > calm.Speed,
                $"a storm makes the water run harder ({Fmt(calm.Speed)} -> {Fmt(stormy.Speed)})");
            Check(Math.Abs(stormy.Speed - calm.Speed * 1.6f) < 1e-4f,
                "the surge multiplies the water speed exactly");
            Check(Math.Abs(stormy.StormSurge - 1.6f) < 1e-6f, "the sample reports the surge it applied");

            // MaxCurrentSpeed is documented as the ceiling ANYWHERE in the world. A storm must
            // drive weak water toward it, never through it, or that description becomes a lie.
            FieldSettings capped = s;
            capped.MaxSpeed = 0.2f;
            float stormPeak = 0f;
            for (int ix = -10; ix <= 10; ix++)
            for (int iz = -10; iz <= 10; iz++)
            {
                FieldSample p = CurrentField.Evaluate(ix * 300f, iz * 300f, seed, 500.0, 0, 4f, deep, capped);
                if (p.Speed > stormPeak) stormPeak = p.Speed;
            }
            Check(stormPeak <= capped.MaxSpeed + 1e-4f,
                $"even a 4x storm cannot breach MaxCurrentSpeed (peak {Fmt(stormPeak)} vs cap {Fmt(capped.MaxSpeed)})");
            Check(stormPeak > capped.MaxSpeed - 1e-4f,
                "the storm sweep actually reaches the ceiling, so the check above means something");

            // A storm over dry land is still no current at all.
            FieldSample stormOnLand = CurrentField.Evaluate(0f, 0f, seed, 500.0, 0, 4f, island, s);
            Check(stormOnLand.Speed == 0f, "a storm over dry land raises nothing");

            // ---- degenerate inputs ---------------------------------------------------------
            FieldSample noProbe = CurrentField.Evaluate(0f, 0f, seed, 500.0, 0, 1f, null, s);
            Check(noProbe.Speed == 0f, "a null probe answers dead calm rather than throwing");

            FieldSettings zeroTide = s;
            zeroTide.TidePeriodSeconds = 0f;
            FieldSample noTide = CurrentField.Evaluate(500f, 500f, seed, 500.0, 0, 1f, deep, zeroTide);
            Check(!float.IsNaN(noTide.Speed) && noTide.TidePhase01 == 0f,
                "a zero tide period does not divide by zero");

            FieldSettings zeroSpeed = s;
            zeroSpeed.MaxSpeed = 0f;
            FieldSample stopped = CurrentField.Evaluate(500f, 500f, seed, 500.0, 0, 1f, deep, zeroSpeed);
            Check(stopped.Speed == 0f, "MaxCurrentSpeed 0 turns the whole field off");

            for (int season = -8; season <= 8; season++)
            {
                FieldSample p = CurrentField.Evaluate(700f, -300f, seed, 500.0, season, 1f, deep, s);
                if (float.IsNaN(p.Speed)) { Check(false, $"season index {season} produced NaN"); break; }
            }
            Check(true, "out-of-range season indices wrap instead of throwing");
        }

        private static void DriftForceTests()
        {
            Section("DriftForce");

            const float dt = 0.02f;      // Valheim's fixed step
            const float coupling = 0.6f;

            // Still water pushes nothing at all.
            DriftForce.Compute(0f, 0f, 0f, coupling, dt, 1f, 1f, out float dvx, out float dvz);
            Check(dvx == 0f && dvz == 0f, "still water pushes nothing");

            // The push runs along the current, and is water^2 * calibration * strength * dt.
            DriftForce.Compute(1f, 0f, 0f, coupling, dt, 1f, 1f, out dvx, out dvz);
            Check(dvx > 0f && Math.Abs(dvz) < 1e-6f, "the push runs along the current");
            Check(Math.Abs(dvx - 1f * coupling * dt) < 1e-6f,
                $"a hull at rest gets the full push, water * strength * dt ({Fmt(dvx)})");

            // ---- SATURATION: the property that makes every hull agree ----------------------
            // Measured live 2026-08-28: a VikingShip under the previous calibrated-push model
            // sailed straight past the water's speed to 1.38x and was still climbing, because
            // m_dampingForward is a SERIALIZED field every prefab overrides — no constant can
            // balance a damping coefficient you do not know. Fading the push out as the hull
            // takes up the water's speed sets the equilibrium directly instead.
            DriftForce.Compute(1f, 0f, 1f, coupling, dt, 1f, 1f, out dvx, out dvz);
            Check(dvx == 0f && dvz == 0f, "a hull already at the water's speed gets no push at all");

            DriftForce.Compute(1f, 0f, 3f, coupling, dt, 1f, 1f, out dvx, out dvz);
            Check(dvx == 0f && dvz == 0f, "a hull outrunning the water gets no push — and is NOT braked");

            DriftForce.Compute(1f, 0f, 0.5f, coupling, dt, 1f, 1f, out float halfway, out _);
            DriftForce.Compute(1f, 0f, 0f, coupling, dt, 1f, 1f, out float atRest, out _);
            Check(Math.Abs(halfway - atRest * 0.5f) < 1e-6f,
                "the push fades linearly as the hull takes up the water's speed");

            // THE ANTI-BRAKING GUARANTEE, swept rather than spot-checked. The push may never
            // oppose the current at ANY hull speed, in either direction — that is the whole
            // reason the saturation term is clamped rather than merely subtracted.
            bool everOpposed = false;
            for (float hull = -8f; hull <= 8f; hull += 0.25f)
            {
                DriftForce.Compute(0.7f, 0f, hull, coupling, dt, 1f, 1f, out float px, out float pz);
                if (px < -1e-9f) everOpposed = true;              // current runs +x
                if (Math.Abs(pz) > 1e-9f) everOpposed = true;     // and only +x
            }
            Check(!everOpposed, "the push never opposes the current at any hull speed, forward or reverse");

            // A hull driving against the current is capped at a full push, never an amplified one.
            DriftForce.Compute(1f, 0f, -50f, coupling, dt, 1f, 1f, out float against, out _);
            Check(Math.Abs(against - atRest) < 1e-6f, "driving against the current gives a full push, not more");

            // Direction is preserved exactly — a current toward the south-west must not push a
            // hull anywhere but south-west.
            DriftForce.Compute(-0.6f, 0.8f, 0f, coupling, dt, 1f, 1f, out dvx, out dvz);
            Check(Math.Abs(dvx / dvz + 0.6f / 0.8f) < 1e-5f, "the push preserves the current's bearing");

            // At rest the push is linear in the water speed again — the saturation term, not the
            // push law, is what sets where a hull settles.
            DriftForce.Compute(0.5f, 0f, 0f, coupling, dt, 1f, 1f, out float slow, out _);
            DriftForce.Compute(1.0f, 0f, 0f, coupling, dt, 1f, 1f, out float fast, out _);
            Check(Math.Abs(fast - slow * 2f) < 1e-6f, "at rest the push is linear in water speed");

            // THE BUG THIS SIGNATURE EXISTS TO PREVENT: the current must never depend on how
            // fast the boat is going, because a drag term against the hull's own velocity
            // double-counts vanilla's damping and brakes every boat under sail. There is no hull
            // velocity to pass, so the only assertion available is that the push for a given
            // current is a constant — which is the property that matters.
            DriftForce.Compute(0.3f, 0f, 0f, coupling, dt, 1f, 1f, out float once, out _);
            DriftForce.Compute(0.3f, 0f, 0f, coupling, dt, 1f, 1f, out float twice, out _);
            Check(once == twice && once > 0f,
                "the push depends only on the water, never on the hull — no braking under sail");

            // A long frame must not hand over more than the current itself.
            DriftForce.Compute(2f, 0f, 0f, coupling, 1000f, 1f, 1f, out dvx, out dvz);
            Check(Math.Abs(dvx - 2f) < 1e-5f,
                $"a huge frame delivers at most the water's own speed ({Fmt(dvx)} vs water 2)");

            DriftForce.Compute(-5f, 0f, 0f, coupling, 10000f, 1f, 1f, out dvx, out dvz);
            Check(Math.Abs(dvx + 5f) < 1e-5f, "the one-tick clamp is symmetric for a reversed current");

            // An unattended hull, at the shipped default, is not touched at all.
            DriftForce.Compute(1f, 1f, 0f, coupling, dt, 0f, 1f, out dvx, out dvz);
            Check(dvx == 0f && dvz == 0f, "crewFactor 0 (the shipped unattended default) pushes nothing");

            DriftForce.Compute(1f, 0f, 0f, coupling, dt, 0.5f, 1f, out float half, out _);
            DriftForce.Compute(1f, 0f, 0f, coupling, dt, 1f, 1f, out float full, out _);
            Check(Math.Abs(half - full * 0.5f) < 1e-6f, "crewFactor scales the push linearly");

            // ---- the world edge ------------------------------------------------------------
            // Vanilla's ApplyEdgeForce starts at 10420. Undertow must be gone by then, or the
            // two argue and a boat judders on the rim of the world.
            Check(DriftForce.EdgeFade(0f) == 1f, "full current at the world centre");
            Check(DriftForce.EdgeFade(9000f) == 1f, "full current well inside the world");
            Check(DriftForce.EdgeFade(10420f) == 0f, "zero current where vanilla's edge force begins");
            Check(DriftForce.EdgeFade(11000f) == 0f, "zero current beyond the edge");
            Check(DriftForce.EdgeFadeEnd <= 10420f,
                "the fade completes at or before vanilla's ApplyEdgeForce threshold of 10420");

            float previous = 1.1f;
            bool monotonic = true;
            for (float d = 10150f; d <= 10450f; d += 10f)
            {
                float fade = DriftForce.EdgeFade(d);
                if (fade > previous + 1e-6f) monotonic = false;
                if (fade < 0f || fade > 1f) monotonic = false;
                previous = fade;
            }
            Check(monotonic, "the edge fade falls monotonically through 0..1");

            DriftForce.Compute(1f, 1f, 0f, coupling, dt, 1f, 0f, out dvx, out dvz);
            Check(dvx == 0f && dvz == 0f, "a zero edge fade pushes nothing");
        }

        private static void FlotsamMathTests()
        {
            Section("FlotsamMath");

            const float maxSpeed = 1.2f;
            const float minDepth = 12f;

            // ---- where flotsam gathers -----------------------------------------------------
            // The whole mechanic: slack water collects, fast water does not. If this inverts,
            // driftwood piles up in the races and the feature says the opposite of what it means.
            var slackSample = new FieldSample { Speed = 0.02f, Depth = 30f };
            var fastSample  = new FieldSample { Speed = 1.1f,  Depth = 30f };
            float slackWeight = FlotsamMath.GatherWeight(slackSample, maxSpeed, minDepth);
            float fastWeight  = FlotsamMath.GatherWeight(fastSample,  maxSpeed, minDepth);
            Check(slackWeight > fastWeight, $"slack water gathers more than fast ({Fmt(slackWeight)} vs {Fmt(fastWeight)})");
            Check(slackWeight > 0.9f, $"dead water gathers near maximum ({Fmt(slackWeight)})");

            // Monotonic across the whole range: no speed gathers more than a slower one.
            bool monotonic = true;
            float previous = 2f;
            for (float sp = 0f; sp <= maxSpeed; sp += 0.05f)
            {
                var s2 = new FieldSample { Speed = sp, Depth = 30f };
                float w = FlotsamMath.GatherWeight(s2, maxSpeed, minDepth);
                if (w > previous + 1e-6f) monotonic = false;
                if (w < 0f || w > 1f) monotonic = false;
                previous = w;
            }
            Check(monotonic, "gather weight falls monotonically through 0..1 as water speeds up");

            // ---- depth gate ----------------------------------------------------------------
            // NON-ZERO SPEED ON PURPOSE. These first used Speed = 0, which meant an unrelated
            // injury to the slackness term ALSO drove them to zero — they passed while the depth
            // gate was deleted, one bug masking another. Caught 2026-08-28 by injuring both at
            // once. At 0.1 m/s the weight is high unless the gate itself is what rejects it, and
            // the control below proves that.
            var atMin    = new FieldSample { Speed = 0.1f, Depth = minDepth };
            var shallow  = new FieldSample { Speed = 0.1f, Depth = 5f };
            var onLand   = new FieldSample { Speed = 0.1f, Depth = -3f };
            var deepEnough = new FieldSample { Speed = 0.1f, Depth = minDepth + 1f };
            Check(FlotsamMath.GatherWeight(deepEnough, maxSpeed, minDepth) > 0.5f,
                "the probe speed scores highly when deep enough, so a 0 below means the gate fired");
            Check(FlotsamMath.GatherWeight(atMin, maxSpeed, minDepth) == 0f, "no flotsam at exactly the minimum depth");
            Check(FlotsamMath.GatherWeight(shallow, maxSpeed, minDepth) == 0f, "no flotsam in the shallows");
            Check(FlotsamMath.GatherWeight(onLand, maxSpeed, minDepth) == 0f, "no flotsam on land");

            // ---- the rate is per HOUR, not per tick ----------------------------------------
            // Why that matters: FlotsamIntervalSeconds can be retuned without silently changing
            // how much washes up. A per-tick rate would couple two unrelated dials.
            Check(FlotsamMath.ShouldSpawn(1f, 3600f, 1f, 0.5f), "3600/hour over one second is certain");
            Check(!FlotsamMath.ShouldSpawn(1f, 0f, 60f, 0.0f), "a zero rate never spawns");
            Check(!FlotsamMath.ShouldSpawn(0f, 3600f, 60f, 0.0f), "zero gather weight never spawns");

            // Doubling the elapsed time doubles the chance - the property that keeps the rate stable.
            // RATE CHOSEN SO THE COUNTS ARE BIG. At the shipped 6/hour the expected counts are
            // about 2 per 1000, and a +/-2 tolerance swallowed the entire elapsed-time factor —
            // this test passed with `deltaSeconds` deleted from the formula. Measured 2026-08-28.
            // At 45/hour the windows land near 250 and 500, where a missing factor cannot hide.
            const float perHour = 45f;
            float shortWindow = 0f, longWindow = 0f;
            for (int i = 0; i < 1000; i++)
            {
                float roll = i / 1000f;
                if (FlotsamMath.ShouldSpawn(1f, perHour, 20f, roll)) shortWindow++;
                if (FlotsamMath.ShouldSpawn(1f, perHour, 40f, roll)) longWindow++;
            }
            Check(shortWindow > 200f && longWindow > 400f,
                $"the rate sweep produces counts large enough to mean something ({Fmt(shortWindow)}, {Fmt(longWindow)})");
            Check(Math.Abs(longWindow - shortWindow * 2f) <= 5f,
                $"twice the elapsed time gives twice the chance ({Fmt(shortWindow)} vs {Fmt(longWindow)} per 1000)");

            // ---- weighted pick -------------------------------------------------------------
            float[] table = { 1f, 3f };
            int zero = 0, one = 0;
            for (int i = 0; i < 1000; i++)
            {
                if (FlotsamMath.PickWeighted(table, i / 1000f) == 0) zero++; else one++;
            }
            Check(Math.Abs(zero - 250) < 15 && Math.Abs(one - 750) < 15,
                $"weights are respected ({zero}/{one} against an expected 250/750)");

            Check(FlotsamMath.PickWeighted(null, 0.5f) == -1, "a null table answers -1, not a crash");
            Check(FlotsamMath.PickWeighted(new float[0], 0.5f) == -1, "an empty table answers -1");
            Check(FlotsamMath.PickWeighted(new float[] { 0f, 0f }, 0.5f) == -1, "an all-zero table answers -1");
            Check(FlotsamMath.PickWeighted(new float[] { 0f, 1f }, 0f) == 1, "a zero-weight entry is never picked");

            // The very top of the range must land in bounds rather than falling off the end.
            bool inBounds = true;
            for (float r = 0f; r <= 1.0001f; r += 0.001f)
            {
                int idx = FlotsamMath.PickWeighted(table, r);
                if (idx < 0 || idx >= table.Length) inBounds = false;
            }
            Check(inBounds, "every roll from 0 to 1 inclusive picks a valid index");
        }

        private static void SwimDriftTests()
        {
            Section("SwimDrift");

            const float swimSpeed = 2f;          // vanilla Character.m_swimSpeed
            const float swimAccel = 0.05f;       // vanilla Character.m_swimAcceleration
            const float cap = 0.35f;
            const float factor = 0.5f;

            // ---- THE SCALING, which is the whole file --------------------------------------
            // Vanilla lerps m_currentVel toward the swimmer's intent each frame, so an addition
            // of d settles at d / m_swimAcceleration. At 0.05 that is a TWENTYFOLD amplification.
            // The delta must therefore be pre-multiplied by the acceleration, and the steady
            // state must come back out as the intended drift rather than 20x it.
            SwimDrift.Compute(1f, 0f, 1f, swimSpeed, 1f, swimAccel, out float dvx, out float dvz);
            float settled = SwimDrift.SteadyStateDrift(dvx, swimAccel);
            Check(Math.Abs(settled - 1f) < 1e-4f,
                $"a 1 m/s current settles a swimmer at 1 m/s, not 20 ({Fmt(settled)})");

            // Simulated rather than reasoned about: run vanilla's own lerp and see where it goes.
            float v = 0f;
            for (int i = 0; i < 4000; i++)
            {
                v = v + swimAccel * (0f - v);   // lerp toward zero intent (treading water)
                v += dvx;                        // our per-frame addition
            }
            Check(Math.Abs(v - 1f) < 0.01f,
                $"simulating vanilla's lerp converges on the intended drift ({Fmt(v)})");

            // ---- THE DROWNING GUARD, swept across the entire config range -------------------
            // A safety property, not a balance dial: at every legal setting a swimmer must still
            // out-swim the water. If this ever fails, someone can be pinned offshore until they
            // drown, and that is a broken feature rather than a mistuned one.
            bool alwaysEscapable = true;
            float worstRatio = 0f;
            for (float f = 0f; f <= 1.0f; f += 0.05f)
            for (float c2 = 0f; c2 <= 0.9f; c2 += 0.05f)
            for (float water = 0f; water <= 5f; water += 0.25f)
            {
                SwimDrift.Compute(water, 0f, f, swimSpeed, c2, swimAccel, out float x, out _);
                float drift = SwimDrift.SteadyStateDrift(x, swimAccel);
                float ratio = drift / swimSpeed;
                if (ratio > worstRatio) worstRatio = ratio;
                if (drift >= swimSpeed) alwaysEscapable = false;
            }
            Check(alwaysEscapable,
                $"across every legal config a swimmer out-swims the current (worst {Fmt(worstRatio)} of swim speed)");
            Check(worstRatio <= 0.9f + 1e-4f,
                $"the cap is honoured at the extreme of the range ({Fmt(worstRatio)})");

            // At the SHIPPED defaults, with the fastest water the field can produce.
            SwimDrift.Compute(1.2f, 0f, factor, swimSpeed, cap, swimAccel, out float defX, out _);
            float defaultDrift = SwimDrift.SteadyStateDrift(defX, swimAccel);
            Check(defaultDrift < swimSpeed * 0.4f,
                $"at shipped defaults the worst drift is well under swim speed ({Fmt(defaultDrift)} vs {swimSpeed})");
            Check(defaultDrift > 0.2f,
                $"and is still enough to feel ({Fmt(defaultDrift)} m/s)");

            // ---- direction, and the degenerate cases ---------------------------------------
            SwimDrift.Compute(-0.6f, 0.8f, factor, swimSpeed, cap, swimAccel, out dvx, out dvz);
            Check(Math.Abs(dvx / dvz + 0.6f / 0.8f) < 1e-4f, "the drift preserves the current's bearing");

            SwimDrift.Compute(0f, 0f, factor, swimSpeed, cap, swimAccel, out dvx, out dvz);
            Check(dvx == 0f && dvz == 0f, "still water moves no swimmer");

            SwimDrift.Compute(1f, 0f, 0f, swimSpeed, cap, swimAccel, out dvx, out dvz);
            Check(dvx == 0f && dvz == 0f, "SwimmerDriftFactor 0 leaves swimmers alone entirely");

            SwimDrift.Compute(1f, 0f, factor, swimSpeed, 0f, swimAccel, out dvx, out dvz);
            Check(dvx == 0f && dvz == 0f, "a zero cap leaves swimmers alone entirely");

            SwimDrift.Compute(1f, 0f, factor, swimSpeed, cap, 0f, out dvx, out dvz);
            Check(dvx == 0f && dvz == 0f && !float.IsNaN(dvx),
                "a zero swim acceleration does not divide by zero");

            Check(SwimDrift.SteadyStateDrift(1f, 0f) == 0f, "steady state with no acceleration is zero, not infinity");
        }

        private static float AngleBetween(FieldSample a, FieldSample b)
        {
            double la = Math.Sqrt(a.X * a.X + a.Z * a.Z);
            double lb = Math.Sqrt(b.X * b.X + b.Z * b.Z);
            if (la < 1e-6 || lb < 1e-6) return 0f;
            double d = (a.X * b.X + a.Z * b.Z) / (la * lb);
            d = Math.Max(-1.0, Math.Min(1.0, d));
            return (float)(Math.Acos(d) * 180.0 / Math.PI);
        }

        private static void ModConfigTests()
        {
            Section("ModConfig");

            var cfg = new ConfigFile();
            ModConfig.Bind(cfg);

            // 1. Every declared entry is actually bound.
            //
            // The failure this catches: someone adds `public static ConfigEntry<float> Foo;`
            // and forgets the matching cfg.Bind. The build is clean, the mod loads, and the
            // first read of Foo.Value throws — possibly weeks later, on someone else's server,
            // inside a try/catch that swallows it into a log nobody reads.
            List<FieldInfo> entryFields = ConfigEntryFields();
            Check(entryFields.Count > 0, "ModConfig declares at least one config entry");

            foreach (FieldInfo f in entryFields)
                Check(f.GetValue(null) != null, $"{f.Name} is bound (non-null after Bind)");

            // 2. One bind call per declared field. A field bound twice, or a bind with no
            //    field behind it, both mean the config file and the code disagree about what
            //    exists.
            Check(cfg.BoundCount == entryFields.Count,
                $"bind count matches field count ({cfg.BoundCount} bound, {entryFields.Count} declared)");

            // 3. No duplicate section/key. BepInEx returns the FIRST entry for a repeated key,
            //    so a copy-paste collision silently aliases two settings onto one value — the
            //    user changes one and the other moves with it.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var duplicates = new List<string>();
            foreach (ConfigFile.BoundEntry e in cfg.Bound)
                if (!seen.Add(e.Path)) duplicates.Add(e.Path);
            Check(duplicates.Count == 0,
                duplicates.Count == 0
                    ? "no duplicate section/key pairs"
                    : $"no duplicate section/key pairs (found: {string.Join(", ", duplicates)})");

            // 4. Every entry carries a description. This mod's settings are invisible in play —
            //    a current has no icon — so the config file's own text is the entire
            //    documentation a server owner gets.
            foreach (ConfigFile.BoundEntry e in cfg.Bound)
                Check(e.Description != null && !string.IsNullOrWhiteSpace(e.Description.Description),
                    $"{e.Path} has a description");

            // 5. Numeric defaults sit inside their own declared range. A default outside its
            //    AcceptableValueRange is clamped by BepInEx on first write, so the shipped
            //    default and the documented default silently differ.
            foreach (ConfigFile.BoundEntry e in cfg.Bound)
            {
                if (e.Description?.AcceptableValues is AcceptableValueRange<float> range
                    && e.DefaultValue is float value)
                {
                    Check(value >= range.MinValue && value <= range.MaxValue,
                        $"{e.Path} default {Fmt(value)} within [{Fmt(range.MinValue)}, {Fmt(range.MaxValue)}]");
                }
            }
        }

        /// <summary>Public static ConfigEntry&lt;T&gt; fields on ModConfig, in declaration order.</summary>
        private static List<FieldInfo> ConfigEntryFields()
        {
            var result = new List<FieldInfo>();
            foreach (FieldInfo f in typeof(ModConfig).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (f.FieldType.IsGenericType
                    && f.FieldType.GetGenericTypeDefinition() == typeof(ConfigEntry<>))
                {
                    result.Add(f);
                }
            }
            return result;
        }

        // ---- harness ----------------------------------------------------------------------

        private static void Section(string name) => Console.WriteLine($"-- {name}");

        private static void Check(bool condition, string what)
        {
            if (condition)
            {
                _passed++;
            }
            else
            {
                _failed++;
                Console.WriteLine($"   FAIL  {what}");
            }
        }

        private static string Fmt(float f) => f.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
