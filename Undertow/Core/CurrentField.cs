using System;

namespace RavenIron.Undertow.Core
{
    /// <summary>
    /// Seabed height at a world XZ. The one thing the field needs from the game.
    ///
    /// Abstracted so <see cref="CurrentField"/> stays pure and testable: the shipping probe
    /// asks <c>WorldGenerator</c> (itself a pure function of the world seed, which is what makes
    /// the whole no-sync design work), and the harness supplies synthetic coastlines.
    /// </summary>
    public interface ITerrainProbe
    {
        /// <summary>Ground height in world units. Water level is 30 in vanilla.</summary>
        float HeightAt(float x, float z);
    }

    /// <summary>Which term is actually doing the work at a point. Diagnostic only.</summary>
    public enum CurrentTerm
    {
        Land,      // above water — no current at all
        Shallows,  // close enough to the waterline that everything is faded out
        Drift,     // open-ocean circulation
        Coastal,   // the shore is steering it
        Race,      // constricted between two rises — accelerated
        Slack      // opposing arms cancelling — near-zero water
    }

    /// <summary>One evaluation of the field. Velocities are metres per second of WATER.</summary>
    public struct FieldSample
    {
        public float X, Z;
        public float Speed;
        /// <summary>Metres below water level. Zero or less means land.</summary>
        public float Depth;
        /// <summary>0..1 through the tide cycle. 0.25 is peak flood, 0.75 peak ebb.</summary>
        public float TidePhase01;
        /// <summary>Storm surge multiplier applied here. 1.0 when no storm stands over it.</summary>
        public float StormSurge;
        public CurrentTerm Dominant;
    }

    /// <summary>
    /// Every tuning value the field reads, passed in rather than fetched.
    ///
    /// The pure file never touches ModConfig — that is what lets the harness evaluate the field
    /// at arbitrary parameters, including ones no config would allow, and what stops a config
    /// default from quietly becoming part of the maths.
    /// </summary>
    public struct FieldSettings
    {
        public float WaterLevel;
        /// <summary>Ceiling on water speed anywhere in the world, m/s.</summary>
        public float MaxSpeed;
        /// <summary>Seconds for one full flood-ebb cycle.</summary>
        public float TidePeriodSeconds;
        /// <summary>How hard the tide swings open-ocean magnitude, 0..1.</summary>
        public float TideAmplitude;
        /// <summary>Share of MaxSpeed the coastal set may reach.</summary>
        public float CoastalStrength;
        /// <summary>Depth at which coastal steering has faded out entirely.</summary>
        public float ShelfDepth;
        /// <summary>Below this depth everything fades to zero, so nothing is shoved aground.</summary>
        public float ShallowFadeDepth;
        /// <summary>Ceiling on the constriction multiplier in a race.</summary>
        public float MaxRaceMultiplier;

        public static FieldSettings Defaults => new FieldSettings
        {
            WaterLevel = 30f,
            // ~1.2 m/s against a karve's half-sail of roughly 5-6 m/s: the design's
            // "15-25% of half-sail at the strongest water in the world". A correction, never
            // a tow. Task 2 measures this rather than trusting it.
            MaxSpeed = 1.2f,
            // Two in-game days at vanilla's 1800s day.
            TidePeriodSeconds = 3600f,
            TideAmplitude = 0.25f,
            CoastalStrength = 0.8f,
            // MEASURED, not chosen: Valheim's open ocean has a FLAT floor at generator height
            // exactly 0, so with a water level of 30 the deep sea is uniformly 30m down (server
            // transect, 2026-08-28). The first value here was 40, which put every point in the
            // ocean on the "shelf" and kept the coastal term out of open water only by the
            // accident that a flat floor has no gradient. At 28 the ramp reaches zero just
            // before the seabed does, so "shelf" means genuinely shallower than the deep.
            ShelfDepth = 28f,
            ShallowFadeDepth = 8f,
            MaxRaceMultiplier = 1.6f
        };
    }

