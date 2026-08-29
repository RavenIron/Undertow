using BepInEx.Configuration;

namespace RavenIron.Undertow.Config
{
    /// <summary>
    /// Config surface. Expected to grow — current strength, tide period, flotsam caps and the
    /// swimmer dial all belong here as their tasks land.
    ///
    /// Three conventions worth keeping:
    ///
    /// 1. Every system gets its own on/off toggle from day one. That is what makes incremental
    ///    testing possible (build one system, disable the rest) and lets server owners adopt
    ///    part of the mod without all of it.
    ///
    /// 2. Clamp on READ as well as on write. Config files get hand-edited, and a value validated
    ///    only for a floor and not a ceiling has already taken a production service down for six
    ///    hours in this codebase's history. AcceptableValueRange handles the write side; anything
    ///    consumed in a loop should be re-clamped where it is used.
    ///
    /// 3. TUNING VALUES ARRIVE WITH THEIR CONSUMER, not before. A bound entry nothing reads is
    ///    a promise to a server owner that the mod does not keep. CurrentStrength therefore
    ///    lands with task 1, not here — the skeleton binds toggles and the tick budget only.
    /// </summary>
    public static class ModConfig
    {
        // ---- Core ----------------------------------------------------------------------
        public static ConfigEntry<float> TickBudgetMs;
        public static ConfigEntry<bool>  VerboseLogging;

        // ---- System toggles -------------------------------------------------------------
        // One per planned system, bound from day one so a half-built mod is testable a piece
        // at a time. Each defaults to the state that is correct while its task is unbuilt.
        public static ConfigEntry<bool> EnableDrift;
        public static ConfigEntry<bool> EnableFlotsam;
        public static ConfigEntry<bool> EnableSwimmers;
        public static ConfigEntry<bool> EnableWrathBridge;

        // ---- The current ----------------------------------------------------------------
        // These arrived with task 1, which is the first thing that reads them. Keep that rule:
        // a bound entry nothing consumes is a promise to a server owner the mod does not keep.
        public static ConfigEntry<float> MaxCurrentSpeed;
        public static ConfigEntry<float> TidePeriodSeconds;
        public static ConfigEntry<float> TideAmplitude;
        public static ConfigEntry<float> CoastalStrength;
        public static ConfigEntry<float> StormSurgeMultiplier;

        // ---- Drift (task 2) --------------------------------------------------------------
        public static ConfigEntry<float> DriftStrength;
        public static ConfigEntry<float> UnattendedDriftFactor;
        public static ConfigEntry<float> FieldRefreshSeconds;

        // ---- Flotsam (task 4) ------------------------------------------------------------
        public static ConfigEntry<float>  FlotsamIntervalSeconds;
        public static ConfigEntry<float>  FlotsamPerHour;
        public static ConfigEntry<float>  FlotsamMinDepth;
        public static ConfigEntry<float>  FlotsamRingMinMeters;
        public static ConfigEntry<float>  FlotsamRingMaxMeters;
        public static ConfigEntry<int>    FlotsamMaxAlive;
        public static ConfigEntry<float>  FlotsamTtlSeconds;
        public static ConfigEntry<float>  FlotsamRareChance;
        public static ConfigEntry<string> FlotsamCommon;
        public static ConfigEntry<string> FlotsamRare;
        public static ConfigEntry<string> FlotsamWreckage;

        // ---- Swimmers (task 5) -----------------------------------------------------------
        public static ConfigEntry<float> SwimmerDriftFactor;
        public static ConfigEntry<float> SwimmerMaxShareOfSwimSpeed;

