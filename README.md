# Undertow

> *The sea remembers where it is going.*

A Valheim mod by **Raven Iron**. Valheim's ocean already has weather — waves that answer the
wind, storms that raise them, a hull that takes damage in a seaway. What it doesn't have is
**water**. Nothing moves. A karve left at half sail on a fixed heading arrives exactly where
geometry says it will, every time, in every part of the map.

Undertow gives the sea its own motion. Currents run across the ocean in a shape you can learn:
a slow basin drift, a stream that follows the coast, fast water between close islands, and dead
water behind a headland. They carry what floats on them.

**No map, no HUD, no icons.** The sea tells you itself, or it doesn't tell you at all.

---

## What it does

### 🌊 The sea has a shape
A current field spread across the whole ocean, built from a stream function — so the flow is
**divergence-free**, the way real water is. Gyres, races and slack water all fall out of the
same mechanism rather than being placed by hand.

- **The drift** — slow, basin-scale, the thing you plan a long voyage around.
- **Coastal set** — a stream that follows the shore, with a slight push toward it. This is why
  you don't doze at the tiller with the coast downwind.
- **Races** — water accelerates between close landmasses. A narrow gap is fast water, in one
  direction, and a 300m strait counts as much as a gap between rocks.
- **Slack and eddies** — where opposing arms meet, the water goes dead. Things collect there.

It is **the same on every machine** without a byte of network traffic: the field is a pure
function of the world seed, the position, the world clock and the season. Two players a thousand
metres apart compute the same water and never have to agree about it.

### 🌒 Tides
A slow flood and ebb on a configurable cycle. It swings how hard the open ocean runs and
**reverses the coastal stream**, so the passage you know is a different passage six hours later.
Departure time becomes a decision.

### ⛈ Storm surge *(with Ragnarok's Wrath)*
Where a Devastating Storm stands, the water rises — **there and nowhere else**. A sheltered
passage stops being sheltered while the storm sits over it. Entirely optional: without
Ragnarok's Wrath the bridge logs its absence once and the sea runs regardless.

### ⛵ Boats are carried, never braked
The current adds to a hull rather than fighting it, so sailing is never slowed — you are simply
somewhere else than you expected. A boat left drifting settles at **the water's own speed**, and
that is true of a raft, a karve and a longship alike. A hull resists sideways drift about twice
as hard as forward drift, which falls out of Valheim's own physics rather than being imposed.

**Boats with nobody aboard are not touched, by default.** Vanilla already damps an unmanned hull
almost to a stop, and a moored longship wandering off while you are away is not a feature.

### 🪵 Flotsam
Currents converge, so things gather. Driftwood, cargo and what the drowned no longer need
collect in slack water — giving you a second reason to know where the sea goes quiet. While a
storm is overhead, what washes up is wreckage instead.

Uses **only vanilla items**, never a new prefab. Capped, reclaimed on a timer, and spawned only
near a real player — an empty ocean stays empty.

### 🏊 Swimmers
The current carries a swimming body too, gently. It is **hard-capped below swim speed**: you can
always out-swim the water and reach shore. That is a safety property rather than a balance dial,
and it is enforced across every setting the config permits.

---

## Install

Drop `Undertow.dll` into `BepInEx/plugins/` on the **server and every client**. Boat physics runs
on whichever machine owns the hull — a player's — so a server-only install pushes nothing.

Config appears at `BepInEx/config/com.raveniron.undertow.cfg`. Every system has its own switch;
every rate, cap and threshold is tunable.

**Keep the config identical on the server and every client.** The field is recomputed
independently on each machine, so a client with different settings sails a different ocean.

## The `wake` console

| Command | Answers |
|---|---|
| `wake status` | what this machine is, and what is running on it |
| `wake here` | the current under your keel — speed, bearing, depth, tide |
| `wake field <x> <z>` | the current anywhere, loaded or not |
| `wake drift` | whether the current is actually reaching boats |
| `wake floats` | which item prefabs carry `Floating` (the flotsam palette) |

Turn on `VerboseLogging` and the log carries a boot-time transect of your seed's ocean, plus a
running readout of drift while you sail.

## Plays well with

- **Ragnarok's Wrath** (Raven Iron) — detected automatically. Storms raise the sea where they
  stand and the season shifts the drift. Optional; absence is logged once and changes nothing else.
- **Seasonality / Seasons** — never touched. Undertow selects no environment, reads no season
  directly, and modifies no material.
- **Boat stat mods** should compose: they change the hull, Undertow changes the water.

## What it deliberately is not

No map, no compass, no wind gauge, no HUD of any kind. No new prefabs, assets or bundles. No
second wave system and no weather — vanilla and Seasonality own that ground. No boat rebalancing
and no new ship types. Undertow changes the sea and nothing else.

---

*Raven Iron. See also **Ragnarok's Wrath** (the world reacts and remembers) and **FireFront**
(fire that spreads).*
