using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using UnityEngine;
using RavenIron.Undertow.Config;

namespace RavenIron.Undertow.Core
{
    /// <summary>
    /// The one place ambient work in this mod is driven from.
    ///
    /// HOUSE STYLE RULE 2: a time-budgeted cursor driven from a single Update, NOT coroutines.
    /// Every long-lived coroutine in this codebase's lineage independently grew the same bug —
    /// a `while (true)` whose body can `continue` past its only `yield`, hard-locking the game.
    /// It reached production once. A rule you must remember at every future edit is a rule that
    /// will eventually be forgotten, so the shape that cannot express the bug is the one used.
    ///
    /// Systems register once and are round-robined. Each Update spends at most
    /// <see cref="ModConfig.TickBudgetMs"/> milliseconds across all of them; whatever is left
    /// over resumes next frame from where the cursor stopped. No system can starve another,
    /// because the cursor never resets to zero — it advances.
    ///
    /// NO PERSISTENCE, deliberately. Ragnarok's Wrath's WorldTick carries an autosave here
    /// because it owns a store. Undertow owns none: the current field is a pure function, which
    /// is exactly why it needs no save file and no sync. If a future change adds a store, the
    /// shutdown flush belongs in OnDestroy — RW lost the last unsaved minute of a ledger on a
    /// clean server stop for want of one.
    /// </summary>
    public class SeaTick : MonoBehaviour
    {
        private static readonly List<IWorldSystem> _systems = new List<IWorldSystem>();
        private static readonly Dictionary<IWorldSystem, float> _lastRun =
            new Dictionary<IWorldSystem, float>();

        private static int _cursor;
        private static bool _initialised;
        private static bool _announced;

        private readonly Stopwatch _sw = new Stopwatch();

        /// <summary>
        /// Register a system. Safe to call before ZNet exists; Initialise is deferred until the
        /// first tick where we know whether this process is the authority.
        /// </summary>
        public static void Register(IWorldSystem system)
        {
            if (system == null) return;
            if (_systems.Contains(system)) return;

            _systems.Add(system);
            _lastRun[system] = 0f;
        }

        public static int SystemCount => _systems.Count;

        /// <summary>True once the online line has been printed — i.e. once ZNet exists.</summary>
        public static bool Online => _announced;

        private void Update()
        {
            // Nothing is knowable until ZNet exists; this also covers the main menu.
            ZNet znet = ZNet.instance;
            if (znet == null) return;

            // PROOF OF LIFE, printed on EVERY role and before any system exists.
            //
            // Deliberately not gated on authority or on a non-empty system list. Ragnarok's
            // Wrath's equivalent returns early when no systems are registered, which would make
            // this mod's task 0 unverifiable — a skeleton with nothing to tick would boot in
            // total silence, and a silent success is indistinguishable from a silent no-op from
            // outside the game.
            if (!_announced)
            {
                _announced = true;
                Undertow.Log.LogInfo(
                    $"SeaTick online — {_systems.Count} system(s), " +
                    $"budget {ModConfig.TickBudgetMs.Value}ms/frame, " +
                    $"authority={Undertow.IsSimulationAuthority()}, " +
                    $"dedicated={Undertow.IsDedicated()}");

                if (!Undertow.IsSimulationAuthority())
                {
                    // Not a warning. A pure client is a correct and expected place for this mod
                    // to be: it owns the boats it sails, and the drift patch runs here.
                    Undertow.Log.LogInfo(
                        "SeaTick: pure client — ambient systems idle here by design. " +
                        "Current still applies to boats this machine owns.");
                }
            }

            MaybeReportField();
            MaybeReportStorm();
            MaybeReportFloats();

            if (!Undertow.IsSimulationAuthority()) return;
            if (_systems.Count == 0) return;

            if (!_initialised)
            {
                InitialiseSystems();
                _initialised = true;
            }

            float now = Time.realtimeSinceStartup;
            float budgetMs = ModConfig.TickBudgetMs.Value;

            _sw.Restart();

            // Walk at most one full lap per frame. The cursor persists across frames, so a lap
            // interrupted by the budget resumes rather than restarting — that is what stops the
            // systems early in the list from starving the ones after them.
            int examined = 0;
            while (examined < _systems.Count && _sw.Elapsed.TotalMilliseconds < budgetMs)
            {
                IWorldSystem system = _systems[_cursor];
                _cursor = (_cursor + 1) % _systems.Count;
                examined++;

                if (!system.Enabled) continue;

                float last = _lastRun[system];
                float due = now - last;
                if (due < system.IntervalSeconds) continue;

                _lastRun[system] = now;

                // One misbehaving system must not take the others down with it. This is the
                // gameplay-path sibling of house style rule 3: catch, log, keep going.
                try
                {
                    system.Tick(due);
                }
                catch (Exception ex)
                {
                    Undertow.Log.LogError($"[{system.Name}] tick threw: {ex}");
                }
            }

            _sw.Stop();
        }

        private static bool _fieldReported;

