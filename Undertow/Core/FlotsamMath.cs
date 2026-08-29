using System;

namespace RavenIron.Undertow.Core
{
    /// <summary>
    /// Where flotsam gathers and how much of it. Pure arithmetic — no Unity, no game types, no
    /// clock — so the harness tests the shipping source.
    ///
    /// THE IDEA: currents converge, so things collect. Slack water and eddies are already
    /// meaningful places in this mod — they are where the field cancels — and this gives a
    /// sailor a second reason to know where they are. A player who has learned that the water
    /// goes dead behind a certain headland now has a reason to go there.
    ///
    /// NOTHING HERE SPAWNS ANYTHING. It decides whether a point qualifies and how strongly, and
    /// the system above it does the spawning. That split is what lets the interesting half —
    /// "is this water slack enough, deep enough, and far enough from shore" — be tested without
    /// launching Valheim.
    /// </summary>
    public static class FlotsamMath
    {
        /// <summary>
        /// How strongly flotsam gathers at a sample, 0..1. Zero means it does not gather here.
        ///
        /// Slack water scores highest. That is the whole mechanic: the mod already makes slack
        /// water a place, and this makes it a place worth visiting.
        /// </summary>
        /// <param name="maxSpeed">The world's current ceiling, so "slack" is relative.</param>
        /// <param name="minDepth">
        /// Below this, no flotsam. Keeps it off the shallows where a player would find it by
        /// walking rather than by sailing, and away from the waterline fade where the field is
        /// least meaningful.
        /// </param>
        public static float GatherWeight(in FieldSample sample, float maxSpeed, float minDepth)
        {
            if (sample.Depth <= minDepth) return 0f;
            if (maxSpeed <= 0f) return 0f;

            // Relative slackness: 1 in dead water, 0 at the world's fastest.
            float slack = 1f - Clamp01(sample.Speed / maxSpeed);

            // Squared so the tail is sharp. A gentle falloff would scatter flotsam across half
            // the ocean and make slack water no more special than anywhere else, which defeats
            // the point of tying it to the field at all.
            return slack * slack;
        }

        /// <summary>
        /// Whether to spawn this tick, given a gather weight and a per-hour rate.
        ///
        /// Rate is expressed PER HOUR rather than per tick so the config value survives a change
        /// to the tick interval. A tuning number that silently means something different when a
        /// neighbouring constant moves is a trap this codebase has been bitten by before.
        /// </summary>
        public static bool ShouldSpawn(float gatherWeight, float perHour, float deltaSeconds, float roll01)
        {
            if (gatherWeight <= 0f || perHour <= 0f || deltaSeconds <= 0f) return false;

            float expected = perHour * (deltaSeconds / 3600f) * gatherWeight;
            if (expected <= 0f) return false;
            if (expected >= 1f) return true;      // saturated: always spawn

            return roll01 < expected;
        }

        /// <summary>
        /// Pick an index from a weight table using a 0..1 roll. Returns -1 for an empty or
        /// zero-weight table rather than throwing or silently picking the first entry.
        /// </summary>
        public static int PickWeighted(float[] weights, float roll01)
        {
            if (weights == null || weights.Length == 0) return -1;

            float total = 0f;
            for (int i = 0; i < weights.Length; i++)
                if (weights[i] > 0f) total += weights[i];

            if (total <= 0f) return -1;

            float target = Clamp01(roll01) * total;
            float running = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0f) continue;
                running += weights[i];
                if (target < running) return i;
            }

            // Only reachable on floating-point drift at the very top of the range.
            for (int i = weights.Length - 1; i >= 0; i--)
                if (weights[i] > 0f) return i;
            return -1;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
