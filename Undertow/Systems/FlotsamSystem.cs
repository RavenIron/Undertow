using System;
using System.Collections.Generic;
using UnityEngine;
using RavenIron.Undertow.Bridge;
using RavenIron.Undertow.Config;
using RavenIron.Undertow.Core;

namespace RavenIron.Undertow.Systems
{
    /// <summary>
    /// The sea collects. Driftwood, spilled cargo and what the drowned no longer need gather in
    /// slack water, which gives a sailor a second reason to know where the current goes dead.
    ///
    /// THE FIRST AMBIENT SYSTEM, and the first thing registered with <see cref="SeaTick"/>.
    /// Everything before it was either a pure function or a Harmony patch on the machine that
    /// owns a hull; this is server-authoritative work on a budgeted cursor.
    ///
    /// NO NEW PREFABS, EVER. An unresolvable prefab hash sends `ZNetScene.CreateObjectsSorted`
    /// into `DestroyZDO` — silent data loss. Flotsam is vanilla `ItemDrop`s and nothing else.
    /// **123 of 1090 item prefabs carry `Floating`, measured on a live server 2026-08-28**, so
    /// there is plenty to choose from — and the choice is a FLAVOUR one, because `Floating` turns
    /// out to be loss-prevention rather than buoyancy (an iron tower shield floats; ore does not).
    ///
    /// FINDING PLAYERS HEADLESS. `Player.GetAllPlayers()` returns ZERO instances on a dedicated
    /// server even with people online — measured in Ragnarok's Wrath, and the reason its whole
    /// announcement layer was undeliverable for months. This reads `ZNet.GetPeers()` and prefers
    /// each peer's character ZDO position, falling back to `m_refPos`.
    ///
    /// NOTHING ACCUMULATES IN EMPTY OCEAN. Spawning requires a peer within range, so an idle
    /// server produces nothing at all and a long-running world does not silently fill its ZDO
    /// table with driftwood.
    /// </summary>
    public class FlotsamSystem : IWorldSystem
    {
        public string Name => "FlotsamSystem";
        public bool Enabled => ModConfig.EnableFlotsam.Value;
        public float IntervalSeconds => ModConfig.FlotsamIntervalSeconds.Value;

        private string[] _common;
        private string[] _rare;
        private string[] _wreck;

        private bool _warnedMissingPrefab;

        /// <summary>
        /// What we spawned, so it can be capped and reclaimed.
        ///
        /// TRACKED BY ZDOID, NOT BY GameObject, AND THAT IS A BUG FIX. The first version held
        /// GameObject references and every entry vanished within a tick: eight spawns in a live
        /// session all logged `[1/12 alive]`, because a dedicated server UNLOADS the instance
        /// once a nearby client takes it over while the item lives on as a ZDO. Holding the
        /// instance therefore lost the item immediately, which quietly disabled BOTH the cap and
        /// the TTL — the whole safety valve — while leaving the log looking healthy. Measured
        /// 2026-08-28. A ZDOID survives the instance and is what actually identifies the thing.
        ///
        /// IN MEMORY ONLY, deliberately: Undertow owns no store by locked decision, and adding
        /// one for driftwood would be a poor trade. Flotsam alive at a restart is forgotten and
        /// never reclaimed, so at most `MaxAlive` items per session outlive us.
        /// </summary>
        private readonly List<Tracked> _alive = new List<Tracked>();

        private struct Tracked
        {
            public ZDOID Id;
            public float SpawnedAt;
        }

        public void Initialise()
        {
            _common = Split(ModConfig.FlotsamCommon.Value);
            _rare = Split(ModConfig.FlotsamRare.Value);
            _wreck = Split(ModConfig.FlotsamWreckage.Value);

            Undertow.Log.LogInfo(
                $"[{Name}] {ModConfig.FlotsamPerHour.Value:0.##}/hour per player in slack water, " +
                $"{ModConfig.FlotsamRingMinMeters.Value:0}-{ModConfig.FlotsamRingMaxMeters.Value:0}m out, " +
                $"cap {ModConfig.FlotsamMaxAlive.Value}, TTL {ModConfig.FlotsamTtlSeconds.Value:0}s. " +
                $"Palette: {_common.Length} common, {_rare.Length} rare, {_wreck.Length} wreckage.");
        }