    /// <summary>
    /// The shape of the sea. Pure arithmetic — no clock, no config, no Unity, no game types —
    /// so the harness compiles and tests the shipping source rather than a copy of it.
    ///
    /// DETERMINISTIC BY CONSTRUCTION, WHICH IS THE WHOLE DESIGN. Every output is a function of
    /// (position, seed, world time, season, terrain), and terrain is itself a pure function of
    /// the seed. Two machines on the same world therefore agree on the current at a point
    /// without exchanging a byte — no sync layer, no save file, no authority. Anything that
    /// breaks that property breaks the mod's architecture, not just its numbers.
    ///
    /// OPEN WATER COMES FROM A STREAM FUNCTION. Rather than adding up hand-tuned "gyre",
    /// "race" and "eddy" terms and hoping they compose, the open-ocean flow is the
    /// perpendicular gradient of a scalar field psi: u = dpsi/dz, v = -dpsi/dx. That makes the
    /// field DIVERGENCE-FREE by construction — water is neither created nor destroyed anywhere
    /// — and it means closed gyres, fast water where cells align, and slack at the saddles all
    /// fall out of one mechanism instead of three that must be balanced against each other.
    /// Three plane waves at descending wavelengths are enough to look like an ocean.
    ///
    /// NOT UnityEngine.Random. Vanilla's own wind octaves save and restore Random.state around
    /// their calls precisely because it is shared global mutable state; a field that consumed
    /// it would perturb every other consumer and stop being a pure function of position.
    /// </summary>
    public static class CurrentField
    {
        // Wavelengths in metres. The longest is the Great Drift — basin-scale, the thing a crew
        // plans a voyage around. The shortest is still 1.8km, because a current you cannot hold
        // a heading through for several minutes is noise, not geography.
        private const float Wave1 = 6000f;
        private const float Wave2 = 3500f;
        private const float Wave3 = 1800f;

        // Relative contribution of each wave to velocity. Normalised out below, so these are
        // ratios rather than speeds.
        private const float Weight1 = 1.0f;
        private const float Weight2 = 0.6f;
        private const float Weight3 = 0.35f;

        /// <summary>Metres between the samples used for the shore gradient.</summary>
        private const float GradientStep = 24f;

        /// <summary>
        /// Metres either side of the flow used to detect a constriction, at two scales.
        ///
        /// TWO SCALES BECAUSE ONE IS NOT ENOUGH, found by a failing test on 2026-08-28: a single
        /// 64m probe can only see gaps narrower than about 128m, so it detected a tight race
        /// between rocks and was completely blind to a 300m strait between two islands — which
        /// is the more recognisable landmark of the two and exactly what this term exists to
        /// create. The stronger squeeze of the two wins.
        /// </summary>
        private static readonly float[] RaceProbeDistances = { 64f, 160f };

        /// <summary>
        /// Seasonal rotation of the open-ocean field, in degrees per season step.
        ///
        /// DELIBERATELY SMALL, and this is a design decision rather than a tuning one. A full
        /// seasonal reversal was the original sketch; it would invalidate every seamark a crew
        /// had learned, four times a year, which destroys the one thing the mod is for. A modest
        /// rotation plus a magnitude change gives the sea a season without erasing its geography.
        /// </summary>
        private const float SeasonRotationDegrees = 15f;

        /// <summary>Open-ocean magnitude by season index (0 spring, 1 summer, 2 fall, 3 winter).</summary>
        private static readonly float[] SeasonMagnitude = { 0.95f, 0.8f, 1.1f, 1.25f };

        /// <summary>
        /// 0..1 through the flood-ebb cycle. Separated out because the console reports it and
        /// the harness pins it, and because it is the one part of the field a player can feel
        /// changing while standing still.
        /// </summary>
        public static float TidePhase01(double worldTimeSeconds, float tidePeriodSeconds)
        {
            if (tidePeriodSeconds <= 0f) return 0f;
            double turns = worldTimeSeconds / tidePeriodSeconds;
            double frac = turns - Math.Floor(turns);
            return (float)frac;
        }

