namespace RavenIron.Undertow.Core
{
    /// <summary>
    /// Every ambient system implements this and registers with <see cref="SeaTick"/>.
    ///
    /// Systems do not own timers, coroutines, or Update methods of their own. SeaTick decides
    /// when they run and how much time they may spend, which is what makes global throttling
    /// possible from one place instead of several.
    ///
    /// NOTE the scope: this interface is for ambient, server-authoritative work — flotsam and
    /// whatever follows it. The current's effect on a boat is NOT a system; it is a Harmony
    /// postfix that runs on whichever machine owns the boat. Do not try to route that through
    /// here, because the machine that owns the hull is usually not the machine SeaTick ticks on.
    /// </summary>
    public interface IWorldSystem
    {
        /// <summary>Log-facing name. Also the config section name.</summary>
        string Name { get; }

        /// <summary>
        /// Config-backed master switch for this system. Checked every tick, not cached, so an
        /// admin toggling it at runtime takes effect without a restart.
        /// </summary>
        bool Enabled { get; }

        /// <summary>Desired seconds between ticks. SeaTick may run it later under load, never sooner.</summary>
        float IntervalSeconds { get; }

        /// <summary>
        /// Called once at load, after config binding and after ZNet exists. Do resolution work
        /// here, not in a constructor.
        /// </summary>
        void Initialise();

        /// <summary>
        /// One pass. <paramref name="deltaSeconds"/> is the real time since this system's own
        /// previous tick — not frame delta, and not the same value its neighbours receive.
        ///
        /// Must return promptly. Anything expensive belongs behind a cursor that resumes on the
        /// next tick rather than a loop that finishes inside one.
        /// </summary>
        void Tick(float deltaSeconds);
    }
}
