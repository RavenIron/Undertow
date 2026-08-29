# Undertow

A Valheim mod by **Raven Iron**. The sea gets its own motion. Currents run across the ocean
in a learnable shape — a basin drift, coastal set, fast water between islands, slack behind a
headland — and they push what floats on them. On a world with no map and no portals, that
turns coordinates into seamarks: knowledge a crew carries in their heads, never on the screen.

**Undertow owns water motion, and nothing else.** Not weather, not waves, not the water
surface, not wayfinding instruments. If a task seems to call for a map, a compass, a wind
gauge, or a second wave system, that is a signal to re-read the locked decisions below, not to
build one.

Design document (the reasoning behind every decision here):
<https://claude.ai/code/artifact/e213f36d-fdcd-4695-a159-f8e4e1157323>

---

## Status

**THE ROADMAP IS BUILT.** Tasks 0–5, harness **162/162**, every assertion proven to fail without
its fix. Verified in-game on a dedicated server and a client (2026-08-28).

- **0 — skeleton.** Loads with `dedicated=True`; the `wake` console is registered, confirmed by
  reading `Terminal.commands` back rather than assuming.
- **1 — `CurrentField`.** Evaluates against real `WorldGenerator` terrain, and the server and a
  client independently produce a byte-identical transect: the no-sync architecture demonstrated
  across two machines rather than asserted.
- **2 — drift.** A karve and a longship drifting in the same water both settle near the water's
  own speed along the current (0.86 and 0.96 against a target of 1.0) — the saturation model's
  whole claim, since those two hulls have different damping constants.
- **3 — the Wrath bridge.** Both states proven by parking and unparking RW between boots, and a
  live storm measured on the CLIENT: `STORM at (8101, 368) — IsStormAt(centre)=True, surge x1.6
  | at centre 0.38 m/s | 800m away 0.266 m/s surge x1`, against RW's own
  `storm started at (8101, 368)`. Same coordinates, two mods, two machines.

- **4 — flotsam.** `123 of 1090` item prefabs carry `Floating`, measured headless — the question
  that gated the whole task, and unanswerable by decompile. The system is capped, TTL-reclaimed
  and spawns only near a peer, so an empty ocean stays empty (verified over 45s).
- **5 — swimmers.** `Character.UpdateSwimming` patched, owner-gated, players only. The drowning
  guard is swept across the entire legal config range in the harness.

**KNOWN LIMIT: RW's season is client-blind.** `SeasonSystem.Current` is set only in `Tick()`,
which RW gates on the simulation authority, so every client computes the field as spring. Boats
do not desync (all clients agree) but the seasonal shift is inert away from a listen host. The
fix is RW syncing its season, NOT a second season clock here — see rule 4.

**The model took three attempts and every one was killed by a measurement, not by review.** The
reasoning is in `Core/DriftForce.cs`; read it before touching the force. Three separate
INSTRUMENT failures cost more round-trips than the bugs did — see `docs/BACKLOG.md` task 2.

Every engine fact in *Known traps* below was read out of the shipping assembly with `ilspycmd`
on 2026-08-28. The one open question that blocks a whole system — which vanilla prefabs carry
`Floating` — is marked as unverified everywhere it appears, and must stay that way until
someone has watched it in-game.

---

## Commands

Ported verbatim from `..\RagnaroksWrath\tools\` — they are debugged, and a second dialect of
the same script is a liability.

```powershell
.\tools\fetch-libs.ps1     # once per machine: copies game/BepInEx DLLs into libs\
.\tools\run-tests.ps1      # off-game logic tests (net10) — run before every commit
```

```powershell
dotnet build .\Undertow\Undertow.csproj -c Debug
```

`tools\package.ps1` is **deliberately not ported yet**. RW's version refuses to package unless
the plugin const, the csproj `<Version>` and `manifest.json` all agree — a guard worth having,
and worth having on a real release rather than on a skeleton. It arrives with the first
publishable build, along with `manifest.json`, `README.md`, `CHANGELOG.md` and an icon.

To inspect a game member — signature, accessibility, default values, or the actual method
body — decompile it. `dotnet tool install -g ilspycmd`, then:

```powershell
ilspycmd -r libs libs\assembly_valheim_publicized.dll -t Ship
```

**Read the body. Do not infer it from the shape of the output.** Every fact in this file that
turned out to matter contradicted a plausible guess: waves already respond to wind, tacking
already exists, and ships and swimmers need opposite injection mechanisms.

---

## Layout

Planned. Mirrors Ragnarok's Wrath, because a reader who knows one should be able to read the
other without relearning anything.

```
Undertow/                  one role-aware plugin (net472)
  Config/ModConfig.cs      config surface; every system has an on/off toggle
  Core/CurrentField.cs     the maths. PURE — no Unity, no config, no game types, no clock
  Core/SeaContext.cs       the seam: WorldGenerator/ZNet/ZoneSystem reads + the terrain probe
  Core/SeaTick.cs          the single time-budgeted cursor
  Core/IWorldSystem.cs     what ambient systems implement
  Systems/                 Drift (ships), Flotsam, Swimmers
  Bridge/WrathBridge.cs    reflected, read-only reads of Ragnarok's Wrath
  Patches/                 Harmony patches — Ship, Character
  Commands/                the `wake` console
