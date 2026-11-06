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

        // Whether a choice put in place on `other` is still in place on this one (see PlanDiff): the same place in
        // the mod, with the same original.
        public abstract bool Same(ExternalTarget other);
    }

    // The pictures of other mods, found by identity. Packs keeps which of them hold a pack's sprite (as choices of
    // its plan) and calls Set and Restore here; this class only finds the targets and runs the mods' code safely.
    // Nothing here is a store object: the targets point at mod assets (sprites, a mod's own delegates), and Clear
    // empties it all when the mod is disabled.
    static class ExternalAssets
    {
        // Whether ModOrigins has been read for the current plan.
        static bool originsRead;

        // Starts a plan: which mod has which origin (ModOrigins) and what the mods offer in code are read again,
        // when first asked. Reading the origins goes through every texture name of every enabled mod, the packs'
        // own included, so a plan in which no pack names a picture of another mod never reads them.
        public static void Begin()
        {
            originsRead = false;
            ProviderBridge.Begin();
        }

        static void ReadOrigins()
        {
            if (originsRead) return;
            ModOrigins.Rebuild();
            originsRead = true;
        }

        // The target for an identity of a mod, or null when no enabled mod has that origin or the mod has no such
        // picture. A picture a mod offers from code wins over one from its folders.
        public static ExternalTarget Find(AssetId id)
        {
            ReadOrigins();
            LoadedMod mod = ModOrigins.Find(id.Origin);
            if (mod == null) return null;
            return ProviderBridge.Find(mod, id.Locator) ?? ModSprites.Find(mod, id.Locator);
        }

        // Why Find found nothing.
        public static Problem Missing(AssetId id)
        {
            ReadOrigins();
            return ModOrigins.Find(id.Origin) == null ? Problem.OriginNotEnabled : Problem.NoSuchModPicture;
        }

        // Puts a sprite in place of the target's picture. A provider's own code runs here, so a failure is logged
        // and leaves the picture as it was.
        public static bool Set(ExternalTarget target, Sprite sprite)
        {
            try { target.Set(sprite); return true; }
            catch (Exception e)
            {
                Debug.LogWarning(TexturePacksMod.Tag + "could not replace a picture of another mod: " + e.Message);
                return false;
            }
        }

        // Puts the mod's own picture back.
        public static void Restore(ExternalTarget target)
        {
            try { target.Set(target.Original); }
            catch (Exception e) { Debug.LogWarning(TexturePacksMod.Tag + "could not restore a picture of another mod: " + e.Message); }
        }

        // Called after every choice has been restored (Packs.Clear).
        public static void Clear()
        {
            ModSprites.Clear();
            ProviderBridge.Clear();
            ModOrigins.Clear();
            originsRead = false;
        }
    }
}