        public static FieldSample Evaluate(
            float x, float z,
            int seed,
            double worldTimeSeconds,
            int seasonIndex,
            float stormSurge,
            ITerrainProbe probe,
            FieldSettings s)
        {
            var result = new FieldSample
            {
                TidePhase01 = TidePhase01(worldTimeSeconds, s.TidePeriodSeconds)
            };

            if (probe == null) return result;

            float height = probe.HeightAt(x, z);
            float depth = s.WaterLevel - height;
            result.Depth = depth;

            // On land there is no water to move. Answering zero rather than refusing keeps every
            // caller free of a special case.
            if (depth <= 0f)
            {
                result.Dominant = CurrentTerm.Land;
                return result;
            }

            double tideRadians = result.TidePhase01 * 2.0 * Math.PI;
            float tideSin = (float)Math.Sin(tideRadians);

            // ---- open ocean: the perpendicular gradient of the stream function ----------
            float season = SeasonRotationDegrees * (float)DEG2RAD * NormaliseSeason(seasonIndex);
            StreamGradient(x, z, seed, season, out float u, out float v);

            float magnitude = SeasonMagnitude[NormaliseSeasonIndex(seasonIndex)]
                              * (1f + s.TideAmplitude * tideSin);
            u *= magnitude;
            v *= magnitude;

            // ---- the shore steers it ----------------------------------------------------
            // Gradient of the seabed. Points uphill, so toward land.
            float hEast  = probe.HeightAt(x + GradientStep, z);
            float hWest  = probe.HeightAt(x - GradientStep, z);
            float hNorth = probe.HeightAt(x, z + GradientStep);
            float hSouth = probe.HeightAt(x, z - GradientStep);

            float gx = hEast - hWest;
            float gz = hNorth - hSouth;
            float gLen = (float)Math.Sqrt(gx * gx + gz * gz);

            bool coastal = false;
            if (gLen > 1e-4f && depth < s.ShelfDepth)
            {
                gx /= gLen;
                gz /= gLen;

                // Shore-parallel, its sense reversing with the tide — flood one way, ebb the
                // other. That reversal is the part of the design a player can actually feel.
                float tangentX = -gz;
                float tangentZ = gx;

                // 1 at the waterline, 0 at the shelf edge.
                float shelf = 1f - Clamp01(depth / s.ShelfDepth);
                float coastalSpeed = s.CoastalStrength * shelf * tideSin;

                // A slight push toward land, always. This is the lee shore: it is why you do not
                // doze at the tiller with the coast downwind of you.
                const float onshoreShare = 0.15f;

                u += tangentX * coastalSpeed + gx * coastalSpeed * onshoreShare;
                v += tangentZ * coastalSpeed + gz * coastalSpeed * onshoreShare;

                coastal = shelf > 0.35f;
            }

            // ---- races: a constriction accelerates the flow ------------------------------
            float speed = (float)Math.Sqrt(u * u + v * v);
            bool race = false;
            if (speed > 1e-4f)
            {
                float dirX = u / speed;
                float dirZ = v / speed;

                float bestSqueeze = 0f;
                for (int i = 0; i < RaceProbeDistances.Length; i++)
                {
                    float d = RaceProbeDistances[i];

                    // Across the flow, both ways. Two rises facing each other is a channel.
                    float hLeft  = probe.HeightAt(x - dirZ * d, z + dirX * d);
                    float hRight = probe.HeightAt(x + dirZ * d, z - dirX * d);

                    float riseLeft  = hLeft  - height;
                    float riseRight = hRight - height;
                    if (riseLeft <= 0f || riseRight <= 0f) continue;

                    // Scale by the WEAKER of the two, so a single steep bank on one side does
                    // not read as a channel — a coast is not a strait.
                    float squeeze = Clamp01(Math.Min(riseLeft, riseRight) / 20f);
                    if (squeeze > bestSqueeze) bestSqueeze = squeeze;
                }

                if (bestSqueeze > 0f)
                {
                    float multiplier = 1f + (s.MaxRaceMultiplier - 1f) * bestSqueeze;
                    u *= multiplier;
                    v *= multiplier;
                    speed *= multiplier;
                    race = bestSqueeze > 0.25f;
                }
            }

            // ---- fade to nothing at the waterline ---------------------------------------
            // Never shove a hull aground. The last few metres of depth carry no current at all,
            // which also keeps the field honest where the terrain gradient goes wild.
            float shallowFade = Clamp01(depth / Math.Max(0.01f, s.ShallowFadeDepth));
            u *= shallowFade;
            v *= shallowFade;
            speed *= shallowFade;

            // ---- storm surge -------------------------------------------------------------
            // A storm standing over this water makes it run harder. Applied BEFORE the ceiling
            // on purpose: MaxCurrentSpeed is documented as the ceiling on water speed ANYWHERE
            // in the world, and a storm that exceeded it would make that description a lie. So a
            // storm drives weak water toward the ceiling and leaves already-fast water where it
            // is - which is both honest to the config and the more interesting behaviour: the
            // sheltered passage stops being sheltered.
            //
            // Passed in rather than fetched, so this file stays pure. The storm is somebody
            // else's fact - see Bridge/WrathBridge.
            if (stormSurge > 0f && stormSurge != 1f)
            {
                u *= stormSurge;
                v *= stormSurge;
                speed *= stormSurge;
            }

            // ---- ceiling -----------------------------------------------------------------
            if (speed > s.MaxSpeed && speed > 1e-6f)
            {
                float scale = s.MaxSpeed / speed;
                u *= scale;
                v *= scale;
                speed = s.MaxSpeed;
            }

            result.X = u;
            result.Z = v;
            result.Speed = speed;
            result.StormSurge = stormSurge;
            result.Dominant = Classify(speed, depth, s, shallowFade, coastal, race);
            return result;
        }