tests/CoreTests/           net10 harness; compiles the REAL source against stubs
tools/                     scripts, ported from RagnaroksWrath
libs/                      gitignored; populated by fetch-libs.ps1
docs/BACKLOG.md            what to build next and in what order
```

Reference assemblies are **publicized** (`assembly_valheim_publicized.dll`) and resolved
through a relative `libs\` path, never a hardcoded Steam path — this repo gets cloned.

---

## House style — non-negotiable

**These rules were earned in FireFront and Ragnarok's Wrath, not here.** Each one cost a
measured production failure in a sibling project. Inherit them; do not re-derive them, and do
not write them up as though Undertow measured them. Rule 6 is the only one this project adds,
and it is a prior from a decompile rather than a scar.

1. **Harmony: prefixes for behaviour** (`Priority.Low`, honour `__runOriginal`, "no opinion"
   means `return true`) — and result-decorating postfixes at default priority where appending
   to a return value is the whole point. A decorating postfix never replaces logic and cedes
   every fight: whoever rewrites the value outright wins, and we decorate what survives. The
   max-priority-prefix-replace pattern is formally retracted — it cost ~50% of a sibling mod's
   entire patch-layer CPU, and `int.MaxValue` defeats every other mod's ordering including
   explicit `HarmonyBefore`. If one specific third-party mod ever forces the issue, put it
   behind a default-off toggle, never in the default path.

2. **No long-lived coroutines. Use a time-budgeted cursor driven from a single `Update`.**
   Every long-lived coroutine in this codebase's lineage independently grew the same bug: a
   `while (true)` whose body can `continue` past its only `yield`, hard-locking the game. It
   reached production once. `SeaTick` is the cursor; systems own no timers of their own.

3. **Keep cosmetics off the gameplay path.** A VFX call that throws inside a shared prefix
   aborts everything downstream. Visual work goes in its own try/catch with a `finally` that
   advances whatever state it owns.

4. **Never patch `EnvMan`, `WaterVolume`, materials, textures, or shaders.** Seasonality
   (RustyMods) and Seasons (shudnal) own environment selection; vanilla owns the water
   surface, and it owns it *twice* — `WaterVolume.CalcWave` feeds CPU buoyancy while the same
   global wind feeds the GPU shader. Change one and the boat floats on water the player cannot
   see. Undertow consumes weather and wave state as **read-only gameplay input** and never
   drives visuals with it.

5. **Publicized assemblies are COMPILE-TIME ONLY.** At runtime the game loads the real
   assembly with original accessibility, and Mono refuses private access. **The build is clean
   and the failure appears only in-game.** Reach private members through a cached
   `AccessTools.MethodDelegate` / `AccessTools.FieldRefAccess` / `AccessTools.Field`, resolved
   once and stored in a static. Keep retrying resolution rather than latching a failure; if the
   member is genuinely absent, log an error naming it, because that means Valheim's API moved.

   **Undertow hit this on its first day, 2026-08-28, and it behaved worse than the rule
   implies.** `Terminal.commands` is public in the publicized reference assembly and non-public
   in the real one; `Terminal.commands.ContainsKey("wake")` compiled with zero warnings and
   threw `FieldAccessException: Field 'Terminal:commands' is inaccessible` in-game. Two
   consequences worth knowing before you trust a `try`:

   - **A try/catch around the access does not help.** Mono resolves field access when the
     method is JIT'd, not when the line runs, so the whole method threw on entry and never
     reached its own catch. Harmony logged it; our handler never saw it.
   - **Everything else in that method died too.** The `ConsoleCommand` registration sat three
     lines above the offending read and never executed, so the console silently vanished — an
     instrument that disabled the feature it was measuring.

   So the rule is stronger than "wrap it": **never name a publicized-only member in code at
   all**, and keep the reflection resolution in a different method from the work.

   **In a Harmony patch, prefer field INJECTION to reflection.** `Ship.m_nview`, `m_body` and
   `m_players` are all private in the shipping assembly, and the drift postfix runs fifty times
   a second per boat — the worst possible place for a per-call reflection lookup or a
   FieldAccessException. Declaring `___m_nview`, `___m_body`, `___m_players` parameters makes
   Harmony generate the accessors, so the patch body contains no field reference at all and
   there is nothing to cache. A renamed field then fails loudly at PATCH time rather than
   silently at call time, which is the failure mode you want.

6. **Never assign `linearVelocity`, and never move anything you do not own.** This mod's
   entire write surface is force on a live `Rigidbody`, on the machine that owns it. Vanilla
   assigns `m_body.linearVelocity` wholesale inside the same tick we run in, so an assignment
   is either overwritten or eats vanilla's damping. And setting a ZDO's position does not move
   an object — it is a suggestion the owning machine overwrites next frame.

**Debugging discipline.** A silent success and a silent no-op are indistinguishable from
outside the game. When something "doesn't work", spend the round-trip on **one log line
proving the code ran at all** before spending it on another guess. When a symptom survives
several confident fixes, stop fixing and audit the instrument — a confident, well-formed,
wrong measurement is the most common cause of a long debugging session here.

**Measure before you push.** Undertow's specific version of the above: `CurrentField` must be
observable through the console (`wake here`) before one newton reaches a boat. A drift you
cannot read is a drift you cannot debug, and "the boat ended up somewhere odd" is the least
diagnostic bug report in this genre.

---

## Locked decisions — do not revisit without asking

---

## Compatibility constraints

**Ragnarok's Wrath (Raven Iron)** — our own world simulation, and the only mod Undertow reads.
Three reflected surfaces, all soft; absence logs once and disables the feature, never errors:
`WeatherSystem.StormActive` / `StormCentre` (storm surge), `WindSystem.IntensityAt(Vector3)`
(RW's positional gameplay wind, already public and already computed per tick), and the season.
Reading RW's wind rather than `EnvMan` keeps rule 4 intact by construction — Undertow never
touches the environment at all.

✅ **VERIFIED 2026-08-28 (0.3.2)** on a real dedicated server, by parking and unparking RW
between boots: absent logs the dormant line and sails on; present resolves both members and
reads a real season. `Season` is `Spring = 0 .. Winter = 3` in RW's source, numerically
identical to `CurrentField`'s ordering, so the cast is a mapping rather than a guess.

⚠️ **One link remains unverified: that `IsStormAt` returns true with a storm overhead.** It
cannot be checked headless — **RW storms cannot fire on an empty server**, confirmed in RW's
source: the event carries `m_pauseIfNoPlayerInArea = true` and its position is chosen from
"somewhere a player actually is". See `docs/BACKLOG.md` task 3 for the five-minute protocol.

**RW must be on the CLIENT for surge to reach a boat.** Drift is computed by the peer that owns
the hull, so a client without RW computes no surge whatever the server believes.

**Seasonality (RustyMods) / Seasons (shudnal)** — no contact. Undertow never selects an
environment, never reads a season directly, and never touches a material. Where season matters
it arrives through RW, which already handles the either/or between those two mods.

Other sailing mods are unassessed. Any mod that patches `Ship`, adds force to a hull, or
rewrites `GetSailForce` deserves the same five-minute decompile before it is assumed harmless.

**Boat stat mods** (cargo, speed, durability) should compose without contact: they change the
hull, Undertow changes the water.

**AwayFromHome (Wubarrk)** — no known interaction, since nothing here ticks on zone load state
and nothing spawns in unloaded ocean. Keep it that way: flotsam requires a real player nearby.

---

## Known traps

Verified by decompile 2026-08-28 unless marked otherwise.

- **`Ship.CustomFixedUpdate`'s owner check is INSIDE the method.**
  `if ((bool)m_nview && !m_nview.IsOwner()) return;` guards only the lines below it. **A
  Harmony postfix still runs on every client**, so a naive postfix has every peer pushing the
  same hull. Re-check `IsOwner()` in the postfix itself. This is the single easiest thing to
  get wrong in the whole mod.

- **Ships and swimmers need OPPOSITE mechanisms.** A ship is a damped rigidbody and vanilla
  adds forces to it, so `AddForce` works. A swimming `Character` is **servo-controlled**:
  `Character.UpdateSwimming` computes `force = m_currentVel - m_body.linearVelocity`, zeroes
  `force.y`, clamps it to 20, and applies it as `ForceMode.VelocityChange` every frame — so an
  external `AddForce` on a swimmer is **cancelled on the next tick**. Write to `m_currentVel`
  instead — but read the two traps below first, because the obvious way to do it is wrong twice
  over.

- 🚫 **`Character.AddPushbackForce` is a SHOVE, not a nudge. This entry previously recommended
  it and that advice was WRONG** — corrected 2026-08-28 by reading the body before shipping it.
  It looks like vanilla's sanctioned "fold an external push into the velocity target" helper.
  In fact it ignores `m_pushForce`'s MAGNITUDE entirely and drives velocity to a flat **20 m/s**
  along its direction — `velocity += normalized * (20f - num)` — halved to 10 while swimming. It
  exists to eject a body from a creature it is clipping through. A 0.3 m/s current routed
  through it would launch a swimmer at five times swim speed.

- **Adding to `Character.m_currentVel` is amplified by `1 / m_swimAcceleration`.** Vanilla lerps
  that target toward the swimmer's intent every frame, so a per-frame addition `d` settles at
  `d / m_swimAcceleration` — and vanilla's value is **0.05**, a twentyfold amplification. Scale
  by the acceleration first, or a 1 m/s current drags a swimmer at 20 m/s. `m_swimSpeed` and
  `m_swimAcceleration` are public; `m_currentVel`, `m_nview` and `UpdateSwimming` are not.

- **Copy vanilla's force convention from the method you are patching.**
  `CustomFixedUpdate` uses `AddForceAtPosition(v * m_body.mass, pos, ForceMode.Impulse)` with
  `num3 = fixedDeltaTime * 50f` folded in. Matching units matters more than being "correct" in
  isolation — a force in different units is a tuning value nobody can reason about.

- **`RandEventSystem.GetActiveEvent()` is ALWAYS NULL on a dedicated server, and is not the
  accessor you want.** Vanilla assigns `m_activeEvent` only inside
  `else if (m_randomEvent != null && (bool)Player.m_localPlayer)`, and then only when that local
  player is INSIDE the event area - so it means "is the local player standing in an event", and
  a headless server has no local Player at all. Measured 2026-08-28: identical code logged the
  storm on the client and nothing on the server. Use **`GetCurrentRandomEvent()`** - it returns
  `m_randomEvent`, set by the scheduler on the server and by `RPC_SetEvent` on every client
  (name, time, position), so it answers on all three roles regardless of where anyone stands.
  `RandomEvent.m_name`, `m_pos` and `m_eventRange` are all public.

- **Another mod's ticked state is usually dead on clients; vanilla's replicated state is not.**
  Ragnarok's Wrath's `WeatherSystem.StormActive` is assigned only in `Tick()`, and its
  `WorldTick` returns early when the process is not the simulation authority - so on a pure
  client it is false forever. Anything computed on the peer that OWNS a hull (i.e. everything
  Undertow does to a boat) must therefore not depend on another mod's ticked state. Reach for
  the replicated vanilla fact underneath it instead. Storm surge was rewritten for exactly this
  before it ever shipped; RW's SEASON has the same problem and no such escape, and is documented
  as a known limit in `Bridge/WrathBridge`.

- **Vanilla's hull damping already assumes still water, so a current must be a PUSH and never a
  DRAG.** `m_damping`, `m_dampingForward` and `m_dampingSideway` in `Ship.CustomFixedUpdate` are
  computed against the hull's ABSOLUTE velocity. Adding a second drag term toward the water —
  the obvious `force ∝ (water − hull)` model — double-counts it, and only shows up under sail: a
  karve making 6 m/s in 0.3 m/s water would receive `0.6 × (0.3 − 6) ≈ −3.4 m/s²`, i.e. the sea
  as a brake on every boat under way. `DriftForce.Compute` therefore takes the water velocity
  and NOT the hull's, so the bug is unrepresentable rather than merely tested against. Caught by
  reasoning about the sailing case on 2026-08-28, after the wrong model was written AND its
  tests written to match it — a green harness agreeing with a wrong premise.

- **Valheim's open ocean has a FLAT floor at generator height exactly 0** — a uniform 30m
  depth against the water level, measured by transect on a live server 2026-08-28
  (`x=8000 h=0`, `x=9000 h=0`, while land points along the same line read 79.34, 30.47, 16.71,
  83.53…). Two consequences: **depth is a clean shore-proximity proxy**, which is what the
  coastal term is built on, and **any "shelf" threshold must sit below 30** or it silently
  classifies the entire sea as shelf. `FieldSettings.ShelfDepth` was 40 for exactly one
  afternoon because of this; a test now pins it under 30.
  `WorldGenerator.GetHeightMultiplier()` returns **200**, so generator heights are a base
  height scaled by 200 — worth knowing before comparing any raw number to a world coordinate.

- **Waves are shared, deterministic and shader-coupled.** `WaterVolume.CalcWave` sums ten
  trochoidal octaves scaled by `Mathf.Lerp(0f, wind.w, depth)` — wind intensity attenuated by
  depth — from wrapped day-time. CPU buoyancy and the GPU water shader read the same global
  wind, so they agree without syncing. Touch it and they stop agreeing.

- **Wind is global, uniform, and already deterministic.** `EnvMan.GetWindDir()` and
  `GetWindIntensity()` take no position; both come from time-seeded RNG octaves, which is why
  every client agrees with no network traffic. Do not make wind positional — RW already
  publishes a positional *gameplay* wind for exactly this, and two mods with different ideas
  about wind is the conflict rule 4 exists to prevent.

- **Vanilla already adds a positional force to ships**, at the world edge:
  `Ship.ApplyEdgeForce` pushes a hull back inside 10420m, ramping to 10500m. It is precedent
  for the technique — and a region where Undertow must not fight it. Fade current out past
  10400m.

- **Empty boats are already handled.** With `m_players.Count == 0`, vanilla forces
  `Speed.Stop` and multiplies horizontal velocity by 0.1 each tick. Respect that intent.

- **`m_sailForce` is dead outside `GetSailForce` and a gizmo.** It does not drive the sail
  mesh. So decorating `GetSailForce`'s return value is visually safe — worth knowing if wind
  ever needs a thumb on it, though nothing in the current design does that.

- **Rough water already damages hulls.** `Ship.UpdateWaterForce` deals 10 blunt when depth
  changes faster than 2.5 m/s, at most once per 2s. Anything that moves boats inherits this
  consequence for free — and could amplify it by accident.

- ⚠️ **UNVERIFIED — which vanilla item prefabs carry `Floating`?** Blocks flotsam entirely.
  One raft of sunken loot disproves the approach. Answer it in-game before writing a spawner.

- **A mod adding a prefab MUST ship server-side** or `ZNetScene.CreateObjectsSorted` calls
  `DestroyZDO` on any hash it cannot resolve — silent data loss. We add no prefabs.

- **Valheim never releases ownership of a persistent ZDO when its owner disconnects.**
  Inherited from the lineage; relevant the moment flotsam exists.

- **Always use `InvariantCulture` for anything written to or parsed from disk.** A
  comma-decimal locale otherwise produces files that work locally and corrupt on a European
  server owner's machine. Undertow writes no save file today; config is still text.

- `ZRoutedRpc.Register` tops out at **6** type parameters, `ZNetView.Register` at **4**.
  (Inherited from Ragnarok's Wrath; not re-verified here.)

---

## Working agreement

- **Run `.\tools\run-tests.ps1` before every commit** once it exists. `CurrentField` is pure
  math and is exactly the kind of logic that fails silently.
- **The harness compiles the *shipping* source, not a copy.** A harness that duplicates logic
  proves nothing and drifts.
- **Prove a new test fails without its fix.** One revert-and-rerun turns a confident guess
  into a fact.
- **A clean build proves nothing about member access.** Anything reaching into game internals
  needs one in-game run before it is done.
- **Definition of done for every task:** tests green, project builds, and anything touching
  game internals has been run in-game once with its log line observed.
- **Verify game APIs by decompiling rather than assuming.** Three of this design's original
  premises were wrong, and all three were caught this way before any code existed.
- **Ask before changing anything in the locked-decisions table.** Those were deliberate calls.

