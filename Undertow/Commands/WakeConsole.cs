using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using RavenIron.Undertow.Config;
using RavenIron.Undertow.Core;
using RavenIron.Undertow.Patches;
using RavenIron.Undertow.Bridge;

namespace RavenIron.Undertow.Commands
{
    /// <summary>
    /// The `wake` console — a ship's wake, and the locked-decision prefix.
    ///
    /// Registered from an InitTerminal postfix. The ConsoleCommand constructor overwrites
    /// same-name entries, so the repeated call per terminal is harmless. Ragnarok's Wrath
    /// verified on a live dedicated server that this registration works headless — the server's
    /// own console is a Terminal too.
    ///
    /// AUTHORITY: `wake status` answers EVERYWHERE and deliberately reports what this machine
    /// is. On a client that is the whole point — the drift patch runs on clients, so "did the
    /// mod load here" is a question a player needs answered on their own screen.
    ///
    /// No mutations exist yet, and when they do they must self-gate: Undertow owns no store, so
    /// there is nothing a client could usefully mutate. If that ever changes, copy Ragnarok's
    /// Wrath's forward-through-ZNet.RemoteCommand pattern rather than inventing an admin gate.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    public static class Patch_Terminal_Wake
    {
        /// <summary>
        /// InitTerminal runs once per Terminal, so the confirmation below is logged only the
        /// first time. Registration itself is idempotent — the ConsoleCommand constructor
        /// assigns into Terminal.commands by name, overwriting any same-name entry.
        /// </summary>
        private static bool _confirmed;

        /// <summary>
        /// Cached reflection handle for <c>Terminal.commands</c>. Resolved once, retried until
        /// it succeeds rather than latching a failure.
        ///
        /// HOUSE RULE 5, AND THIS ONE COST A ROUND-TRIP ON 2026-08-28. `Terminal.commands` is
        /// public in the PUBLICIZED reference assembly and non-public in the real one, so
        /// `Terminal.commands.ContainsKey(...)` compiled clean and threw in-game:
        /// `FieldAccessException: Field 'Terminal:commands' is inaccessible`.
        ///
        /// Two things that run counter to intuition, both measured:
        ///
        ///   1. A try/catch around the access DOES NOT HELP. Mono resolves field access when
        ///      the method is JIT'd, not when the line executes, so the whole method threw on
        ///      entry and never reached its own catch.
        ///   2. Because the throw was on entry, the ConsoleCommand registration sharing that
        ///      method never ran either — the instrument disabled the feature it was measuring.
        ///
        /// The rule that follows: never name a publicized-only member in code, even inside a
        /// try. Go through reflection, and keep the resolution off the method that does the work.
        /// </summary>
        private static FieldInfo _commandsField;

        private static void Postfix()
        {
            Register();
            ConfirmOnce();
        }

        private static void Register()
        {
            try
            {
                new Terminal.ConsoleCommand("wake",
                    "Undertow: wake status | here | field <x> <z>",
                    Run);
            }
            catch (Exception ex)
            {
                Undertow.Log.LogWarning($"wake console: register failed: {ex.Message}");
            }
        }

        /// <summary>
        /// PROOF THE COMMAND EXISTS, not merely that the postfix ran.
        ///
        /// A headless server has no console a script can type into, so "wake status works" is
        /// otherwise unverifiable without a human at the server window. Reading the registry
        /// back distinguishes the two failures that look identical from outside: the postfix
        /// never ran, or it ran and something else overwrote the entry afterwards.
        /// </summary>
        private static void ConfirmOnce()
        {
            if (_confirmed) return;

            try
            {
                if (_commandsField == null)
                    _commandsField = AccessTools.Field(typeof(Terminal), "commands");

                if (_commandsField == null)
                {
                    // Not latched: a later terminal gets another attempt. If this persists,
                    // Valheim's console API moved and the name below is the thing to re-check.
                    Undertow.Log.LogWarning("wake console: could not resolve Terminal.commands — retrying on the next terminal.");
                    return;
                }

                var registry = _commandsField.GetValue(null) as IDictionary;
                if (registry == null)
                {
                    Undertow.Log.LogWarning("wake console: Terminal.commands resolved but was not a dictionary — retrying.");
                    return;
                }

                _confirmed = true;

                if (registry.Contains("wake"))
                    Undertow.Log.LogInfo("wake console registered — `wake status` is available on this machine.");
                else
                    Undertow.Log.LogError(
                        "wake console: registration ran but 'wake' is absent from Terminal.commands. " +
                        "Something overwrote it, or Valheim's console API moved.");
            }
            catch (Exception ex)
            {
                Undertow.Log.LogWarning($"wake console: registration check failed: {ex.Message}");
            }
        }

        private static void Run(Terminal.ConsoleEventArgs args)
        {
            try
            {
                string sub = args.Args.Length > 1 ? args.Args[1].ToLowerInvariant() : "help";
                switch (sub)
                {
                    case "status":
                        Status(args);
                        return;
                    case "here":
                        Here(args);
                        return;
                    case "field":
                        Field(args);
                        return;
                    case "drift":
                        Drift(args);
                        return;
                    default:
                        args.Context.AddString(
                            "wake status — what this machine is, and what is running on it\n" +
                            "wake here — the current under your own keel\n" +
                            "wake field <x> <z> — the current at any point, loaded or not\n" +
                            "wake drift — whether the current is actually reaching boats");
                        return;
                }
            }
            catch (Exception ex)
            {
                args.Context.AddString($"wake: failed — {ex.Message}");
            }
        }

        private static void Status(Terminal.ConsoleEventArgs args)
        {
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(256);

            sb.Append($"{Undertow.PluginName} v{Undertow.PluginVersion} — {Role()}\n");
            sb.Append($"SeaTick {(SeaTick.Online ? "online" : "waiting for ZNet")}, ");
            sb.Append($"{SeaTick.SystemCount} ambient system(s), ");
            sb.Append($"budget {ModConfig.TickBudgetMs.Value.ToString("0.##", c)}ms/frame\n");
            sb.Append($"drift {OnOff(ModConfig.EnableDrift.Value)}, ");
            sb.Append($"flotsam {OnOff(ModConfig.EnableFlotsam.Value)}, ");
            sb.Append($"swimmers {OnOff(ModConfig.EnableSwimmers.Value)}, ");
            sb.Append($"wrath bridge {OnOff(ModConfig.EnableWrathBridge.Value)}\n");
            sb.Append(WrathBridge.Describe()).Append("\n");
            sb.Append("the field is computed, readable and pushing hulls; ");
            sb.Append("flotsam and swimmers are still unbuilt.");

            args.Context.AddString(sb.ToString());
        }

        /// <summary>
        /// The current under the caller's own keel. Needs a local player, so it never works on a
        /// dedicated server — say so and point at `wake field`, rather than answering with a
        /// zero vector that reads exactly like dead calm.
        /// </summary>
        private static void Here(Terminal.ConsoleEventArgs args)
        {
            Player local = Player.m_localPlayer;
            if (local == null)
            {
                args.Context.AddString(
                    "wake: no local player on this machine (a dedicated server has none). " +
                    "Use `wake field <x> <z>`.");
                return;
            }

            Vector3 p = local.transform.position;
            Report(args, p.x, p.z);
        }

        private static void Field(Terminal.ConsoleEventArgs args)
        {
            if (args.Args.Length < 4
                || !TryParseFloat(args.Args[2], out float x)
                || !TryParseFloat(args.Args[3], out float z))
            {
                args.Context.AddString("usage: wake field <x> <z>   (world coordinates, e.g. `wake field 1500 -400`)");
                return;
            }
            Report(args, x, z);
        }

        /// <summary>
        /// The whole point of task 1: MEASURE BEFORE YOU PUSH. A drift you cannot read is a
        /// drift you cannot debug, and "the boat ended up somewhere odd" is the least
        /// diagnostic bug report in this genre. Everything here is a read — no force, no write.
        /// </summary>
        private static void Report(Terminal.ConsoleEventArgs args, float x, float z)
        {
            if (!SeaContext.TryEvaluate(x, z, out FieldSample s))
            {
                args.Context.AddString("wake: no world loaded — WorldGenerator is not up yet.");
                return;
            }

            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(220);

            sb.Append($"({x.ToString("0", c)}, {z.ToString("0", c)})  ");
            sb.Append(s.Depth <= 0f
                ? $"LAND (ground {(-s.Depth).ToString("0.#", c)}m above water)\n"
                : $"depth {s.Depth.ToString("0.#", c)}m\n");

            sb.Append($"current {s.Speed.ToString("0.###", c)} m/s");
            if (s.Speed > 0.0005f)
                sb.Append($" toward {Compass(s.X, s.Z)} ({s.X.ToString("0.###", c)}, {s.Z.ToString("0.###", c)})");
            sb.Append($" — {s.Dominant}\n");

            if (s.StormSurge > 1.001f)
                sb.Append($"STORM SURGE x{s.StormSurge.ToString("0.##", c)} — the sea is up here\n");
            sb.Append($"tide {(s.TidePhase01 * 100f).ToString("0", c)}% ({TideWord(s.TidePhase01)}), ");
            sb.Append($"season {SeasonWord(SeaContext.SeasonIndex)}");

            args.Context.AddString(sb.ToString());
        }

        /// <summary>
        /// Whether the current is actually reaching hulls, on THIS machine.
        ///
        /// Exists because the drift patch is unverifiable any other way. Boat physics runs only
        /// on the peer that owns the hull, so a dedicated server with nobody aboard never runs
        /// the patch even once — the line that proves it works can only be read by the person
        /// holding the tiller. This is that line.
        ///
        /// "pushed 0 hulls" on a machine whose player is sitting in a boat is the diagnostic:
        /// it separates "the patch never ran" from "it ran and did nothing", which look
        /// identical from the deck.
        /// </summary>
        private static void Drift(Terminal.ConsoleEventArgs args)
        {
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(256);

            sb.Append($"drift {(ModConfig.EnableDrift.Value ? "enabled" : "DISABLED in config")}, ");
            sb.Append($"strength {ModConfig.DriftStrength.Value.ToString("0.##", c)} (1.0 = drift at the water's speed), ");
            sb.Append($"unattended {ModConfig.UnattendedDriftFactor.Value.ToString("0.##", c)}, ");
            sb.Append($"refresh {ModConfig.FieldRefreshSeconds.Value.ToString("0.##", c)}s\n");

            if (!Patch_Ship_Drift.EverRan)
            {
                sb.Append("pushed 0 hulls since boot — the patch has never run on this machine. ");
                sb.Append("Expected on a dedicated server, and on any client not steering a boat: ");
                sb.Append("physics runs only on the peer that OWNS the hull.");
            }
            else
            {
                sb.Append($"pushed {Patch_Ship_Drift.PushCount} times, last '{Patch_Ship_Drift.LastShip}' — ");
                sb.Append($"water {Patch_Ship_Drift.LastWaterSpeed.ToString("0.###", c)} m/s, ");
                sb.Append($"applied {Patch_Ship_Drift.LastAppliedDv.ToString("0.#####", c)} m/s this tick");
            }

            args.Context.AddString(sb.ToString());
        }

        /// <summary>
        /// Sixteen-point compass. A bearing is what a sailor can act on; a pair of floats is
        /// what a debugger can act on. The line carries both.
        /// </summary>
        private static string Compass(float x, float z)
        {
            string[] points =
            {
                "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
                "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"
            };
            // Valheim's +z is north, +x is east.
            double deg = Math.Atan2(x, z) * (180.0 / Math.PI);
            if (deg < 0) deg += 360.0;
            int index = (int)Math.Round(deg / 22.5) % 16;
            return points[index];
        }

        /// <summary>
        /// Flood, ebb or slack — derived from the same sine the field itself uses, rather than
        /// from bands over the phase.
        ///
        /// The first version banded the phase directly and called 0.375-0.625 "slack", which
        /// mislabelled a tide running at 43% of peak as still water — spotted live on
        /// 2026-08-28 at 57%. A label that disagrees with the number beside it is worse than no
        /// label, because the reader trusts the word.
        /// </summary>
        /// <summary>
        /// Season by name, with its source. "spring (no Wrath)" and "spring (Wrath)" are
        /// different facts and must not print identically — the first means nothing is driving
        /// the season, the second means Ragnarok's Wrath really did conclude spring.
        /// </summary>
        private static string SeasonWord(int index)
        {
            string[] names = { "spring", "summer", "fall", "winter" };
            int i = index % 4;
            if (i < 0) i += 4;
            return names[i] + (WrathBridge.Available ? " (Wrath)" : " (no Wrath — fixed)");
        }

        private static string TideWord(float phase01)
        {
            double s = Math.Sin(phase01 * 2.0 * Math.PI);
            if (Math.Abs(s) < 0.25) return "slack";
            return s > 0 ? "flooding" : "ebbing";
        }

        private static bool TryParseFloat(string text, out float value)
            => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        /// <summary>
        /// Names the three roles apart rather than collapsing them into a bool. A listen host is
        /// both authority and renderer, and a bug that only appears on one of the three is the
        /// kind this line exists to make obvious at a glance.
        /// </summary>
        private static string Role()
        {
            if (ZNet.instance == null) return "no world loaded";
            if (Undertow.IsDedicated()) return "dedicated server";
            return Undertow.IsSimulationAuthority() ? "listen host (authority + client)" : "client";
        }

        private static string OnOff(bool value) => value ? "on" : "off";
    }
}