        /// <summary>
        /// One-shot dump of the field at five fixed points, spread across the world.
        ///
        /// Verbose-gated and off by default, but not scaffolding: on a headless server nobody
        /// can type `wake field` into a console that has no keyboard attached, so without this
        /// the entire field is unobservable on the machine that matters most. It is also the
        /// fastest way for a server owner to see what their seed's ocean actually does.
        ///
        /// Deferred rather than run at announce time: WorldGenerator is created when the world
        /// loads, which is later than ZNet, so this retries until the world exists.
        /// </summary>
        private static void MaybeReportField()
        {
            if (_fieldReported) return;
            if (!ModConfig.VerboseLogging.Value) return;
            if (!SeaContext.TryGetSeed(out int seed)) return;

            _fieldReported = true;

            var c = CultureInfo.InvariantCulture;

            var sb = new StringBuilder(512);
            sb.Append($"CurrentField live — seed {seed}, water level ");
            sb.Append(SeaContext.WaterLevel.ToString("0.#", c));
            sb.Append(", tide ");
            sb.Append((CurrentField.TidePhase01(SeaContext.WorldTimeSeconds,
                        ModConfig.TidePeriodSeconds.Value) * 100f).ToString("0", c));
            // Season WITH its provenance. Index 0 is spring and is also every failure mode, so
            // the number alone is not evidence of anything — see WrathBridge.SeasonWasRead.
            int season = SeaContext.SeasonIndex;
            sb.Append("%, season index ").Append(season)
              .Append(Bridge.WrathBridge.SeasonWasRead ? " (read from Wrath)" : " (defaulted, no Wrath)");

            // A transect straight out from spawn, which is guaranteed to cross from land to
            // deep ocean. RAW HEIGHT IS LOGGED ALONGSIDE DEPTH deliberately: the first version
            // of this line reported two distant ocean points at depth exactly 30.0m, and a
            // derived number that suspiciously round is a reason to print the number it was
            // derived from rather than to reason about what it must mean.
            for (int i = 0; i <= 10; i++)
            {
                float x = i * 1000f;
                float height = WorldTerrainProbe.Instance.HeightAt(x, 0f);
                if (!SeaContext.TryEvaluate(x, 0f, out FieldSample s)) continue;

                sb.Append($"\n  x={x.ToString("0", c)} h={height.ToString("0.##", c)} ");
                sb.Append(s.Depth <= 0f
                    ? "land"
                    : $"depth {s.Depth.ToString("0.##", c)}m {s.Speed.ToString("0.###", c)}m/s {s.Dominant}");
            }

            Undertow.Log.LogInfo(sb.ToString());
        }

        private static bool _floatsReported;

        /// <summary>
        /// One-shot answer to the question that gates flotsam: which vanilla prefabs float.
        ///
        /// Verbose-gated and one-shot. Retries until a prefab list exists, because ObjectDB and
        /// ZNetScene are populated some way into the load and answering "nothing floats" early
        /// would be a confident false negative — the exact failure this scan exists to avoid.
        /// </summary>
        private static void MaybeReportFloats()
        {
            if (_floatsReported) return;
            if (!ModConfig.VerboseLogging.Value) return;

            if (!FloatScan.TryScan(out FloatScan.Result r)) return;   // not loaded yet; try again
            _floatsReported = true;
            Undertow.Log.LogInfo(FloatScan.Describe());
        }

        private static bool _stormWasRunning;
        private static float _nextStormPoll;

        /// <summary>
        /// Announce a storm the first time it is seen, and prove the surge reaches the field.
        ///
        /// THE LAST UNVERIFIED LINK IN THE BRIDGE, and it cannot be checked any other way on a
        /// headless server: nobody can type `wake here` while standing in a storm. Everything
        /// else is provable — the field multiplies by surge (harness), the bridge resolves
        /// IsStormAt (boot log) — but "IsStormAt returns TRUE when a storm is actually overhead"
        /// needs a storm to happen. This logs the answer when one does.
        ///
        /// Verbose-gated and polled slowly. It reads RW's storm centre, asks our own bridge
        /// whether that point is in a storm, and evaluates the field there — so a disagreement
        /// between the two shows up as a contradiction on one line rather than as a mod that
        /// quietly never surges.
        /// </summary>
        private static void MaybeReportStorm()
        {
            if (!ModConfig.VerboseLogging.Value) return;

            float now = Time.realtimeSinceStartup;
            if (now < _nextStormPoll) return;
            _nextStormPoll = now + 5f;

            bool running = Bridge.WrathBridge.TryGetStorm(out Vector3 centre, out float stormRange);
            if (running == _stormWasRunning) return;
            _stormWasRunning = running;

            var c = CultureInfo.InvariantCulture;

            if (!running)
            {
                Undertow.Log.LogInfo("storm lifted — the sea drops back.");
                return;
            }

            bool insideAtCentre = Bridge.WrathBridge.IsStormAt(centre);
            float surge = SeaContext.StormSurgeAt(centre);

            var sb = new StringBuilder(220);
            sb.Append($"STORM at ({centre.x.ToString("0", c)}, {centre.z.ToString("0", c)}) — ");
            sb.Append($"IsStormAt(centre)={insideAtCentre}, surge x{surge.ToString("0.##", c)}");

            if (SeaContext.TryEvaluate(centre.x, centre.z, out FieldSample at))
                sb.Append($" | at centre: {at.Speed.ToString("0.###", c)} m/s {at.Dominant}");

            // 800m out. The design promise is that a storm raises the sea WHERE IT STANDS and
            // nowhere else, so the contrast is the point of the line.
            if (SeaContext.TryEvaluate(centre.x + 800f, centre.z, out FieldSample away))
                sb.Append($" | 800m away: {away.Speed.ToString("0.###", c)} m/s surge x{away.StormSurge.ToString("0.##", c)}");

            Undertow.Log.LogInfo(sb.ToString());
        }

        private static void InitialiseSystems()
        {
            foreach (IWorldSystem system in _systems)
            {
                try
                {
                    system.Initialise();
                    Undertow.Log.LogInfo(
                        $"[{system.Name}] initialised (enabled={system.Enabled}, interval={system.IntervalSeconds}s)");
                }
                catch (Exception ex)
                {
                    Undertow.Log.LogError($"[{system.Name}] failed to initialise: {ex}");
                }
            }
        }

        private void OnDestroy()
        {
            _systems.Clear();
            _lastRun.Clear();
            _cursor = 0;
            _initialised = false;
            _announced = false;
            _fieldReported = false;
        }
    }
}
