using System;
using UnityEngine;
using RavenIron.Undertow.Config;
using RavenIron.Undertow.Bridge;

namespace RavenIron.Undertow.Core
{
    /// <summary>
    /// Seabed height straight from <c>WorldGenerator</c>.
    ///
    /// This is the reason the whole mod needs no sync: WorldGenerator is a pure function of the
    /// world seed, so every machine already agrees about the shape of the seabed before anyone
    /// says a word. Reading terrain from the generator rather than from loaded heightmaps also
    /// means the field answers for open ocean nobody has ever visited — which the console needs,
    /// and which a loaded-zone read could never do.
    ///
    /// COST, and it is task 2's problem rather than task 1's: one evaluation makes seven
    /// GetHeight calls. That is free for a console command and is NOT obviously free per boat
    /// per FixedUpdate. Measure it before the drift patch ships; a small memo cache keyed on
    /// rounded coordinates is the obvious answer if it bites, and rounding is safe precisely
    /// because the field is smooth at the scale of a boat.
    /// </summary>
    public sealed class WorldTerrainProbe : ITerrainProbe
    {
        public static readonly WorldTerrainProbe Instance = new WorldTerrainProbe();

        public float HeightAt(float x, float z)
        {
            WorldGenerator wg = WorldGenerator.instance;
            // Above every water level in the game, so callers read "land" and produce no
            // current. Failing to a dead calm would be worse: it looks like the mod working.
            if (wg == null) return 10000f;
            return wg.GetHeight(x, z);
        }
    }

    /// <summary>
    /// The seam between the game and the pure field: everything <see cref="CurrentField"/>
    /// needs, fetched from live game state and handed over as plain values.
    ///
    /// Every accessor here is a real public member of the shipping assembly — verified against
    /// the NON-publicized `assembly_valheim.dll` on 2026-08-28, after house rule 5 cost a
    /// round-trip on `Terminal.commands`. `WorldGenerator.instance`, `GetHeight`, `GetSeed`,
    /// `ZoneSystem.instance`, `m_waterLevel`, `ZNet.instance` and `GetTimeSeconds` are all
    /// genuinely public, so none of this needs reflection. Re-check before adding a new one.
    /// </summary>
    public static class SeaContext
    {
        /// <summary>Vanilla's water level, or its default when ZoneSystem is not up yet.</summary>
        public static float WaterLevel
        {
            get
            {
                ZoneSystem zs = ZoneSystem.instance;
                return zs != null ? zs.m_waterLevel : 30f;
            }
        }

        /// <summary>
        /// World clock in seconds. Shared by every peer, so the tide agrees everywhere.
        ///
        /// Note the inherited trap: `ZNet.UpdateNetTime` returns early at zero players, so this
        /// stops advancing on an empty server. Harmless here — with nobody aboard there is no
        /// boat to push and nobody to see a tide — but do not build anything on it that must
        /// accrue while the world is empty.
        /// </summary>
        public static double WorldTimeSeconds
        {
            get
            {
                ZNet net = ZNet.instance;
                return net != null ? net.GetTimeSeconds() : 0.0;
            }
        }

        public static bool TryGetSeed(out int seed)
        {
            WorldGenerator wg = WorldGenerator.instance;
            if (wg == null)
            {
                seed = 0;
                return false;
            }
            seed = wg.GetSeed();
            return true;
        }

        /// <summary>
        /// Season index, 0..3 spring-summer-fall-winter, from Ragnarok's Wrath when present and
        /// spring when it is not.
        ///
        /// Undertow deliberately runs NO season clock of its own. Two mods with different ideas
        /// about the season is exactly the conflict house rule 4 exists to prevent, and RW
        /// already handles the either/or between Seasonality and shudnal's Seasons. We take
        /// whatever it concluded.
        /// </summary>
        public static int SeasonIndex =>
            ModConfig.EnableWrathBridge.Value ? WrathBridge.SeasonIndex() : 0;

        /// <summary>
        /// Storm surge multiplier at a position. 1.0 with no storm, with RW absent, or with the
        /// bridge switched off.
        /// </summary>
        public static float StormSurgeAt(Vector3 position)
        {
            if (!ModConfig.EnableWrathBridge.Value) return 1f;
            return WrathBridge.IsStormAt(position) ? ModConfig.StormSurgeMultiplier.Value : 1f;
        }

        public static FieldSettings BuildSettings()
        {
            FieldSettings s = FieldSettings.Defaults;
            s.WaterLevel = WaterLevel;
            s.MaxSpeed = ModConfig.MaxCurrentSpeed.Value;
            s.TidePeriodSeconds = ModConfig.TidePeriodSeconds.Value;
            s.TideAmplitude = ModConfig.TideAmplitude.Value;
            s.CoastalStrength = ModConfig.CoastalStrength.Value;
            return s;
        }

        /// <summary>
        /// The one door. Answers false only when the world is not up yet, which callers should
        /// report rather than paper over — "no current here" and "no world loaded" are different
        /// answers and look identical if they are both a zero vector.
        /// </summary>
        public static bool TryEvaluate(float x, float z, out FieldSample sample)
        {
            sample = default(FieldSample);
            if (!TryGetSeed(out int seed)) return false;

            sample = CurrentField.Evaluate(
                x, z,
                seed,
                WorldTimeSeconds,
                SeasonIndex,
                StormSurgeAt(new Vector3(x, 0f, z)),
                WorldTerrainProbe.Instance,
                BuildSettings());
            return true;
        }
    }
}
