# Session handoff — 2026-09-02 (picked up 2026-08-30: one fix next door, store art, three compatibility analyses, a release check)

For the next session picking this up cold. Read `CLAUDE.md` first, then this. `docs/BACKLOG.md`
carries per-task verification detail and the acceptance each task was held to; tasks 2c, 2d
and 5c are new since the last handoff.

Design document (the reasoning behind every locked decision):
<https://claude.ai/code/artifact/e213f36d-fdcd-4695-a159-f8e4e1157323>

---

## Where things stand

**Undertow 0.5.1. The roadmap is complete and every task is verified in-game** — unchanged
since 2026-08-28. Harness **162/162**, clean build. `main` is `d621c5f`, in sync with the
**public** (since 2026-09-03) `RavenIron/Undertow`. `tools\package.ps1` builds `dist\RavenIron-Undertow-0.5.1.zip`
with the new shield icon and passes its own three guards.

| Task | State |
|---|---|
| 0 skeleton | done — loads headless, `wake` console registered and read back |
| 1 `CurrentField` | done — server and client produce byte-identical transects independently |
| 2 drift | done — karve and longship both settle near the water's own speed |
| 3 Wrath bridge | done — live storm surge x1.6 at centre, x1.0 at 800m |
| 4 flotsam | done — spawns, caps, reclaims, **and floats** (seen by the owner) |
| 5 swimmers | done — computed 0.172 vs measured 0.164, tenfold margin over drowning |
| 2c Sailing compat | **analysed from public source, NOT measured** |
| 2d Njord compat | **analysed from published docs only, NOT measured** |
| 5c Dive In compat | **analysed from public source, NOT measured** |

**Release check, 2026-09-02: not yet, but close, and almost nothing left is code.** One gate left:
one boat session on Ravenrest. The repo went public on 2026-09-03. Everything else is tidied.

---

## The scrub question — CLOSED 2026-09-03 by the owner's decision

The repo was briefly public on 2026-08-28 carrying a decompile writeup of another author's
shipping mod. The working tree and every commit were scrubbed and force-pushed; GitHub kept
serving the seven pre-rewrite SHAs, and the 2026-08-28 decision was to delete and recreate the
repo before going public — blocked on a `delete_repo` token scope only the owner could grant.

**On 2026-09-03 the owner chose otherwise and made the current repo public themselves, in the
web UI**, with the seven old SHAs still resolving (re-checked that morning: all seven). That is
a decision, recorded here, not an oversight: the old objects are not discoverable without
having recorded their SHAs during a roughly twenty-minute window five days earlier, nothing in
this repo's history references them, and GitHub's garbage collector will eventually evict them.

**Consequences for the next session:**

- `website_url` and the README's links resolve (HTTP 200). A store listing no longer 404s.
- The delete-and-recreate sequence and the `delete_repo` scope are **no longer needed**. Do not
  run them; do not ask for the scope.
- The seven SHAs — `4c8d698 705e595 7ebdbf1 13e128a 110fa5d 397c502 802aab3` — are kept here
  only so a future check (`gh api repos/RavenIron/Undertow/commits/<sha>`) can confirm when GC
  has run. There is nothing to do when it has.
- The local safety nets, branch `backup-pre-scrub` and folder `../Undertow-backup-prescrub`,
  still hold the ORIGINAL unscrubbed history. Never push either. They can go whenever the owner
  says so; not before.
- **Never put another author's decompiled implementation in this repo again.** Tasks 2c, 2d and
  5c show the standard that replaced it: published source or published docs, cited, surfaces
  named, code never reproduced.
- The owner flips repo visibility themselves, in the web UI. Prepare, state what would change,
  and hand over the path — do not run `gh repo edit --visibility`.

---

## What this session did (2026-08-30 → 2026-09-02)

**1. The client-blind season, fixed next door.** `SeasonSystem.Current` was only ever assigned
on RW's simulation authority, so every client computed Undertow's field as spring. The fix
belonged in RW (house rule 4), and it is there: **`Net/SeasonSync` in RW 0.25.0** broadcasts the
season to every client on `SeasonSystem`'s own 10s cadence, unconditionally, four bytes, and
`wrath status` now names the season's SOURCE on both sides — mandatory, because Spring is index
0 and so is "nothing has ever told us". Every `ZRoutedRpc` call shape was verified by decompile;
one fact recorded: `HandleRoutedRPC` drops an unregistered hash **silently**, so an unarmed or
older client leaves no trace in any log. **Undertow changed nothing and needs nothing**:
`WrathBridge` reads `SeasonSystem.Current` as before and starts getting a true answer; against
an older RW it reads spring, exactly as today, so there is no version floor. **NOT VERIFIED
IN-GAME on either side.** RW `9a879e9` was pushed by this session. RW's local `main` has since
moved to `250410a` ("0.26.0: storms anchor in the wild") from another session, unpushed and not
this session's — the season sync rides in whatever RW version deploys next.

