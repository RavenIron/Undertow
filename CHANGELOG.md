# Changelog

## 0.5.1

First release. The sea gets its own motion.

- **Currents across the whole ocean**, in a shape you can learn: a slow basin drift, a stream
  that follows the coast, fast water between close islands, and dead water behind a headland.
  Built from a stream function, so the flow is divergence-free the way real water is — gyres,
  races and slack all come out of one mechanism instead of being placed by hand.
- **The same on every machine with no network traffic.** The field is a pure function of the
  world seed, position, world clock and season. Verified by a server and a client independently
  producing identical readings for the same points.
- **Tides.** A slow flood and ebb that swings how hard the open ocean runs and reverses the
  coastal stream, so a passage you know is a different passage later.
- **Boats are carried, never braked.** A drifting hull settles at the water's own speed, and a
  raft, a karve and a longship all agree — the push fades as a hull takes up the water's speed
  rather than being calibrated against damping constants that differ per boat. Unattended boats
  are not touched by default.
- **Flotsam.** Driftwood and cargo gather in slack water, with wreckage instead while a storm is
  overhead. Vanilla items only, capped, reclaimed on a timer, and spawned only near a real
  player, so an empty ocean stays empty.
- **Swimmers** are carried gently, and hard-capped below swim speed: you can always out-swim the
  water and reach shore, at every setting the config permits.
- **Storm surge with Ragnarok's Wrath.** Where a Devastating Storm stands, the water rises —
  there and nowhere else. Optional: without RW the bridge logs its absence once and the sea runs
  regardless.
- **The `wake` console** — `status`, `here`, `field`, `drift`, `floats`.

Requires the mod on the server **and every client**: boat physics runs on whichever machine owns
the hull, and that is a player's.
