using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TexturePacks
{
    // Which enabled mod has which origin (see Origin). The decision is OriginResolver's, from the marker pictures in
    // the mod's LoadedMod.Textures and the UUID the mod announces from code; this class only feeds it the loaded mods
    // and keeps the answer for one Rebuild. It holds LoadedMod objects, which are not store objects, and Clear
    // empties it.
    static class ModOrigins
    {
        static readonly Dictionary<Origin, LoadedMod> byOrigin = new Dictionary<Origin, LoadedMod>();
        static readonly HashSet<string> warned = new HashSet<string>();

        // The origin of one mod from what it has declared so far, and what is wrong with the declaration.
        static OriginInfo Describe(LoadedMod mod)
        {
            string id = mod.Manifest.ID;
            return OriginResolver.Resolve(id, mod.Textures == null ? null : mod.Textures.Keys.ToList(), ProviderBridge.DeclaredOrigin(id));
        }

        // The origin of a mod, or null; no warning. For asking about a mod that has just finished loading.
        public static Origin OriginOf(LoadedMod mod)
        {
            return mod == null || mod.Manifest == null ? null : Describe(mod).Origin;
        }

        public static void Rebuild()
        {
            byOrigin.Clear();
            foreach (var mod in ModLoader.Mods)
            {
                if (mod == null || !mod.IsEnabled || mod.Manifest == null) continue;
                string id = mod.Manifest.ID;
                var info = Describe(mod);
                if (info.Problem != null) Warn(id, info.Problem);
                if (info.Origin == null) continue;
                LoadedMod other;
                if (byOrigin.TryGetValue(info.Origin, out other))
                {
                    Warn(id, "has the same origin as " + other.Manifest.ID + ", which is used");
                    continue;
                }
                byOrigin[info.Origin] = mod;
            }
        }

        public static LoadedMod Find(Origin origin)
        {
            LoadedMod mod;
            return byOrigin.TryGetValue(origin, out mod) ? mod : null;
        }

        public static void Clear() { byOrigin.Clear(); }

        // One warning per mod and problem, so a rebuild does not repeat it.
        static void Warn(string modId, string problem)
        {
            if (warned.Add(modId + "|" + problem)) Debug.LogWarning(TexturePacksMod.Tag + modId + ": " + problem);
        }
    }
}
