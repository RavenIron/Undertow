# Session handoff — 2026-08-28 (the mod was designed, built, verified and packaged in one day)

For the next session picking this up cold. Read `CLAUDE.md` first, then this. `docs/BACKLOG.md`
carries per-task verification detail and the acceptance criteria each task was held to.

Design document (the reasoning behind every locked decision):
<https://claude.ai/code/artifact/e213f36d-fdcd-4695-a159-f8e4e1157323>

---

## Where things stand

**Undertow 0.5.1. The roadmap is complete and every task is verified in-game.** Harness
**162/162**, clean build, `RavenIron/Undertow` on GitHub, release zip built.

| Task | State |
|---|---|
| 0 skeleton | done — loads headless, `wake` console registered and read back |
| 1 `CurrentField` | done — server and client produce byte-identical transects independently |
| 2 drift | done — karve and longship both settle near the water's own speed |
| 3 Wrath bridge | done — live storm surge x1.6 at centre, x1.0 at 800m |
| 4 flotsam | done — spawns, caps, reclaims, **and floats** (seen by the owner) |
| 5 swimmers | done — computed 0.172 vs measured 0.164, tenfold margin over drowning |

Release files exist: `manifest.json`, `README.md`, `CHANGELOG.md`, the owner's `icon.png`
(256x256), and `tools/package.ps1` with three guards. `dist/RavenIron-Undertow-0.5.1.zip`
builds clean.

---

## 🚨 THE ONE OPEN BLOCKER — read this before making the repo public

The repo is **PRIVATE right now and must stay that way until this is resolved.**

It was briefly public carrying a decompile writeup of another author's shipping mod. That was
removed from the working tree, then removed from **every commit** by a `filter-branch` rewrite
and force-pushed — the current history is clean, verified commit by commit.

**GitHub still serves the OLD commit SHAs.** Measured after the force-push: all seven
pre-rewrite SHAs still return content through the API. That is normal GitHub behaviour —
unreferenced objects survive until their garbage collector runs, which is not on a schedule you
control.

### The decision is made, and the exact next action is one command

**The owner chose: delete the remote repo and recreate it**, pushing only the clean history.
Definitive and entirely under our control — the repo was created the same day and has no stars,
forks or collaborators, so nothing is lost.

**It is blocked on a token scope.** `gh repo delete` returns:

```
HTTP 403: Must have admin rights to Repository.
This API operation needs the "delete_repo" scope.
```

The token carries `gist, read:org, repo, workflow`. Granting the scope needs a browser
authorization only the owner can complete:

```
gh auth refresh -h github.com -s delete_repo
```

**Once that is done, the whole remaining sequence is:**

1. `gh repo delete RavenIron/Undertow --yes`
2. `gh repo create RavenIron/Undertow --private --source=. --remote=origin --description "The sea gets its own motion. Currents, tides and storm surge for Valheim. A Raven Iron mod."`
3. `git push -u origin main`
4. Verify the old SHAs 404 — they are `4c8d698 705e595 7ebdbf1 13e128a 110fa5d 397c502 802aab3`,
   checked with `gh api repos/RavenIron/Undertow/commits/<sha>`. **Verify this rather than
   assuming it**, since the whole point of the exercise is that a force-push did NOT achieve it.
5. `gh repo edit RavenIron/Undertow --visibility public --accept-visibility-change-consequences`

**If the owner would rather not grant `delete_repo`**, there is a route needing no new scope:
rename the current repo to `Undertow-archive-prescrub` (stays private, keeps the old objects),
create a fresh `Undertow`, push the clean history there, and delete the archive from the web UI
later. Same end state — the public URL holds only clean objects — one loose end to tidy.

Doing neither is also defensible: the old SHAs are not discoverable without having recorded them
during a roughly twenty-minute public window. But that is the owner's call, not an assumption
for the next session to make.

**A local safety net exists:** branch `backup-pre-scrub` and the folder
`../Undertow-backup-prescrub` both hold the ORIGINAL unscrubbed history. Never push either.
Delete them once you are satisfied.

---

## What remains, in order

1. **Resolve the blocker above, then make the repo public.** `manifest.json`'s `website_url`
   points at it, so a store listing 404s while it is private.
2. **Publish.** Thunderstore and Hexium, team `RavenIronStudios` (NOT the GitHub org name —
   that distinction has bitten this studio before). Build with `tools\package.ps1`, never by
   hand: it refuses to package when the three version strings disagree, when a store file is
   missing, or when the icon is not exactly 256x256. Both new guards were tested by breaking
   them on purpose.
3. **Compatibility testing against other boat mods.** Every drift measurement so far is a clean
   baseline taken with none installed. Some boat mods patch `Ship.CustomFixedUpdate` too; our
   postfix runs last, so a push lands on top of whatever they did. Run task 2's acceptance
   twice, with and without, and compare displacement. If they fight, the answer is a
   default-off compatibility toggle, never a priority war (house rule 1).
4. **The client-blind season.** `SeasonSystem.Current` is only assigned in RW's `Tick()`, which
   RW gates on the simulation authority, so every client computes the field as spring. Boats do
   not desync (all clients agree with each other) but the seasonal shift is inert away from a
   listen host. **The fix belongs in Ragnarok's Wrath** — syncing its season — not in a second
   season clock here, which is exactly the conflict house rule 4 exists to prevent.
5. **Tuning, once real players have sailed it.** `MaxCurrentSpeed` (1.2 m/s) is the headline
   dial and has never been judged by anyone but its author. `FlotsamPerHour` ships at 6/hour,
   which is deliberately sparse.

---

## What this session actually taught, and it is the useful part

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

- **The server used for testing is the owner's Ravenrest dedicated server**, with the full
  fifteen-mod set. Testing Undertow means trimming it to match whatever the client's Gale
  profile carries, then restoring. Three of its mods (VikingOS, AzuExtendedPlayerInventory,
  WardIsLove) **hard-kick a client on version mismatch** — that is what "incompatible version"
  means there, not a Valheim mismatch.
- **The client runs through Gale**, profile `Default`, which currently carries Undertow with
  test config (`VerboseLogging = true`, flotsam at 120/hour). Valheim locks the DLL while
  running: the owner must quit to desktop before any redeploy.
- **The scratch world `UndertowSmoke`** and `LogOutput.log.pre-undertow` are both still in
  place, left deliberately — world data is not deleted without being asked.
- **Bump the version on every deploy.** It sat at 0.1.0 through several rebuilds early on, and
  "which build is actually loaded" became unanswerable from a log. The owner caught it.
- **`python` on this machine is the Microsoft Store stub, not an interpreter.** Use sed/awk or
  PowerShell.
