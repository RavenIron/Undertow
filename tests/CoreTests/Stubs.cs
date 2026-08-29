// Hand-written stand-ins for the handful of BepInEx types the tested source mentions in its
// signatures. Deliberately minimal: the stub surface is almost always smaller than it looks.
// Nothing here needs to behave like BepInEx — it only needs to compile and let the real logic
// run.
//
// At 0.1.0 the only shipping file under test is ModConfig, which touches nothing else. When
// CurrentField lands (task 1) this file grows a UnityEngine.Vector3 and little else: the field
// is pure arithmetic on coordinates by design, precisely so it can be tested here.

using System.Collections.Generic;

namespace BepInEx.Configuration
{
    public class AcceptableValueRange<T>
    {
        public readonly T MinValue, MaxValue;
        public AcceptableValueRange(T min, T max) { MinValue = min; MaxValue = max; }
    }

    public class ConfigDescription
    {
        public readonly string Description;
        public readonly object AcceptableValues;

        public ConfigDescription(string description, object acceptableValues = null)
        {
            Description = description;
            AcceptableValues = acceptableValues;
        }
    }

    public class ConfigEntry<T>
    {
        public T Value { get; set; }
        public ConfigEntry(T defaultValue) { Value = defaultValue; }
    }

    /// <summary>
    /// Records what was bound rather than only counting it. The harness asserts on the
    /// recorded entries — a duplicate section/key pair is a real and silent bug in BepInEx
    /// (the second Bind returns the FIRST entry, so two config fields quietly share one
    /// value), and it is invisible without a record of what was asked for.
    /// </summary>
    public class ConfigFile
    {
        public sealed class BoundEntry
        {
            public string Section;
            public string Key;
            public object DefaultValue;
            public ConfigDescription Description;
            public string Path => Section + "/" + Key;
        }

        public readonly List<BoundEntry> Bound = new List<BoundEntry>();

        public int BoundCount => Bound.Count;

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue,
                                      ConfigDescription description = null)
        {
            Bound.Add(new BoundEntry
            {
                Section = section,
                Key = key,
                DefaultValue = defaultValue,
                Description = description
            });
            return new ConfigEntry<T>(defaultValue);
        }

        public ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description)
            => Bind(section, key, defaultValue, new ConfigDescription(description));
    }
}