        private static CurrentTerm Classify(
            float speed, float depth, FieldSettings s, float shallowFade, bool coastal, bool race)
        {
            if (shallowFade < 0.5f) return CurrentTerm.Shallows;
            if (speed < s.MaxSpeed * 0.12f) return CurrentTerm.Slack;
            if (race) return CurrentTerm.Race;
            if (coastal) return CurrentTerm.Coastal;
            return CurrentTerm.Drift;
        }

        /// <summary>
        /// u = dpsi/dz, v = -dpsi/dx for psi built from three plane waves. Differentiated
        /// analytically rather than by finite difference: exact, cheaper, and it cannot pick up
        /// the step-size artefacts that would make the field disagree between machines running
        /// at different float settings.
        /// </summary>
        private static void StreamGradient(float x, float z, int seed, float rotation,
                                           out float u, out float v)
        {
            u = 0f;
            v = 0f;

            AddWave(x, z, seed, 1, Wave1, Weight1, rotation, ref u, ref v);
            AddWave(x, z, seed, 2, Wave2, Weight2, rotation, ref u, ref v);
            AddWave(x, z, seed, 3, Wave3, Weight3, rotation, ref u, ref v);

            float norm = Weight1 + Weight2 + Weight3;
            u /= norm;
            v /= norm;
        }

        private static void AddWave(float x, float z, int seed, int index,
                                    float wavelength, float weight, float rotation,
                                    ref float u, ref float v)
        {
            // Direction and phase are the only seed-derived quantities in the field. Everything
            // else is fixed, so two worlds differ in where their gyres sit, not in how an ocean
            // behaves.
            float angle = Hash01(seed, index * 7919) * (float)(2.0 * Math.PI) + rotation;
            float phase = Hash01(seed, index * 104729) * (float)(2.0 * Math.PI);

            float dirX = (float)Math.Cos(angle);
            float dirZ = (float)Math.Sin(angle);

            float k = (float)(2.0 * Math.PI) / wavelength;
            float arg = k * (x * dirX + z * dirZ) + phase;
            float c = (float)Math.Cos(arg);

            // psi_i = (weight / k) * sin(arg), so the velocity amplitude is exactly `weight`
            // regardless of wavelength — which is what lets the weights above read as ratios.
            u += weight * dirZ * c;
            v += -weight * dirX * c;
        }

        private const double DEG2RAD = Math.PI / 180.0;

        private static int NormaliseSeasonIndex(int seasonIndex)
        {
            int i = seasonIndex % 4;
            if (i < 0) i += 4;
            return i;
        }

        /// <summary>Season as a signed step so the rotation runs -1, 0, +1, +2 rather than 0..3.</summary>
        private static float NormaliseSeason(int seasonIndex) => NormaliseSeasonIndex(seasonIndex);

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        /// <summary>
        /// Deterministic 0..1 from a seed and a salt. Integer avalanche, no floating point and
        /// no shared state, so it answers identically on every machine and every run.
        /// </summary>
        private static float Hash01(int seed, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u ^ (uint)salt * 2246822519u;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                h *= 3266489917u;
                h ^= h >> 16;
                return (h & 0xFFFFFFu) / (float)0x1000000;
            }
        }
    }
}
