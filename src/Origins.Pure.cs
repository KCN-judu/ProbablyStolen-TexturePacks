using System;
using System.Collections.Generic;

namespace TexturePacks
{
    // A pure file (no Unity types): tested headless by tests/PureTest.cs and tests/DiffTest.cs.
    // How the mod learns the origin (see Origin) of another mod without reading any file: the mod either ships a
    // 1x1 marker picture Textures/origin.<uuid>.png, which the game already decodes into LoadedMod.Textures under the
    // key "origin.<uuid>", or announces the UUID from its code (ProviderBridge). A mod that does neither is
    // identified by its mod id, which works without protection against two mods sharing an id.

    public static class OriginMarker
    {
        const string Prefix = "origin.";

        // The Textures key (file name without ".png") of the marker picture for a UUID.
        public static string KeyFor(Guid uuid) { return Prefix + uuid.ToString("D"); }

        // False for every key that is not "origin." and a hyphenated UUID.
        public static bool TryParse(string textureKey, out Guid uuid)
        {
            uuid = Guid.Empty;
            if (textureKey == null || textureKey.Length != Prefix.Length + 36 || !textureKey.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            return Guid.TryParseExact(textureKey.Substring(Prefix.Length), "D", out uuid);
        }
    }

    // The origin chosen for one mod, and what is wrong with how it was declared (null when nothing is).
    public sealed class OriginInfo
    {
        public readonly Origin Origin;
        public readonly string Problem;
        public OriginInfo(Origin origin, string problem) { Origin = origin; Problem = problem; }
    }

    public static class OriginResolver
    {
        // The origin of a mod from what it declares. `declared` is the UUID it announced from code, if any.
        //   a declaration from code wins; markers that disagree with it are a problem;
        //   otherwise one marker gives the UUID; several different markers are a problem and give no UUID;
        //   with nothing declared the mod id stands in. The origin is null only for a mod id the game would refuse.
        public static OriginInfo Resolve(string modId, IEnumerable<string> textureKeys, Guid? declared)
        {
            var markers = new List<Guid>();
            if (textureKeys != null)
                foreach (string key in textureKeys)
                {
                    Guid uuid;
                    if (OriginMarker.TryParse(key, out uuid) && !markers.Contains(uuid)) markers.Add(uuid);
                }
            markers.Sort();

            if (declared.HasValue)
            {
                bool agree = markers.Count == 0 || (markers.Count == 1 && markers[0] == declared.Value);
                return new OriginInfo(Origin.FromUuid(declared.Value),
                    agree ? null : "the origin marker picture names another UUID than the one the mod declares");
            }
            if (markers.Count == 1) return new OriginInfo(Origin.FromUuid(markers[0]), null);
            if (markers.Count > 1) return new OriginInfo(Origin.FromModId(modId), "more than one origin marker picture; the mod id is used");
            return new OriginInfo(Origin.FromModId(modId), null);
        }
    }
}