**2. Store art.** `icon.png` is now the owner's shield render (wave, sea serpent, the name cut
into weathered wood). The 1408x768 source is wider than it is tall, so no square crop holds the
whole emblem; it is an 816x816 window centred on the shield with the 24px beyond the top and
bottom edges filled by stretching the edge row, then resized to 256x256. Both bands vanish at
icon scale; a plain 768 crop clipped both wing tips. Zip rebuilt. No version bump: nothing has
shipped with the old icon. A banner reading was tried and reverted — the owner wanted the logo.

**3. Three compatibility analyses, at three honest levels of confidence.** All in `CLAUDE.md`'s
Compatibility constraints with a ⚠️, and each with a measurement protocol in the backlog.

- **Dive In (sighsorry) — task 5c.** From its published GPL-3.0 source. Everything it does to a
  swimmer is inside `Character.UpdateSwimming`, our postfix's method: a prefix scales
  `m_swimSpeed` and steers `m_moveDir`; a postfix/finalizer restore both. **Never writes
  `m_currentVel`, never replaces the lerp, never touches `m_swimAcceleration`** — so the 20x
  cancellation holds and the two compose. The one ordering-dependent detail: which
  `m_swimSpeed` our drowning-guard cap sees. Holds either way; the tight case is an
  **encumbered diver in a storm** (0.3 m/s headway, not tenfold). Lives in the `Wonderland`
  Gale profile, not Ravenrest.
- **Sailing (Smoothbrain) — task 2c.** From public source, which stops at 1.1.7 while
  Ravenrest ships 1.1.8 (empty changelog; gap unknown). **It is NOT the "adds force and caps
  speed" mod the boat-mod warning describes** — it never touches `CustomFixedUpdate`. One
  result-decorating postfix on `GetSailForce` (up to 2.5x), skill gates, a once-a-second
  nudge impulse. Propulsion, not water. **Prediction: sail down, `ALONG-RATIO` reads
  identically with it on or off**, since 2.5 × 0 is 0. It is ON RAVENREST.
- **Njord (Wubarrk) — task 2d.** **No public source** (Discord invite for a website, a licence
  reserving modification), so analysed from README, changelog and config only, and the entry
  says so. This IS the mod the warning was written for: forces, per-hull caps (7/16.8/26/30),
  overhauled curve. What holds anyway: our saturation term is zero above 1.92 m/s along the
  current and Njord's lowest cap is 7, so **push and cap never meet**; our impulse integrates
  after every FixedUpdate patch, so any in-tick clamp overshoots by one tick's `dv` (0.024
  m/s). If it replaces vanilla's update, our postfix still runs; what changes is the damping —
  **the first non-vanilla damping the saturation claim has met, which makes `ALONG-RATIO`
  under Njord the single most informative number left to measure.** Well below 0.86–0.96 means
  `DriftStrength` up on that server, a tuning not a toggle. It is ON RAVENREST.

**4. Docs retired that a reader would trip on:** "`package.ps1` not ported yet" (it is, with
three guards), "Planned." on the built layout, the `Floating` trap still marked UNVERIFIED,
and two backlog open questions — including **every-client is settled: server and every client,
deliberately NOT version-gated** (a skewed pair behaves like the config-mismatch case the README
already warns about; revisit when a release changes the field's maths, copying RW's warn-once
`VersionSync`).

---

## What remains, in order

1. ~~Resolve the blocker, then make the repo public.~~ **Done 2026-09-03** — public, by the owner's hand; see the closed section above.
2. **One Ravenrest session, and it answers three things at once.** Njord and Sailing are
   already there, so the "with" runs are the default state. A karve, sail down, in known water
   (`wake here`), `VerboseLogging = true`, watch the 2s `drift` line settle: the `ALONG-RATIO`
   is tasks 2c and 2d's answer together (vanilla gave 0.86 karve / 0.96 longship). In the same
   session, once RW ≥ 0.25.0 is on server AND client, type `wrath status` on the client and
   read `synced from the server` — that is the season fix verified, and then `wake here` to
   see the field's seasonal term move with it. **Deploying RW to Ravenrest means a restart;
   ask before touching that server** (see memory: the join code dies with it).
3. **Publish 0.5.1.** Thunderstore and Hexium, team `RavenIronStudios` (NOT the GitHub org
   name). `tools\package.ps1` only, never by hand.
4. **Dive In, task 5c, on `Wonderland`.** Six steps; step 2 reads which postfix ordering the
   profile actually produced from the `swimSpeed` the log line prints.
5. **The "without" runs** for 2c and 2d, each a both-sides park (both mods are ServerSync-pinned).
6. **Tuning, once real players have sailed it.** `MaxCurrentSpeed` 1.2 and `FlotsamPerHour` 6
   have one judge so far.
7. **Low, not blocking:** the swimmer postfix evaluates the field every physics tick with no
   cache — 450 `GetHeight` calls a second for the local swimmer, the exact cost the ship patch
   caches away on a timer. One swimmer per client, so modest; five lines if a profile flags it.
   Nothing has ever been profiled.

---

## What this session taught

- **"Read the published source, never the DLL" scales to three confidence levels, and the
  docs must say which one each entry is at.** Dive In and Sailing got source; Njord got docs.
  All three verdicts are "composes", but a reader has to know that 2d is reasoned from Unity
  physics and our own code rather than from theirs. Say the version gap out loud too: Sailing's
  source is behind its release, and that is a fact about the analysis, not a footnote.
