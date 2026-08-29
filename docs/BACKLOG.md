# Undertow — backlog

Ordered. Each task lists its acceptance criteria. Read `CLAUDE.md` first — the house style
rules and locked decisions there constrain every task below.

**Definition of done for every task:** `.\tools\run-tests.ps1` green, project builds, and
anything touching game internals has been run in-game once with its log line observed.

The design is at <https://claude.ai/code/artifact/e213f36d-fdcd-4695-a159-f8e4e1157323>.

---

## 0. Skeleton — plugin, config, cursor, console — DONE 2026-08-28 (0.1.0)

Verified on a real dedicated server (world `UndertowSmoke`, alongside the full Ravenrest mod
set), three lines, no errors:

```
[Info   :   BepInEx] Loading [Undertow 0.1.0]
[Info   :  Undertow] wake console registered — `wake status` is available on this machine.
[Info   :  Undertow] SeaTick online — 0 system(s), budget 2ms/frame, authority=True, dedicated=True
```

`dedicated=True` is the fact no client run can establish — `ZNet.IsDedicated()` is a
compile-time constant, false in the client assembly.

Shipped: `tools\fetch-libs.ps1` and `tools\run-tests.ps1` ported verbatim from RW,
`Undertow.csproj` (net472, relative `libs\`), `Undertow.slnx`, `Plugin.cs`,
`Config/ModConfig.cs`, `Core/IWorldSystem.cs`, `Core/SeaTick.cs`,
`Commands/WakeConsole.cs`, and the net10 harness at **16/16**.

**Three deliberate departures from Ragnarok's Wrath, each with a reason:**

1. **`SeaTick` announces itself even with zero systems registered.** RW's `WorldTick` returns
   early on an empty list, which would have made this very task unverifiable — a skeleton with
   nothing to tick would have booted in complete silence, and a silent success is
   indistinguishable from a silent no-op from outside the game.
2. **`SeaTick` logs on a pure client too**, at Info, saying ambient systems idle there by
   design. A client is a correct place for this mod to live: it owns the boats it sails.
3. **No persistence and no autosave.** RW's `WorldTick` carries both because it owns stores.
   Undertow owns none, by locked decision.

**Two findings that cost a round-trip and are now written into `CLAUDE.md`:**

- **House rule 5 fired on `Terminal.commands`** — public in the publicized assembly, private in
  the real one. It compiled clean and threw `FieldAccessException` in-game. Worse than the rule
  implies: Mono resolves field access at JIT time, so the enclosing `try/catch` never ran, and
  the `ConsoleCommand` registration three lines above it never ran either. The instrument
  disabled the feature it was measuring. Fixed with cached `AccessTools.Field` reflection, kept
  in a separate method from the registration.
- **Other boat mods may patch `Ship.CustomFixedUpdate` too** - see task 2 and `CLAUDE.md`.

`tools\package.ps1` was deliberately not ported: its whole value is refusing to package when
the three version strings disagree, and that is worth having on a real release, not a skeleton.
It arrives with the first publishable build.

---

## 1. `CurrentField` — the math, and a way to read it — DONE 2026-08-28 (0.1.0)

Verified on the dedicated server, a land-to-ocean transect straight out from spawn:

```
CurrentField live — seed -1790482695, water level 30, tide 57%, season index 0
  x=0     h=79.34  land
  x=2000  h=16.71  depth 13.29m 0.335m/s Coastal
  x=8000  h=0      depth 30m    0.315m/s Drift
  x=9000  h=0      depth 30m    0.197m/s Drift
  x=10000 h=19.7   depth 10.3m  0.665m/s Coastal
```

Harness **57/57**, every assertion proven to fail without its fix. Nothing is pushed: the
field touches no rigidbody anywhere.

**Design change, made during the build and worth knowing before task 2 reads the code.** The
four hand-written terms in the original sketch became **one stream function plus terrain**:
open water is the perpendicular gradient of a scalar field, `u = dpsi/dz, v = -dpsi/dx`, built
from three plane waves. That makes the field **divergence-free by construction** — water is
neither created nor destroyed — and gyres, fast water and slack all fall out of one mechanism
instead of three that have to be balanced against each other. Coastal set and race detection
stay terrain-driven on top of it.

**The seasonal REVERSAL was deliberately softened to a 15-degree rotation plus a magnitude
change.** A full flip would invalidate every seamark a crew had learned, four times a year,
which destroys the one thing this mod exists for. The reversal a player actually feels is the
tide flipping the coastal stream, which is implemented and tested.

**Three things the work turned up, all now in `CLAUDE.md`:**

1. **Valheim's open ocean is a flat floor at generator height exactly 0** — a uniform 30m
   depth. Caught by refusing to accept two ocean points reporting depth "exactly 30.0m" and
   re-logging the raw height instead of reasoning about what it must mean. `ShelfDepth` was 40,
   above the sea's own floor, which classified the entire ocean as shelf; it is 28 now and a
   test pins it under 30.
2. **A single-scale race probe is blind to a strait.** 64m either side of the flow can only see
   gaps under ~128m. Two scales now (64m and 160m), so both a tight race between rocks and a
   300m strait between islands register.
3. **A passing test can still be a worthless test.** The "speed never exceeds MaxSpeed" sweep
   passed with the clamp deleted, because at default settings natural magnitudes never approach
   the ceiling. It now also sweeps against a deliberately low ceiling and asserts the clamp
   actually engages. Separately, the coastal-reversal test was rewritten to isolate the term by
   subtracting the slack-tide sample — comparing absolute directions made it a hostage to two
   unrelated constants, and it broke when one of them was corrected while the code was right.

**Verbose-gated field dump.** `VerboseLogging` makes `SeaTick` log a one-shot transect at boot.
It is not scaffolding to remove: a headless server has no console anyone can type `wake field`
into, so without it the field is unobservable on the machine that matters most.

## 1b. Original task 1 specification (kept for reference)

Pure arithmetic in `Core/CurrentField.cs`: no clock, no config, no Unity types beyond
`Vector3`, so the harness compiles and tests the shipping source. **No forces are applied in
this task.** Reporting comes before pushing — a drift you cannot read is a drift you cannot
debug.

Composed of four terms, each independently togglable so they can be tuned apart:

- **The Great Drift** — a basin-scale gyre keyed off world seed, seasonally reversing.
- **Coastal set** — parallel to the shore with a slight onshore component. Needs a cheap
  land-proximity read; prefer `WorldGenerator` height sampling over anything that touches
  loaded zones, so the field stays a pure function of coordinates.
- **Races** — acceleration where two landmasses are close.
- **Slack and eddies** — near-zero magnitude with slow rotation where arms oppose.
- **Tide** — a global phase on a ~2-day cycle, swinging magnitude and reversing coastal set.
  A pure function of world time. No state, no save file.

`wake here` reports the vector, its magnitude, the tide phase and which term dominates, at the
caller's position. `wake field <x> <z>` answers for an arbitrary point.

**Acceptance:** unit tests cover determinism (same seed + position + time ⇒ identical vector,
on repeat calls and across process restarts), the seasonal reversal, the tide cycle, and
magnitude bounds. `wake here` answers on a live server and the numbers change as you sail.
Two different machines on the same world report the **same vector for the same point** — the
whole no-sync design rests on that, so measure it rather than assuming it.

**Tuning target, from the design:** strongest water in the world ≈ 15–25% of half-sail speed,
typical water ≈ 5%. Vanilla reference values: `m_backwardForce = 50f`,
`m_sailForceFactor = 0.1f`.

---

## 2. Set and drift — DONE 2026-08-28 (0.2.1), MEASURED LIVE

**Verified on a live client, in a boat.** A VikingShip drifting at (8060, 246) in 0.256 m/s
water settled at **0.97x the water's speed along the current** — the model's target is 1.0.
The push faded from 0.0051 at rest to 0.00017 at convergence, which is the saturation term
doing exactly what it was built to do.

The model took **three attempts**, and each was killed by a measurement rather than by review:

1. **Drag toward the water**, `force ∝ (water − hull)`. Double-counts vanilla's damping, which
   already assumes still water. Would have braked every boat under sail by ~3.4 m/s². Caught by
   reasoning about the sailing case — but only after the tests had been written to match the
   wrong premise, so the harness was green on it.
2. **A push calibrated against vanilla's damping** so the two balance at the water's speed.
   Measured: a karve drifted 19m in 30s, matching the prediction to 3% — including the half of
   the prediction that was bad news, that it drifted at **twice** the water's speed, worse in
   weaker water. The fix looked like a square law. It was not.
3. **A square law.** Also wrong, and unfixable in principle: `m_dampingForward` is a SERIALIZED
   UNITY FIELD that every boat prefab overrides. The class default read off the decompile (0.01)
   applies to no actual boat — a VikingShip measured ~0.0053 effective and settled at 1.38x. A
   raft, karve and longship would each land on a different multiple, so **no single constant can
   be correct**.

**What shipped: saturation.** The push fades to zero as the hull's speed along the current
reaches the water's own. That sets the equilibrium by construction, without knowing anything
about how a hull damps, so every boat type converges on the same answer — and `wake here`'s
number becomes the speed a sailor will actually drift, on any hull.

**THREE instrument failures, one per round-trip, and they cost more than the bugs:**

- `wake drift` reporting **"pushed 0 hulls"** was the designed control on land, reported as a
  failure. The control needs to be labelled as one.
- The **RATIO readout printed total hull speed**, so a converged hull at 0.97x along the current
  read as a runaway at 1.33x. The gap was motion ACROSS the current — wave-driven surge the mod
  neither causes nor controls. Now reports ALONG-RATIO with total beside it.
- The **anti-braking test failed to fail** when first injured, because two clamps guard that
  property and only one had been removed. A test that cannot be made to fail proves nothing.

**TWO-HULL CONVERGENCE CONFIRMED 2026-08-28 (0.2.1)** — the saturation model's central claim,
and the one thing the harness cannot check. Same water (~0.245 m/s), same spot:

| Hull | ALONG-RATIO | total |
|---|---|---|
| Karve | 0.84 – 0.98 (mostly ~0.86) | 1.5 – 2.5 |
| VikingShip | ~0.96 | 1.03 – 1.41 |

Under the calibrated model these two would have settled on quite different multiples, because
their `m_dampingForward` values differ. They now agree within ~0.1 and both sit near 1.0.

Two things fell out of the same run:

- **The anti-brake clamp caught working live, in a case nobody constructed.** One karve line
  reads `along -0.41 ALONG-RATIO -1.68 dv 0.00488` — the hull momentarily moving AGAINST the
  current, and the push going to its at-rest maximum rather than negative.
- **Wave-driven cross-motion is large and hull-dependent.** The karve shows total ratios above
  2.0 while sitting at 0.86 along; the longship stays near 1.2. A light hull gets thrown around
  by waves far more. Reporting total speed would have made the karve look twice as broken as the
  longship when both were fine — which is exactly the mistake the ALONG/total split fixed.

**A hull sitting still under near-full push turned out to be two spawned boats collided**, not
a defect. Worth recording because the wrong explanation was nearly written up as an engine
fact: the guess was that `WorldGenerator.GetHeight` returns base terrain and so cannot see
placed rock prefabs. That may well be true, but it is UNVERIFIED and was not the cause here.
Do not cite it.

**Residual, accepted:** the karve settles ~0.10 lower than the longship. Within wave noise, and
closing it would mean raising `DriftStrength`, which also changes how fast a hull is grabbed.
Left alone deliberately.

**Still open, needs a human:** compatibility testing against other boat mods. Everything
measured so far is a clean baseline, taken with none installed.

## 2z. Original task 2 build notes (superseded)

Built, unit-tested (**83/83**, every assertion proven to fail without its fix), and confirmed
armed on a dedicated server:

```
[Info :  Undertow] Harmony patched 2: Terminal.InitTerminal, Ship.CustomFixedUpdate
```

`Ship.CustomFixedUpdate` attaching is a real result rather than a formality: Harmony resolves
the three private field injections (`___m_nview`, `___m_body`, `___m_players`) at patch time, so
a wrong field name throws there. All three are **private** in the shipping assembly and public
only in the publicized reference — naming any of them directly would have been the
`Terminal.commands` failure again, fifty times a second.

🚫 **THE ACCEPTANCE IS NOT MET AND CANNOT BE MET FROM A SCRIPT.** Boat physics runs only on the
peer that OWNS the hull, which is a player's machine. A dedicated server with nobody aboard
never executes the postfix once. Everything below the line is verified; the quantitative
displacement test needs a person in a boat. `wake drift` exists precisely so that person can
check it in under a minute.

### The model changed during the build, and this is the important part

The first implementation was **drag toward the water**, `force ∝ (water − hull)`. It is wrong,
and wrong only under sail: vanilla's damping already models hull-water resistance against
absolute velocity, so a second drag term double-counts it and a karve making 6 m/s in 0.3 m/s
water gets `0.6 × (0.3 − 6) ≈ −3.4 m/s²` — the sea braking every boat under way.

It is now **entrainment**: a push proportional to the water's velocity and nothing else, which
is the first-order correction for vanilla damping being computed in the wrong frame. It carries
a boat and never fights the sail. `DriftForce.Compute` does not take the hull's velocity as a
parameter at all, so the bug is unrepresentable rather than merely tested against.

**The harness was green on the wrong model**, because the tests were written from the same
mistaken premise as the code — the failure mode a test suite cannot catch by itself.

### Verification protocol for whoever next sails

1. Install `Undertow.dll` on the server **and the client** — physics runs on the client that
   owns the hull, so a server-only install pushes nothing, ever.
2. Set `VerboseLogging = true` in `com.raveniron.undertow.cfg` on both.
3. Standing on land: `wake drift` should report **pushed 0 hulls**. That is the control.
4. Board a boat and get under way, then `wake drift` again. It should now report a push count,
   the water speed under you, and the velocity change applied that tick. If it still says 0,
   the patch is not reaching hulls and nothing below this line is worth doing.
5. `wake here` gives the current's speed and BEARING under you. **The strongest single signal is
   directional**: drift with the sail down and confirm you move the way `wake here` said.
6. Quantitative run: from a fixed spot, fixed heading, half sail, 120 seconds, note the end
   position. Set `EnableDrift = false`, repeat identically. The displacement difference should
   be on the order of `water speed × 120s` (≈36m at 0.3 m/s), and along the reported bearing.
   Expect somewhat less than that: vanilla's quadratic damping opposes the drift, and it
   opposes sideways motion five times harder than forward.
7. **Repeat step 6 with any other boat mod disabled.** Some of them patch this same method, and
   at least one adds force to the hull and caps its speed there. Our postfix runs last, so a
   push arrives on top of whatever they did and a speed cap simply absorbs it near the ceiling -
   expected to compose, but measure rather than assume. If the two runs differ by more than a
   cap explains, the answer is a default-off compatibility toggle, never a priority war.

### What was deliberately NOT done

Nothing assigns `linearVelocity`; nothing touches a ZDO position; the postfix re-checks
`IsOwner()` itself and is stricter than vanilla — a hull whose ownership cannot be established
is declined rather than assumed. Unattended boats get `UnattendedDriftFactor`, **default 0**.
The current fades to nothing by 10420m so it never argues with `Ship.ApplyEdgeForce`.

## 2b. Original task 2 specification (kept for reference)

The mod's entire write surface. One postfix on `Ship.CustomFixedUpdate`.

- 🚫 **Re-check `IsOwner()` inside the postfix.** Vanilla's owner guard is *inside* the
  method and does not protect a postfix. Without this, every peer pushes the same hull.
- Apply as force at the centre of mass, in vanilla's own units from that same method.
- **Never assign `linearVelocity`** — vanilla assigns it wholesale in the same tick.
- **Never touch the ZDO position.**
- Unattended boats (`m_players.Count == 0`) get a configurable fraction, **default 0**.
- Fade the current out past 10400m so it never fights `Ship.ApplyEdgeForce`.

⚠️ **Other boat mods may patch this same method**, and at least one popular one adds force to
the hull and caps its speed there. Our postfix runs last - after any prefix and after vanilla -
so the current arrives on top of whatever they did, and a speed cap simply absorbs it near the
ceiling. Expected to compose. **Run the acceptance below twice, with and without**, and compare;
if the displacement differs by more than a cap explains, the answer is a default-off
compatibility toggle, never a priority war.

**Acceptance, and it is quantitative:** sail a fixed heading at half sail for a fixed duration
from a fixed start, with the mod off, and record where you end up. Repeat with the mod on. The
displacement between the two runs matches `CurrentField`'s prediction for that passage. A
"felt about right" acceptance is not acceptance — this is the task where a sign error or a
units error hides for months.

Second run to do while you are there: two players in one boat, sailing together. Confirm one
push, not two, and no fight over the hull.

---

## 3. Tide, storm surge, and the Wrath bridge — BUILT 0.3.2, VERIFIED EXCEPT ONE LINK

Harness **97/97**. Both bridge states verified headless on a real dedicated server, by parking
and unparking RW between boots:

```
RW ABSENT:  Ragnarok's Wrath not installed — storm surge and seasons are dormant.
            The sea still runs; it just has no weather behind it.
RW PRESENT: Ragnarok's Wrath detected — bridged.
            CurrentField live — ... season index 0 (read from Wrath)
```

**`(read from Wrath)` is the whole point of that line.** Spring is index 0, and so is every
failure mode — RW absent, getter unresolved, invoke throwing. On a day-0 world where RW
genuinely reports Spring, a correct read and a total failure print the identical number.
`WrathBridge.SeasonWasRead` is the only thing that separates them, and without it the log
entry would have been worthless.

**We read FACTS, not tuning.** RW exposes `WindMultiplierAt(pos)`, which already returns a
number that rises in a storm and would have been one fewer read. It is deliberately unused:
that value is `StormWindMultiplier`, which a server owner sets to tune FIRE SPREAD. Borrowing
it would mean raising your fire risk silently roughened the sea — a coupling neither mod's
owner asked for, and one nobody would think to look for. We read `IsStormAt` and apply our own
multiplier.

**Surge is applied BEFORE the ceiling**, so `MaxCurrentSpeed` stays a true ceiling on water
speed anywhere in the world. A storm drives weak water toward it, never through it. Tested,
including a sweep proving a 4x storm cannot breach a 0.2 m/s cap.

✅ **THE LAST LINK IS CLOSED — measured live 2026-08-28 (0.3.3), ON THE CLIENT**, which is
exactly the machine the first implementation was dead on:

```
[Undertow]        STORM at (8101, 368) — IsStormAt(centre)=True, surge x1.6
                  | at centre: 0.38 m/s Drift | 800m away: 0.266 m/s surge x1
[RagnaroksWrath]  [WeatherSystem] storm started at (8101, 368).
```

Two mods, two machines, the same centre — our client recovered from vanilla's replicated event
the exact position RW chose on the server. And the design promise reads off one line: **x1.6 at
the centre, x1.0 at 800m.** The sea rises where the storm stands and nowhere else.

**A defect was found BEFORE this test ran, by reading RW's source rather than trusting the
plan.** The bridge originally called `WeatherSystem.IsStormAt(pos)`. That is dead on a
dedicated server: `StormActive` is assigned only in `WeatherSystem.Tick()`, and RW's
`WorldTick` returns early when the process is not the simulation authority — so on a pure
client it is false forever, and drift is applied by the peer that OWNS the hull, i.e. a client.
Storm surge would have reached nobody except on a listen host. Rewritten to read vanilla's
replicated `RandomEvent`, which needs nothing of RW's to be ticking.

**Then a SECOND accessor bug, caught by the server logging nothing while the client logged the
storm.** `GetActiveEvent()` returns `m_activeEvent`, which vanilla sets only when a LOCAL PLAYER
is inside the event area — so it is permanently null on a headless server. `GetCurrentRandomEvent()`
is the correct call (0.3.4): the scheduler sets it on the server, `RPC_SetEvent` sets it on every
client, and it does not care where anyone is standing. Both facts are now in `CLAUDE.md`.

🚫 **THE SEASON HAS THE SAME DISEASE AND NO CURE.** `SeasonSystem.Current` is also assigned
only inside `Tick()`, and RW syncs no season state anywhere - so every client computes the
field as spring while the server knows the truth. Boats do NOT desync (every client agrees with
every other) but the seasonal shift is inert away from a listen host. The effect is a
15-degree rotation and a magnitude nudge, so this is a lost flourish rather than a broken
feature. The fix is asking RW to sync its season, NOT running a second season clock here -
that is precisely the conflict house rule 4 exists to prevent.

**Also worth keeping:** RW storms cannot fire on an EMPTY server at all - the event carries
`m_pauseIfNoPlayerInArea = true` and its position is chosen from "somewhere a player actually
is" via character ZDOs. Eight minutes at a 60-120s interval with nobody online produced
nothing, exactly as that design predicts. Storm work needs a player online.

**To close it (needs a player, ~5 minutes):**
1. RW must be on the CLIENT too, not just the server — drift is computed on the peer that owns
   the hull, so a client without RW computes no surge no matter what the server thinks. Copy
   `RavenIronStudios-RagnaroksWrath` into the Gale `Default` profile.
2. Shorten `StormMinIntervalSeconds` / `StormMaxIntervalSeconds` in RW's config (both machines),
   or trigger one directly with `event ragnarokswrath_devastating_storm` from an admin console.
3. Sail into it and run `wake here`. It prints `STORM SURGE x1.6 — the sea is up here` inside
   and nothing outside. With `VerboseLogging` on, `SeaTick` also logs the storm centre, the
   speed there, and the speed 800m away, so the "here and nowhere else" promise is one line.

## 3z. Original task 3 specification (kept for reference)

`Bridge/WrathBridge.cs` — reflected, read-only, soft. Resolve once into cached delegates;
retry until resolved rather than latching a failure; log once on success and once on absence,
naming the member.

- `WeatherSystem.StormActive` / `StormCentre` → storm surge: magnitude and confusion rise
  under a storm, positionally, never globally.
- `WindSystem.IntensityAt(Vector3)` → feeds current strength, so Undertow never reads
  `EnvMan` and rule 4 holds by construction.
- Season → the Great Drift's seasonal reversal.

**Acceptance:** with RW installed, a storm passing over water measurably changes the reported
field at its centre and not 500m outside it. **With RW absent, the mod loads clean, logs the
absence once, and sails normally** — that second half is the one that gets skipped, and it is
the one users will hit.

---

## 4. Flotsam — UNBLOCKED 2026-08-28 (0.4.1): the `Floating` question is answered

**123 of 1090 item prefabs carry `Floating`.** Measured headless by scanning
`ObjectDB.instance.m_items` on a live dedicated server — the one question no decompile could
answer, because component attachment lives in Unity asset data rather than in the assembly.
Flotsam can be built from vanilla `ItemDrop`s, so the no-new-prefabs constraint holds and the
design survives.

**The surprise, and it changes how the palette is chosen: `Floating` is loss-prevention, not
buoyancy.** `ShieldIronTower`, `MaceIron` and `ShieldFlametal` float; ore, stone, berries and
most food do not. Valheim attached it to things a player might drop and never recover. So what
washes up is a FLAVOUR decision, not a list dictated by physics — pick for the story, not for
what happens to be buoyant.

Usable palette, from the measured list:

| Kind | Prefabs |
|---|---|
| Driftwood | `Wood` `RoundLog` `FineWood` `ElderBark` `Blackwood` `YggdrasilWood` `Root` |
| Forest debris | `FirCone` `PineCone` `HardAntler` `WitheredBone` `Tar` |
| From the sea | `SerpentMeat` `BonemawSerpentTooth` `VoltureMeat` |
| Wreckage | `ShieldWood` `SpearWood` `Club` `BowFineWood` `FishingRod` `FishingBaitOcean` |
| Rare prize | `DragonTear` `Wishbone` `Demister` `MeadSwimmer` |

`wake floats` reports this in-game; `VerboseLogging` logs it once at boot. Kept rather than
removed: it is the only way to re-answer the question after a Valheim update moves the assets.

⚠️ **One step still unproven:** that a spawned `ItemDrop` actually SITS on the surface where we
put it. The component's presence is strong evidence, not proof — spawn one and watch it before
trusting the spawner.

✅ **FULLY VERIFIED 2026-08-28 (0.5.1) — including the step no log could settle: IT FLOATS.**
Driftwood spawns in slack water and was seen bobbing on the surface by the owner. `Root`,
`RoundLog`, `Wood`, `FirCone`, `FineWood` all spawned cleanly, no missing prefabs, no warnings.

**A REAL BUG WAS FOUND BY THE LIVE TEST, and the log looked healthy the whole time it was there.**
Eight spawns in a row each printed `[1/12 alive]` — the tracking list emptied every tick, because
the first version held `GameObject` references and **a dedicated server unloads the instance the
moment a nearby client takes the item over**, while the item lives on as a ZDO. That silently
disabled BOTH the cap and the TTL: the entire safety valve against filling the ZDO table was
inert, and nothing in the output said so. Fixed by tracking `ZDO.m_uid` and looking it up through
`ZDOMan.GetZDO`, taking ownership before destroying since only the owner may remove a ZDO. The
count then climbed `1→2→3→4→5` as it should.

This is the clearest case in the project of a green-looking log hiding a dead safety mechanism —
the sort of thing only a live test finds, which is why task 4 was gated on one.
## 4z. Original task 4 specification

🚫 **Blocked until the `Floating` question is answered in-game.** Do not write the spawner
first.

- Vanilla `ItemDrop` prefabs only. No new prefabs, ever.
- Server-authoritative, driven from `SeaTick`, budgeted.
- Spawn only near a real player. Nothing accumulates in unloaded ocean.
- Hard cap per zone and a decay timer, both configured, both logged when hit.
- Post-storm wreckage differs from calm-day driftwood.

**Acceptance:** items spawn in slack water, float correctly, and are collectable. On a
long-running world the per-zone cap holds and the ZDO count is stable across several hours —
measure it, do not assume it. Removing every player from the area stops production entirely.

---

## 5. Swimmers — BUILT 0.5.0, ARMED, drift itself needs a swimmer

Harness **162/162**, every assertion proven to fail without its fix. The patch attaches on a
dedicated server:

```
Harmony patched 3: Terminal.InitTerminal, Character.UpdateSwimming, Ship.CustomFixedUpdate
```

That is a real result: `UpdateSwimming` is PRIVATE, and `m_currentVel` and `m_nview` are too, so
attachment proves Harmony resolved the method and both field injections — including the writable
`ref ___m_currentVel`. A wrong name throws at patch time.

**THE BACKLOG'S OWN PRESCRIPTION WAS WRONG, and reading the body before building caught it.**
This task said to fold the current in "the way vanilla's own `AddPushbackForce(ref m_currentVel)`
does". That helper ignores `m_pushForce`'s magnitude completely and drives velocity to a flat
20 m/s along its direction, halved to 10 while swimming — it ejects a body from a creature it is
clipping through. A 0.3 m/s current through that channel would fire a swimmer off at five times
swim speed. Both `CLAUDE.md` and this entry are corrected; the old advice is marked as wrong
rather than quietly deleted.

**The scaling is the other trap, and it is a factor of twenty.** Vanilla lerps `m_currentVel`
toward the swimmer's intent each frame, so a per-frame addition `d` settles at
`d / m_swimAcceleration` — and that is 0.05. The delta is therefore pre-multiplied by the
acceleration, which cancels the amplification exactly and leaves the steady state equal to the
intended drift. A test simulates vanilla's own lerp for 4000 frames rather than trusting the
algebra.

**The drowning guard is a SAFETY PROPERTY, not a balance dial.** `SwimmerMaxShareOfSwimSpeed`
caps drift as a share of the character's own swim speed. The harness sweeps the ENTIRE legal
config range — every drift factor, every cap, water from 0 to 5 m/s — and asserts a swimmer
always out-swims the water. If a player can be held offshore until they drown, the feature is
wrong rather than mistuned. Worst case across the whole range is 0.9 of swim speed, at the
extreme of a setting the description warns about; at shipped defaults it is 0.21 m/s against a
swim speed of 2.

**Players only, deliberately.** Vanilla lerps a CREATURE's swim velocity with a factor of 0.5
rather than `m_swimAcceleration`, so the scaling above would be wrong for them — and dragging
swimming creatures around changes AI pathing, which nothing asked for.

🚫 **Unverified, and needs a swimmer:** that the drift is actually felt in the water. Swim out
into open ocean with `VerboseLogging` on and the log prints water speed, computed drift, the cap
and the swimmer's measured speed on one line every three seconds. **Then test the guard on
purpose:** swim directly upstream in the fastest water you can find and confirm you make
progress. That is the one acceptance criterion that matters.

✅ **VERIFIED LIVE 2026-08-28 (0.5.1), including the safety property.** The decisive lines are
the ones where the player stopped swimming and simply floated:

```
swim drift @ (8075,-23) | water 0.345 drift 0.172 (cap 0.7) | swimmer 0.164 m/s, swimSpeed 2
swim drift @ (8077,-22) | water 0.347 drift 0.173 (cap 0.7) | swimmer 0.165 m/s, swimSpeed 2
```

**Computed drift 0.172, measured 0.164** — a 95% match, and conclusive proof that the
1/m_swimAcceleration amplification is cancelled: had the scaling been omitted the swimmer would
have moved at roughly 3.4 m/s instead of 0.16.

**The drowning guard has a tenfold margin.** While actually swimming the player held 1.9-2.0 m/s
— full swim speed — against a 0.17 m/s current. Nobody can be pinned offshore.
## 5z. Original task 5 specification (its AddPushbackForce advice was WRONG - see above)

Last, deliberately: the highest-annoyance surface in the mod, and it wants the most tuning
evidence behind it.

- 🚫 **`AddForce` does not work here.** `Character.UpdateSwimming` servos the body toward
  `m_currentVel` every frame, so an external force is cancelled next tick. Fold the current
  into the velocity target the way vanilla's own `AddPushbackForce(ref m_currentVel)` does.
- Cap well below `m_swimSpeed` (2f) so a player can always make headway against it.
- Config-gated, and gentle.

**Acceptance:** a swimmer is visibly set down-current while still able to swim to shore from
any point in the field, at the strongest current the config allows. Verify the drowning case
explicitly and deliberately: **if a player can be held offshore until they drown, the feature
is wrong**, not the tuning.

---

## Open questions

- **Which vanilla item prefabs carry `Floating`?** Blocks task 4 entirely. Answerable in one
  in-game session; do it early even though the system ships late.
- **Does the mod have to be on every client?** Boat physics runs on the owning peer, which is
  a player's machine, so an unmodded client sails a currentless sea. Leaning: required on
  server and clients, version-gated the way RW does it. Settle it before the first release.
- **Does Moder's wind control exempt you from the current?** Leaning strongly no —
  "Moder gives you the wind, not the sea". Locked in `CLAUDE.md` unless argued otherwise.
- **Should the field ever be persisted?** Leaning no for v1. It is a pure function today,
  which is exactly why it needs no sync and no save file; a sea that *remembers* is a sequel
  question, and RW already owns "the world remembers".
- **Do currents belong in rivers and lakes?** Ocean-only for v1; narrow water plus a sideways
  force pins players against terrain.
- **What does a fully loaded server cost?** `CurrentField` is evaluated per boat per fixed
  update. It is cheap arithmetic, but it has never been profiled. Do so before release.