        public static void Bind(ConfigFile cfg)
        {
            const string core = "1 - Core";

            TickBudgetMs = cfg.Bind(core, "TickBudgetMs", 2.0f,
                new ConfigDescription(
                    "Milliseconds per frame SeaTick may spend across all ambient systems " +
                    "combined. Work that does not fit resumes next frame. Raise only if systems " +
                    "are visibly falling behind on a server with headroom. Does not affect the " +
                    "current's effect on boats, which is applied in the physics step by the " +
                    "machine that owns each hull.",
                    new AcceptableValueRange<float>(0.25f, 16.0f)));

            VerboseLogging = cfg.Bind(core, "VerboseLogging", false,
                "Log every system pass rather than summaries. This mod's work is invisible by " +
                "design — a current has no sound and no icon — so this is how you see it running.");

            const string systems = "2 - Systems";

            EnableDrift = cfg.Bind(systems, "EnableDrift", true,
                "Master switch for the current's effect on boats. With this off the field is " +
                "still computed and still readable through `wake here`, but nothing is pushed. " +
                "Unbuilt as of 0.1.0.");

            EnableFlotsam = cfg.Bind(systems, "EnableFlotsam", true,
                "Driftwood and cargo collecting in slack water. Server-authoritative and " +
                "budgeted; spawns only near a real player, so nothing accumulates in unloaded " +
                "ocean. Unbuilt as of 0.1.0.");

            EnableSwimmers = cfg.Bind(systems, "EnableSwimmers", true,
                "Whether the current also carries a swimming player. Capped well below swim " +
                "speed so a swimmer can always make headway against it. Unbuilt as of 0.1.0.");

            EnableWrathBridge = cfg.Bind(systems, "EnableWrathBridge", true,
                "Read Ragnarok's Wrath when it is installed: storms raise the sea where they " +
                "stand, and the season shifts the drift. Harmless with RW absent - the bridge " +
                "logs the absence once and stays dormant, and the sea runs regardless.");

            const string current = "3 - The current";

            MaxCurrentSpeed = cfg.Bind(current, "MaxCurrentSpeed", 1.2f,
                new ConfigDescription(
                    "Ceiling on water speed anywhere in the world, in metres per second. THE " +
                    "dial: everything else in this section is a shape, and this is the size. A " +
                    "karve at half sail makes roughly 5-6 m/s, so the default is about a fifth " +
                    "of that — enough to shape every voyage and never enough to forbid one. " +
                    "Raise it for a harsher sea; past about 2.5 the current stops being a " +
                    "correction and starts being a tow.",
                    new AcceptableValueRange<float>(0f, 5f)));

            TidePeriodSeconds = cfg.Bind(current, "TidePeriodSeconds", 3600f,
                new ConfigDescription(
                    "Seconds for one full flood-ebb cycle. The default is two in-game days at " +
                    "vanilla's 1800-second day. The tide swings how hard the open ocean runs and " +
                    "reverses the direction of the coastal stream, so this is how often the " +
                    "passage you know changes its mind.",
                    new AcceptableValueRange<float>(300f, 86400f)));

            TideAmplitude = cfg.Bind(current, "TideAmplitude", 0.25f,
                new ConfigDescription(
                    "How much the tide swings open-ocean strength, as a share. 0.25 means the " +
                    "drift runs a quarter stronger at peak flood than at slack. Zero gives a " +
                    "tideless ocean whose coastal streams still reverse.",
                    new AcceptableValueRange<float>(0f, 1f)));

            CoastalStrength = cfg.Bind(current, "CoastalStrength", 0.8f,
                new ConfigDescription(
                    "Share of MaxCurrentSpeed the shore-parallel stream may reach on the shelf. " +
                    "This is the near-land half of the mod — the reason a lee shore is dangerous " +
                    "and the reason a headland has slack water behind it. Set 0 for open-ocean " +
                    "drift only.",
                    new AcceptableValueRange<float>(0f, 2f)));

            StormSurgeMultiplier = cfg.Bind(current, "StormSurgeMultiplier", 1.6f,
                new ConfigDescription(
                    "How much harder the water runs where a Ragnarok's Wrath storm is standing. " +
                    "Positional, not global: the sea rises under the storm and nowhere else, so a " +
                    "sheltered passage stops being sheltered while the storm sits over it. " +
                    "Applied before MaxCurrentSpeed, so a storm drives weak water toward the " +
                    "ceiling rather than through it. Ignored entirely without Ragnarok's Wrath.",
                    new AcceptableValueRange<float>(1f, 4f)));

            const string drift = "4 - Drift";

            DriftStrength = cfg.Bind(drift, "DriftStrength", 1.0f,
                new ConfigDescription(
                    "How hard the water grips a hull, per second. This sets how QUICKLY a boat " +
                    "takes up the water's speed, NOT how fast it ends up going: a drifting hull " +
                    "settles at the water's own speed no matter what this is, because the push " +
                    "fades out as the boat catches up. So the number `wake here` prints is the " +
                    "speed you will actually drift, on any hull — a raft, a karve and a longship " +
                    "all agree. Raise it for a sea that grabs a boat the moment it stops rowing; " +
                    "lower it for a hull that takes its time.",
                    new AcceptableValueRange<float>(0.05f, 4f)));

            UnattendedDriftFactor = cfg.Bind(drift, "UnattendedDriftFactor", 0f,
                new ConfigDescription(
                    "Share of the current that acts on a boat with nobody aboard. DEFAULT ZERO, " +
                    "and that is a design decision rather than a timid default: vanilla already " +
                    "damps an unmanned hull's horizontal speed to a tenth each tick and forces it " +
                    "to Stop, and a moored longship that wanders off while its owner is away " +
                    "reads as the mod stealing a boat. Raise it only if your crew moors properly " +
                    "and wants the sea to punish them when they do not.",
                    new AcceptableValueRange<float>(0f, 1f)));

            FieldRefreshSeconds = cfg.Bind(drift, "FieldRefreshSeconds", 0.25f,
                new ConfigDescription(
                    "Seconds between recalculations of the current under each boat. The field is " +
                    "smooth at the scale of a hull — its shortest feature is nearly two " +
                    "kilometres across — so recomputing it every physics tick buys nothing and " +
                    "costs nine terrain samples per boat per tick. Lower it only if you can " +
                    "measure a difference.",
                    new AcceptableValueRange<float>(0f, 5f)));

            const string flotsam = "5 - Flotsam";

            FlotsamIntervalSeconds = cfg.Bind(flotsam, "FlotsamIntervalSeconds", 20f,
                new ConfigDescription(
                    "Seconds between flotsam passes. One candidate point is tested per player " +
                    "per pass, so this is the coarse dial for how busy the sea feels.",
                    new AcceptableValueRange<float>(2f, 600f)));

            FlotsamPerHour = cfg.Bind(flotsam, "FlotsamPerHour", 6f,
                new ConfigDescription(
                    "Pieces of flotsam per hour per player, in perfectly slack water - and " +
                    "scaled down sharply as the water runs faster, so most candidate points " +
                    "produce nothing. Expressed per HOUR so the number survives a change to " +
                    "FlotsamIntervalSeconds; a rate that silently means something else when a " +
                    "neighbouring constant moves is a trap.",
                    new AcceptableValueRange<float>(0f, 120f)));

            FlotsamMinDepth = cfg.Bind(flotsam, "FlotsamMinDepth", 12f,
                new ConfigDescription(
                    "Flotsam needs at least this much water under it. Keeps driftwood off the " +
                    "shallows where a player would find it by walking rather than by sailing.",
                    new AcceptableValueRange<float>(0f, 30f)));

            FlotsamRingMinMeters = cfg.Bind(flotsam, "FlotsamRingMinMeters", 30f,
                new ConfigDescription(
                    "Nearest flotsam may appear to a player. Far enough that it does not pop into " +
                    "existence under their nose.",
                    new AcceptableValueRange<float>(5f, 200f)));

            FlotsamRingMaxMeters = cfg.Bind(flotsam, "FlotsamRingMaxMeters", 90f,
                new ConfigDescription(
                    "Furthest flotsam may appear. Keep it inside the loaded zone around a player, " +
                    "or the object spawns where nothing is watching.",
                    new AcceptableValueRange<float>(10f, 300f)));

            FlotsamMaxAlive = cfg.Bind(flotsam, "FlotsamMaxAlive", 12,
                new ConfigDescription(
                    "Hard ceiling on flotsam this server has spawned and not yet reclaimed. THE " +
                    "safety valve: the whole risk of this system is a long-running world quietly " +
                    "filling its ZDO table with driftwood, and this is what forbids it.",
                    new AcceptableValueRange<int>(0, 100)));

            FlotsamTtlSeconds = cfg.Bind(flotsam, "FlotsamTtlSeconds", 1800f,
                new ConfigDescription(
                    "Seconds before unclaimed flotsam is reclaimed. Without this the cap becomes " +
                    "a permanent ceiling of abandoned driftwood rather than a rolling one. Set 0 " +
                    "to never reclaim, and accept that the sea fills up to the cap and stays there.",
                    new AcceptableValueRange<float>(0f, 86400f)));

            FlotsamRareChance = cfg.Bind(flotsam, "FlotsamRareChance", 0.04f,
                new ConfigDescription(
                    "Chance a piece of flotsam comes from the rare table instead. Low on purpose: " +
                    "the point of a prize is that it is not the usual thing.",
                    new AcceptableValueRange<float>(0f, 1f)));

            FlotsamCommon = cfg.Bind(flotsam, "FlotsamCommon",
                "Wood,RoundLog,FineWood,ElderBark,FirCone,PineCone,Root",
                "What usually washes up, comma separated. MUST be prefabs that carry a Floating " +
                "component or they sink out of reach - run `wake floats` for the list the game " +
                "actually has (123 of 1090 items, measured). Note that Floating is Valheim's " +
                "loss-prevention marker rather than buoyancy, so an iron shield floats and ore " +
                "does not: choose for the story, not for physics.");

            FlotsamRare = cfg.Bind(flotsam, "FlotsamRare",
                "DragonTear,Wishbone,MeadSwimmer,Demister",
                "The occasional prize. Same Floating rule applies.");

            FlotsamWreckage = cfg.Bind(flotsam, "FlotsamWreckage",
                "ShieldWood,SpearWood,Club,BowFineWood,FishingRod,SerpentMeat,FishingBaitOcean",
                "What washes up instead while a Ragnarok's Wrath storm stands over that water. " +
                "This is where the sea gets a voice without a line of UI. Ignored without RW.");

            const string swimmers = "6 - Swimmers";

            SwimmerDriftFactor = cfg.Bind(swimmers, "SwimmerDriftFactor", 0.5f,
                new ConfigDescription(
                    "Share of the water's speed that carries a swimming player. Half by default: " +
                    "a body in the water is not a hull, and being set gently down the coast is " +
                    "atmosphere. Set 0 to leave swimmers alone entirely.",
                    new AcceptableValueRange<float>(0f, 1f)));

            SwimmerMaxShareOfSwimSpeed = cfg.Bind(swimmers, "SwimmerMaxShareOfSwimSpeed", 0.35f,
                new ConfigDescription(
                    "Hard ceiling on swimmer drift, as a share of that character's own swim speed. " +
                    "THIS IS A SAFETY PROPERTY, NOT A BALANCE DIAL: if a player can be held " +
                    "offshore by the current until they drown, the feature is wrong rather than " +
                    "mistuned. At the default a swimmer always makes headway against the worst " +
                    "water in the world. Raising it toward 1.0 approaches the point where they " +
                    "cannot, and above that they simply lose.",
                    new AcceptableValueRange<float>(0f, 0.9f)));
        }
    }
}
