using System;
using System.Collections.Generic;
using UnityEngine;

namespace TexturePacks
{
    // One picture of another mod that a pack can replace, wherever that mod keeps it: in the Sprites or PaperSprites
    // the game decoded from its folders (ModSprites), or in a sprite its code makes and offers (ProviderBridge).
    // Packs only sees this class; how a replacement reaches the mod's picture is the target's own business.
    abstract class ExternalTarget
    {
        public readonly Sprite Original;    // the mod's own sprite: the size a pack file must have, and what Set(Original) restores

        protected ExternalTarget(Sprite original) { Original = original; }

        // Puts a sprite where the mod's picture is read from. Called with the pack's sprite, and with Original to
        // put the mod's own back.
        public abstract void Set(Sprite sprite);
    }

    // The pictures of other mods, found by identity and replaced for as long as the packs that choose them are on.
    //
    // The whole state is the list of targets that hold a replacement now and the sprites made for them. Clear puts
    // every original back first, so each Rebuild starts from the mods' own pictures. Nothing here is a store object:
    // the targets point at mod assets (sprites, textures, a mod's own delegates), and Clear empties it all when the
    // mod is disabled.
    static class ExternalAssets
    {
        static readonly List<ExternalTarget> replaced = new List<ExternalTarget>();
        static readonly List<Sprite> made = new List<Sprite>();

        public static int Count { get { return replaced.Count; } }

        // Reads, once per Rebuild, which mod has which origin (ModOrigins) and what the mods offer in code.
        public static void Begin()
        {
            ModOrigins.Rebuild();
            ProviderBridge.Begin();
        }

        // The target for an identity of a mod, or null when no enabled mod has that origin or the mod has no such
        // picture. A picture a mod offers from code wins over one from its folders.
        public static ExternalTarget Find(AssetId id)
        {
            LoadedMod mod = ModOrigins.Find(id.Origin);
            if (mod == null) return null;
            return ProviderBridge.Find(mod, id.Locator) ?? ModSprites.Find(mod, id.Locator);
        }

        // Why Find found nothing, for the warning in Player.log.
        public static string Missing(AssetId id)
        {
            return ModOrigins.Find(id.Origin) == null
                ? "no enabled mod has the origin " + id.Origin
                : "the mod has no picture " + id.Locator.Path + "/" + id.Locator.Name;
        }

        // Puts a sprite in place of the target's picture. A provider's own code runs here, so a failure is logged
        // and leaves the picture as it was.
        public static bool Replace(ExternalTarget target, Sprite sprite)
        {
            try { target.Set(sprite); }
            catch (Exception e)
            {
                Debug.LogWarning(TexturePacksMod.Tag + "could not replace a picture of another mod: " + e.Message);
                return false;
            }
            replaced.Add(target);
            made.Add(sprite);
            return true;
        }

        // Puts every original back. The replacement sprites go to `retired`, which Packs destroys once the scene
        // that may still show them is gone.
        public static void Clear(List<Sprite> retired)
        {
            foreach (var target in replaced)
            {
                try { target.Set(target.Original); }
                catch (Exception e) { Debug.LogWarning(TexturePacksMod.Tag + "could not restore a picture of another mod: " + e.Message); }
            }
            replaced.Clear();
            retired.AddRange(made);
            made.Clear();
            ModSprites.Clear();
            ProviderBridge.Clear();
            ModOrigins.Clear();
        }
    }
}