- **When the value you are syncing has a failure mode that looks exactly like its default,
  print the SOURCE next to it.** Spring is index 0; an unarmed client, an old client and a
  working sync on a spring day all read the same. The season line without its source was an
  instrument that could not distinguish success from silence.
- **A decompile is worth one sentence when it overturns an assumption.** "Unregistered RPC
  hashes log a warning" was wrong — they drop silently — and that changes what a missing log
  line means. Recorded where the registration lives.
- **Three tooling scars, all cheap, all avoidable next time.** The scratchpad path moves with
  the session's working directory — a write into the old one failed. Both `awk` print
  statements and `sed` replacements interpret backslash escapes, so `.\tools\package.ps1`
  arrived as a tab and a `p`; when a line carries backslashes, put it in a file and `getline`
  it in. And a ~14KB quoted heredoc through the Bash tool failed to parse at all — for a whole
  document, use the Write tool into the scratchpad and move it. `python` is still the Store
  stub.
- **A question left open for the owner, not assumed:** Njord's README is written in this
  codebase's voice, and the owner's `Wonderland` profile is the Wubarrk Wonderland pack. **If
  Njord is the owner's, its source is available** and task 2d's two open questions — replace or
  decorate `CustomFixedUpdate`, where the cap acts — close in ten minutes at 2c's standard.
  Asked; not answered; not assumed.

---

## What the 2026-08-28 session taught (kept — it is the useful part)

**Four designs were killed by measurement. None by review.**

1. **Drag toward the water** (`force ∝ water − hull`) double-counts vanilla's damping, which
   already assumes still water. It would have braked every boat under sail by ~3.4 m/s². The
   tests had been written to match the wrong premise, so the harness was green on it.
2. **A push calibrated against vanilla's damping** matched its own prediction to 3% and was
   still wrong: the boat drifted at twice the water's speed, worse in weaker water.
3. **A square law** was unfixable in principle — `m_dampingForward` is a serialized field every
   boat prefab overrides, so the decompiled default applies to no actual boat.
4. **`AddPushbackForce` for swimmers**, which this repo's own backlog prescribed. Reading the
   body showed it ignores magnitude entirely and drives velocity to a flat 20 m/s. It would have
   launched a swimmer at five times swim speed.

What shipped instead: **saturation** for boats (the push fades as a hull takes up the water's
speed, so every hull agrees regardless of its damping constants) and **acceleration-scaled
addition to `m_currentVel`** for swimmers (cancelling a twentyfold amplification).

**Five instrument failures cost more round-trips than the bugs did.**

- `wake drift`'s "pushed 0 hulls" was the designed control on land, reported as a failure.
- The RATIO readout printed TOTAL hull speed, so a converged hull at 0.97 read as a runaway at
  1.33. The gap was wave-driven surge the mod neither causes nor controls.
- An anti-braking test **could not be made to fail** on first injury — two clamps guarded the
  property and only one had been removed. A test that cannot fail proves nothing.
- Two flotsam tests were weak: the depth-gate cases used `Speed = 0`, so an unrelated injury
  drove them to zero as well (one bug masking another), and a ±2 tolerance swallowed an entire
  missing time factor.
- A **stale incremental build** produced a false test failure. The same failure mode produces a
  false PASS when injuring code to check a test — the dangerous direction. Use
  `--no-incremental` when proving a test fails.

**And one fabricated explanation was nearly written up as an engine fact.** A longship sitting
still under full push looked like `WorldGenerator.GetHeight` being blind to placed rocks. It was
two spawned boats colliding. The guess is recorded in the backlog explicitly marked *unverified,
was not the cause, do not cite*.

---

## Working notes for the next session

- **The server used for testing is the owner's Ravenrest dedicated server**, 26 plugins today
  including Njord 1.3.5 and Sailing 1.1.8. Three of its mods (VikingOS,
  AzuExtendedPlayerInventory, WardIsLove) **hard-kick a client on version mismatch** — that is
  what "incompatible version" means there, not a Valheim mismatch. RW's own `VersionSync` only
  warns, so deploying RW server-first is safe.
- **Do not restart Ravenrest without asking.** The join code dies with the process.
- **The client runs through Gale.** `Default` carries Undertow with test config
  (`VerboseLogging = true`, flotsam at 120/hour). `Wonderland` is the larger build and the
  only profile with Dive In. Valheim locks the DLL while running: quit to desktop before any
  redeploy.
- **The scratch world `UndertowSmoke`** and `LogOutput.log.pre-undertow` are still in place,
  left deliberately — world data is not deleted without being asked.
- **Bump the version on every deploy**, or "which build is actually loaded" is unanswerable.
- **RagnaroksWrath has `.claude/` and `scratchpad-ravenrest/` untracked and not ignored.**
  Stage explicitly there; do not sweep.
- **`python` on this machine is the Microsoft Store stub.** Use sed/awk or PowerShell — and
  for lines with backslashes, a file and `getline`.
