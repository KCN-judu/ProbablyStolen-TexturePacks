using System;
using System.Collections.Generic;
using UnityEngine;

namespace TexturePacks
{
    // How a mod that makes sprites in code (an asset bundle, Sprite.Create) lets packs replace them. The mod is the
    // provider; this class is the reader. Two slots of the AppDomain, which every assembly shares, carry the contract,
    // so the provider needs no reference to this mod and no shared assembly, and works the same whether or not
    // Texture Packs is installed (the game resolves a mod's assembly references inside that mod's own folder only, so
    // a shared API assembly would not bind). The provider's side is template/TexturePacksClient.cs.
    //
    //   texture_packs.v1.sprites.<modId>   Func<IEnumerable<object[]>>   one row per picture: { string container,
    //                                      string name, Sprite original, Action<Sprite> apply }
    //   texture_packs.v1.origin.<modId>    string                        the mod's UUID, if it declares one
    //
    // The provider keeps owning its objects. `apply` is called with the pack's sprite, or with the original to put
    // the mod's own back. The slots are read, never written: the provider decides when its list changes, and this
    // mod reads it again on every Rebuild, so it does not matter who started first. A row whose container and name
    // are those of a picture the game decoded from the mod's folders is a code-made picture too; the one offered
    // from code wins (see ExternalAssets.Find).
    static class ProviderBridge
    {
        const string SpritesPrefix = "texture_packs.v1.sprites.";
        const string OriginPrefix = "texture_packs.v1.origin.";

        sealed class Target : ExternalTarget
        {
            readonly Action<Sprite> apply;
            public Target(Sprite original, Action<Sprite> apply) : base(original) { this.apply = apply; }
            public override void Set(Sprite sprite) { apply(sprite); }

            // The same original handed to the same delegate. A provider that makes a new delegate on every listing
            // gets its pictures put in again on every plan, which is correct, only not free.
            public override bool Same(ExternalTarget other)
            {
                var o = other as Target;
                return o != null && o.Original == Original && Equals(o.apply, apply);
            }
        }

        // The rows of each mod, read once per Rebuild (Begin) and looked up by container and name.
        static readonly Dictionary<string, Dictionary<string, Target>> rows = new Dictionary<string, Dictionary<string, Target>>();
        static readonly HashSet<string> warned = new HashSet<string>();

        // Whether the mod offers pictures, for deciding that a finished mod needs a Rebuild.
        public static bool Publishes(string modId)
        {
            return AppDomain.CurrentDomain.GetData(SpritesPrefix + modId) is Func<IEnumerable<object[]>>;
        }

        // The UUID a mod declares from code, or null.
        public static Guid? DeclaredOrigin(string modId)
        {
            var text = AppDomain.CurrentDomain.GetData(OriginPrefix + modId) as string;
            Guid uuid;
            return text != null && Guid.TryParse(text, out uuid) ? (Guid?)uuid : null;
        }

        public static void Begin() { rows.Clear(); }

        public static void Clear() { rows.Clear(); }

        public static ExternalTarget Find(LoadedMod mod, Locator locator)
        {
            if (!locator.IsModAsset) return null;
            string id = mod.Manifest.ID;
            Dictionary<string, Target> mine;
            if (!rows.TryGetValue(id, out mine)) rows[id] = mine = Read(id);
            Target target;
            return mine.TryGetValue(locator.Path + "/" + locator.Name, out target) ? target : null;
        }

        // The mod's rows, with the malformed ones left out. The provider's code runs here, so a failure is a warning.
        static Dictionary<string, Target> Read(string modId)
        {
            var found = new Dictionary<string, Target>();
            var list = AppDomain.CurrentDomain.GetData(SpritesPrefix + modId) as Func<IEnumerable<object[]>>;
            if (list == null) return found;
            try
            {
                foreach (object[] row in list())
                {
                    var container = row != null && row.Length >= 4 ? row[0] as string : null;
                    var name = row != null && row.Length >= 4 ? row[1] as string : null;
                    var original = row != null && row.Length >= 4 ? row[2] as Sprite : null;
                    var apply = row != null && row.Length >= 4 ? row[3] as Action<Sprite> : null;
                    if (!Locator.IsContainer(container) || string.IsNullOrEmpty(name) || original == null || apply == null)
                    {
                        Warn(modId, "a picture it offers is malformed (container, name, original sprite and apply are all needed)");
                        continue;
                    }
                    string key = container + "/" + name;
                    if (found.ContainsKey(key)) Warn(modId, "offers " + key + " more than once; the first is used");
                    else found[key] = new Target(original, apply);
                }
            }
            catch (Exception e) { Warn(modId, "could not list its pictures: " + e.Message); }
            return found;
        }

        static void Warn(string modId, string problem)
        {
            if (warned.Add(modId + "|" + problem)) Debug.LogWarning(TexturePacksMod.Tag + modId + ": " + problem);
        }
    }
}