        public void Tick(float deltaSeconds)
        {
            Prune();

            ZNet net = ZNet.instance;
            if (net == null) return;

            List<ZNetPeer> peers = net.GetPeers();
            if (peers == null || peers.Count == 0) return;          // empty ocean stays empty

            if (_alive.Count >= ModConfig.FlotsamMaxAlive.Value) return;

            float maxSpeed = ModConfig.MaxCurrentSpeed.Value;
            float minDepth = ModConfig.FlotsamMinDepth.Value;
            float perHour = ModConfig.FlotsamPerHour.Value;

            for (int i = 0; i < peers.Count; i++)
            {
                if (_alive.Count >= ModConfig.FlotsamMaxAlive.Value) return;

                if (!TryGetPeerPosition(peers[i], out Vector3 origin)) continue;

                // One candidate point per player per tick. Deliberately not a search: a sweep for
                // the slackest water nearby would cost a field evaluation per sample and put the
                // whole thing on the budget for no gain. One roll, one point, most of them fail.
                Vector3 point = RingPoint(origin,
                    ModConfig.FlotsamRingMinMeters.Value,
                    ModConfig.FlotsamRingMaxMeters.Value);

                if (!SeaContext.TryEvaluate(point.x, point.z, out FieldSample sample)) continue;

                float weight = FlotsamMath.GatherWeight(sample, maxSpeed, minDepth);
                if (weight <= 0f) continue;

                if (!FlotsamMath.ShouldSpawn(weight, perHour, deltaSeconds, UnityEngine.Random.value))
                    continue;

                Spawn(point, sample);
            }
        }

