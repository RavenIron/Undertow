using System;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace RavenIron.Undertow.Bridge
{
    /// <summary>
    /// Read-only, one-directional bridge to Ragnarok's Wrath. The studio's third use of the
    /// pattern FireFront and RW already share, pointed a third way.
    ///
    /// SOFT BY CONSTRUCTION. Undertow never references RW's assembly, never lists it as a
    /// dependency, and runs complete without it — the sea simply has no storms in it and no
    /// season. Everything here resolves by reflection at runtime and answers a neutral default
    /// when it cannot.
    ///
    /// WE READ FACTS, NOT TUNING. RW exposes `WindMultiplierAt(pos)`, which already rises in a
    /// storm and would have been the easy call. It is deliberately unused: that value is
    /// `StormWindMultiplier`, which a server owner sets to tune FIRE SPREAD. Borrowing it would
    /// mean raising your fire risk silently roughened the sea - a coupling neither mod's owner
    /// asked for, and one nobody would think to look for. We read the FACT that a storm is
    /// running and apply our own multiplier from our own config.
    ///
    /// TWO DIFFERENT ROUTES, FOR A MEASURED REASON. The SEASON comes from RW by reflection. The
    /// STORM does not: it comes from vanilla's own replicated RandomEvent, because RW's storm
    /// state is only maintained on the simulation authority and is dead on every client. See
    /// IsStormAt. The season has the same limitation and no such workaround - documented there.
    ///
    /// DETERMINISM, AND ITS ONE HONEST LIMIT. The rest of the field is identical on every
    /// machine, which is why the mod needs no sync. Storm surge is not: a peer without RW
    /// computes no surge. That does NOT desync boats — a hull's physics runs only on the peer
    /// that owns it and its position is replicated from there — but two players CAN read
    /// different `wake here` values during a storm if their installs differ. RW is a
    /// server-and-clients mod anyway, so in practice everyone has it or nobody does. Documented
    /// rather than defended against.
    /// </summary>
    public static class WrathBridge
    {
        public const string WrathPluginId = "com.raveniron.ragnarokswrath";

        private const string SeasonSystemType  = "RavenIron.RagnaroksWrath.Systems.World.SeasonSystem";

        private static MethodInfo _seasonGetter;

        private static bool _installedChecked;
        private static bool _installed;
        private static bool _loggedResult;

        /// <summary>Retry cadence. RW may finish loading after us, so absence is never latched.</summary>
        private const float RetrySeconds = 10f;
        private static float _nextAttempt;

        /// <summary>
        /// True once RW's season is readable. The STORM path needs nothing from here — it goes
        /// through vanilla's replicated event, see <see cref="IsStormAt"/> — so this is purely
        /// about the season.
        /// </summary>
        public static bool Available => _seasonGetter != null;

        /// <summary>True when RW is loaded at all, whatever resolved.</summary>
        public static bool WrathInstalled
        {
            get { EnsureInstalledChecked(); return _installed; }
        }

        private static void EnsureInstalledChecked()
        {
            if (_installedChecked) return;
            try
            {
                _installed = Chainloader.PluginInfos != null &&
                             Chainloader.PluginInfos.ContainsKey(WrathPluginId);
                _installedChecked = true;
            }
            catch
            {
                // Chainloader not ready. Leave unchecked so we ask again rather than latching a
                // "not installed" that was only "not yet".
            }
        }

        private static void TryResolve()
        {
            if (_seasonGetter != null) return;

            float now = Time.realtimeSinceStartup;
            if (now < _nextAttempt) return;
            _nextAttempt = now + RetrySeconds;

            EnsureInstalledChecked();

            if (_installedChecked && !_installed)
            {
                if (!_loggedResult)
                {
                    _loggedResult = true;
                    Undertow.Log.LogInfo(
                        "Ragnarok's Wrath not installed — storm surge and seasons are dormant. " +
                        "The sea still runs; it just has no weather behind it.");
                }
                return;
            }

            try
            {
                Type season = AccessTools.TypeByName(SeasonSystemType);
                _seasonGetter = season == null ? null : AccessTools.PropertyGetter(season, "Current");
            }
            catch (Exception ex)
            {
                Undertow.Log.LogWarning($"Wrath bridge: resolution threw — {ex.Message}");
                return;
            }

            if (_loggedResult) return;

            if (_seasonGetter != null)
            {
                _loggedResult = true;
                Undertow.Log.LogInfo(
                    "Ragnarok's Wrath detected — bridged. Storms raise the sea where they stand, " +
                    "and the season shifts the drift.");
            }
            else if (_installed)
            {
                // Present but unreadable means RW's API moved. Name the member, because that is
                // the only thing that makes this diagnosable from a log. Storms are unaffected —
                // they come from vanilla's replicated event, not from here.
                _loggedResult = true;
                Undertow.Log.LogError(
                    $"Ragnarok's Wrath is installed but {SeasonSystemType}.Current did not resolve. " +
                    "The season falls back to spring; storms are unaffected.");
            }
        }

        /// <summary>Prefix Ragnarok's Wrath gives its own RandomEvents, so we surge for its storms only.</summary>
        private const string StormEventPrefix = "ragnarokswrath_";

        /// <summary>
        /// True when a Ragnarok's Wrath storm is standing over this position.
        ///
        /// READS VANILLA'S REPLICATED EVENT, NOT RW'S OWN STATE, AND THAT IS A BUG FIX.
        /// RW exposes `WeatherSystem.IsStormAt(pos)`, which is the obvious thing to call and was
        /// what this did first. It is dead on a dedicated server: `StormActive` is assigned only
        /// inside `WeatherSystem.Tick()`, and RW's `WorldTick.Update()` opens with
        /// `if (!IsSimulationAuthority()) return;` — so on a PURE CLIENT no RW system ticks and
        /// `StormActive` stays false forever. Drift is applied by the peer that OWNS the hull,
        /// which is a client, so storm surge would have reached exactly nobody except on a listen
        /// host. Found by reading RW's source before running the test, 2026-08-28.
        ///
        /// The storm is a real vanilla `RandomEvent`, and vanilla replicates the active one to
        /// every peer through the routed `SetEvent` RPC — which is how the banner reaches players
        /// at all. So reading `RandEventSystem.GetActiveEvent()` gives the same answer on the
        /// server, a listen host and a pure client, using only public vanilla API and needing
        /// nothing of RW's to be running.
        ///
        /// RW must still be INSTALLED on the machine, because it is RW that registers the event
        /// definition the RPC resolves by name.
        ///
        /// DISTANCE IS XZ-PLANAR. The event centre sits at y = 0 while a boat floats at the water
        /// line and a player stands on terrain above it; a 3D check therefore reports things
        /// outside a radius they are plainly inside. Ragnarok's Wrath shipped that exact bug in
        /// its announcement layer and measured a player dead-centre as "77m away".
        /// </summary>
        public static bool IsStormAt(Vector3 position)
        {
            if (!TryGetStorm(out Vector3 centre, out float range)) return false;

            float dx = position.x - centre.x;
            float dz = position.z - centre.z;
            return (dx * dx + dz * dz) <= range * range;
        }

        /// <summary>
        /// The active Ragnarok's Wrath storm, from vanilla's own replicated event state.
        /// Answers false when nothing is running or the active event belongs to someone else.
        /// </summary>
        public static bool TryGetStorm(out Vector3 centre, out float range)
        {
            centre = Vector3.zero;
            range = 0f;

            try
            {
                RandEventSystem system = RandEventSystem.instance;
                if (system == null) return false;

                // GetCurrentRandomEvent, NOT GetActiveEvent, and the difference is measured.
                //
                // GetActiveEvent() returns m_activeEvent, which vanilla assigns only inside
                // `else if (m_randomEvent != null && (bool)Player.m_localPlayer)` and then only
                // when that local player is INSIDE the event area. It therefore means "is the
                // local player standing in an event", and on a DEDICATED SERVER - which has no
                // local Player at all - it is permanently null. Verified live 2026-08-28: the
                // client logged the storm and the server logged nothing, from the same code.
                //
                // GetCurrentRandomEvent() returns m_randomEvent, which the scheduler sets on the
                // server and RPC_SetEvent sets on every client (name, time and position), so it
                // answers "is a storm running, and where" on all three roles and does not care
                // where anyone is standing. Our own XZ radius check below then decides whether a
                // given POSITION is inside it - which is the question actually being asked, and
                // it now answers correctly for points no player is near.
                RandomEvent active = system.GetCurrentRandomEvent();
                if (active == null || string.IsNullOrEmpty(active.m_name)) return false;
                if (!active.m_name.StartsWith(StormEventPrefix, StringComparison.Ordinal)) return false;

                centre = active.m_pos;
                range = active.m_eventRange;
                return range > 0f;
            }
            catch { return false; }
        }

        /// <summary>
        /// Season as 0..3 (spring, summer, fall, winter), or 0 when RW is absent.
        ///
        /// RW's `Season` enum is declared `Spring = 0 .. Winter = 3`, verified in its source, and
        /// is numerically identical to the ordering <c>CurrentField</c> expects — so this is a
        /// mapping rather than a guess. The value is boxed and converted rather than cast to a
        /// named type, so Undertow never needs RW's enum at compile time.
        /// </summary>
        public static int SeasonIndex()
        {
            TryResolve();
            SeasonWasRead = false;
            if (_seasonGetter == null) return 0;
            try
            {
                object value = _seasonGetter.Invoke(null, null);
                if (value == null) return 0;
                int index = Convert.ToInt32(value);
                SeasonWasRead = true;
                return index;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Whether the LAST season read actually came from Ragnarok's Wrath.
        ///
        /// Exists because spring is index 0 and so is every failure mode — an absent bridge, an
        /// unresolved getter and a throwing invoke all answer 0. On a day-0 world, where RW
        /// genuinely reports Spring, a correct read and a total failure print the same number.
        /// This is the only thing that tells them apart, and without it "season index 0" in a log
        /// is worth nothing.
        /// </summary>
        public static bool SeasonWasRead { get; private set; }


        /// <summary>Human-readable state for the console.</summary>
        public static string Describe()
        {
            TryResolve();
            if (!WrathInstalled) return "Ragnarok's Wrath absent (dormant)";

            string storm = TryGetStorm(out Vector3 c, out float r)
                ? $"STORM running at ({c.x:0}, {c.z:0}) r{r:0}"
                : "no storm";
            return _seasonGetter != null
                ? $"Ragnarok's Wrath bridged — {storm}"
                : $"Ragnarok's Wrath present, season UNRESOLVED — {storm}";
        }
    }
}
