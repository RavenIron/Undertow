using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RavenIron.Undertow.Core
{
    /// <summary>
    /// Which vanilla item prefabs actually float.
    ///
    /// THE QUESTION THAT BLOCKS FLOTSAM, and it is the one kind of question no decompile can
    /// answer: whether a `Floating` component is attached to a prefab lives in Unity asset data,
    /// not in the assembly. The design assumed dropped items bob on the surface and that flotsam
    /// could therefore be built from vanilla `ItemDrop`s with no new prefabs â€” which is a hard
    /// requirement, because an unresolvable prefab hash sends `ZNetScene.CreateObjectsSorted`
    /// into `DestroyZDO`. If nothing floats, one raft of sunken loot disproves the whole approach
    /// and task 4 needs a different design.
    ///
    /// So this asks the game instead of guessing. It runs headless, which matters: the answer is
    /// a property of the loaded prefabs, and a dedicated server loads them all.
    ///
    /// `ObjectDB.instance.m_items`, `ZNetScene.instance.m_prefabs` and `Floating` are all public
    /// in the shipping assembly â€” verified against the non-publicized DLL, per house rule 5.
    /// </summary>
    public static class FloatScan
    {
        public struct Result
        {
            public int Scanned;
            public List<string> Floats;
            public string Source;
        }

        /// <summary>
        /// Scan every known item prefab for a <c>Floating</c> component.
        ///
        /// Prefers ObjectDB (items only, which is what flotsam would spawn) and falls back to
        /// ZNetScene's full prefab list, so the answer is available whichever is up first.
        /// Searches children too: a component sitting on a child of the prefab root still floats
        /// the object, and assuming otherwise would produce a confident false negative.
        /// </summary>
        public static bool TryScan(out Result result)
        {
            result = new Result { Floats = new List<string>() };

            List<GameObject> prefabs = null;

            ObjectDB odb = ObjectDB.instance;
            if (odb != null && odb.m_items != null && odb.m_items.Count > 0)
            {
                prefabs = odb.m_items;
                result.Source = "ObjectDB.m_items";
            }
            else
            {
                ZNetScene scene = ZNetScene.instance;
                if (scene == null || scene.m_prefabs == null || scene.m_prefabs.Count == 0) return false;
                prefabs = scene.m_prefabs;
                result.Source = "ZNetScene.m_prefabs";
            }

            for (int i = 0; i < prefabs.Count; i++)
            {
                GameObject go = prefabs[i];
                if (go == null) continue;

                result.Scanned++;
                if (go.GetComponentInChildren<Floating>(true) != null)
                    result.Floats.Add(go.name);
            }

            result.Floats.Sort(System.StringComparer.OrdinalIgnoreCase);
            return true;
        }

        /// <summary>One line plus the names, capped so a 500-prefab world does not flood a log.</summary>
        public static string Describe(int maxNames = 200)
        {
            if (!TryScan(out Result r))
                return "float scan: no prefab list yet (ObjectDB and ZNetScene both empty).";

            var sb = new StringBuilder(512);
            sb.Append($"float scan ({r.Source}): {r.Floats.Count} of {r.Scanned} prefabs carry Floating");

            if (r.Floats.Count == 0)
            {
                sb.Append("\n  NOTHING FLOATS â€” flotsam cannot be built from vanilla item drops. " +
                          "Task 4 needs a different design.");
                return sb.ToString();
            }

            int shown = r.Floats.Count < maxNames ? r.Floats.Count : maxNames;
            for (int i = 0; i < shown; i++)
            {
                if (i % 6 == 0) sb.Append("\n  ");
                sb.Append(r.Floats[i]).Append("  ");
            }
            if (r.Floats.Count > shown) sb.Append($"\n  ... and {r.Floats.Count - shown} more");

            return sb.ToString();
        }
    }
}