        /// <summary>
        /// A peer's position, headless-safe. The character ZDO is authoritative; `m_refPos` is
        /// the server's own idea of where the peer is and is a sound fallback while a character
        /// is loading or between deaths.
        /// </summary>
        private static bool TryGetPeerPosition(ZNetPeer peer, out Vector3 position)
        {
            position = Vector3.zero;
            if (peer == null) return false;

            try
            {
                if (peer.m_characterID != ZDOID.None && ZDOMan.instance != null)
                {
                    ZDO zdo = ZDOMan.instance.GetZDO(peer.m_characterID);
                    if (zdo != null)
                    {
                        position = zdo.GetPosition();
                        return true;
                    }
                }

                if (peer.m_refPos != Vector3.zero)
                {
                    position = peer.m_refPos;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static Vector3 RingPoint(Vector3 origin, float min, float max)
        {
            if (max < min) max = min;
            float angle = UnityEngine.Random.value * Mathf.PI * 2f;
            float radius = Mathf.Lerp(min, max, UnityEngine.Random.value);
            return new Vector3(
                origin.x + Mathf.Cos(angle) * radius,
                0f,
                origin.z + Mathf.Sin(angle) * radius);
        }

        private void Spawn(Vector3 point, in FieldSample sample)
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) return;

            // A storm standing over this water changes what washes up. This is where the sea
            // gets a voice without a line of UI.
            bool stormy = WrathBridge.IsStormAt(point);
            string[] table = stormy && _wreck.Length > 0 ? _wreck : _common;

            if (UnityEngine.Random.value < ModConfig.FlotsamRareChance.Value && _rare.Length > 0)
                table = _rare;

            if (table.Length == 0) return;
            string prefabName = table[UnityEngine.Random.Range(0, table.Length)];

            GameObject prefab = scene.GetPrefab(prefabName);
            if (prefab == null)
            {
                // Named, once, because a typo in a config palette is otherwise a system that
                // silently does nothing forever.
                if (!_warnedMissingPrefab)
                {
                    _warnedMissingPrefab = true;
                    Undertow.Log.LogWarning(
                        $"[{Name}] prefab '{prefabName}' not found — check the palette in config. " +
                        "Use `wake floats` for the list of prefabs that actually float.");
                }
                return;
            }

            try
            {
                var pos = new Vector3(point.x, SeaContext.WaterLevel, point.z);
                GameObject go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
                if (go == null) return;

                // Record the ZDOID, not the instance — the instance is unloaded out from under us
                // as soon as a client takes the item over. See the _alive summary.
                ZNetView nview = go.GetComponent<ZNetView>();
                ZDO zdo = nview != null ? nview.GetZDO() : null;
                if (zdo == null)
                {
                    // No networked identity means we could never reclaim it, so refuse to leave it
                    // behind at all rather than losing count of it.
                    UnityEngine.Object.Destroy(go);
                    if (!_warnedMissingPrefab)
                    {
                        _warnedMissingPrefab = true;
                        Undertow.Log.LogWarning(
                            $"[{Name}] '{prefabName}' spawned without a ZNetView/ZDO — cannot be capped " +
                            "or reclaimed, so it was removed. Drop it from the palette.");
                    }
                    return;
                }

                _alive.Add(new Tracked { Id = zdo.m_uid, SpawnedAt = Time.realtimeSinceStartup });

                if (ModConfig.VerboseLogging.Value)
                    Undertow.Log.LogInfo(
                        $"[{Name}] {prefabName} at ({point.x:0}, {point.z:0}) — " +
                        $"depth {sample.Depth:0.#}m, water {sample.Speed:0.###} m/s {sample.Dominant}" +
                        (stormy ? ", STORM wreckage" : "") +
                        $" [{_alive.Count}/{ModConfig.FlotsamMaxAlive.Value} alive]");
            }
            catch (Exception ex)
            {
                Undertow.Log.LogWarning($"[{Name}] could not spawn '{prefabName}': {ex.Message}");
            }
        }

        /// <summary>
        /// Drop entries whose item is gone (picked up or destroyed) and reclaim anything past its
        /// time.
        ///
        /// Reclaiming matters more than it looks: without it the cap becomes a permanent ceiling
        /// of abandoned driftwood rather than a rolling one. And the lookup MUST go through
        /// ZDOMan rather than through a held instance — a dedicated server unloads the instance
        /// while the ZDO lives on, so an instance-based check reports everything as gone and the
        /// cap never binds. That is exactly what happened on 2026-08-28.
        /// </summary>
        private void Prune()
        {
            ZDOMan man = ZDOMan.instance;
            if (man == null) return;

            float now = Time.realtimeSinceStartup;
            float ttl = ModConfig.FlotsamTtlSeconds.Value;

            for (int i = _alive.Count - 1; i >= 0; i--)
            {
                Tracked t = _alive[i];

                ZDO zdo;
                try { zdo = man.GetZDO(t.Id); }
                catch { zdo = null; }

                // Gone: somebody picked it up, or the world removed it. Stop counting it.
                if (zdo == null || !zdo.IsValid())
                {
                    _alive.RemoveAt(i);
                    continue;
                }

                if (ttl > 0f && now - t.SpawnedAt > ttl)
                {
                    try
                    {
                        // Take ownership before destroying: only the owner may remove a ZDO, and
                        // a client may have taken this one over when it came into range.
                        if (!zdo.IsOwner()) zdo.SetOwner(ZDOMan.GetSessionID());
                        man.DestroyZDO(zdo);
                    }
                    catch (Exception ex)
                    {
                        Undertow.Log.LogWarning($"[{Name}] could not reclaim flotsam: {ex.Message}");
                    }

                    _alive.RemoveAt(i);
                }
            }
        }

        private static string[] Split(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return new string[0];
            string[] parts = csv.Split(',');
            var list = new List<string>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string s = parts[i].Trim();
                if (s.Length > 0) list.Add(s);
            }
            return list.ToArray();
        }
    }
}
