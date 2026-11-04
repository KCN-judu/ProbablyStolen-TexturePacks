using System;
using System.Collections.Generic;
using UnityEngine;

namespace TexturePacks
{
    // The pictures a mod keeps in its Items/ and Paper/ folders. The game decodes each PNG into a sprite and stores
    // it in LoadedMod.Sprites (Items/) or LoadedMod.PaperSprites (Paper/), under the file name without ".png", and
    // every consumer reads that dictionary when it needs the sprite: GameItemElement.ResolveSpriteByName for items,
    // CustomUIManager.GetModSprite for icons, PaperAssets.FromMod for papers. So replacing the dictionary entry
    // replaces the picture everywhere, with no patch on any of them. Swapper holds the bookkeeping (the originals,
    // and what may be written back), this class only knows which dictionary a mod's picture lives in.
    //
    // The one reader that must keep seeing the original is the item's shape: GameItem.SetSpriteAndShapeFromMod
    // reads the sprite's alpha to decide which cells an item takes, as for the game's own items. Suspend and Resume
    // put the original back for the length of that call (see the patch in TexturePacksMod), so a pack changes how a
    // mod's item looks and never which cells it takes.
    static class ModSprites
    {
        public const string ItemsContainer = "Items", PaperContainer = "Paper";

        static readonly Swapper<Sprite> swapper = new Swapper<Sprite>();

        sealed class Target : ExternalTarget
        {
            readonly Dictionary<string, Sprite> dictionary;
            readonly string key, name;

            public Target(Sprite original, Dictionary<string, Sprite> dictionary, string key, string name) : base(original)
            {
                this.dictionary = dictionary; this.key = key; this.name = name;
            }

            public override void Set(Sprite sprite)
            {
                if (sprite == Original) swapper.Restore(key);
                else if (!swapper.Replace(key, dictionary, name, sprite)) throw new InvalidOperationException("the mod no longer has the picture " + name);
            }
        }

        static string KeyOf(string modId, string container, string name) { return modId + "/" + container + "/" + name; }

        // The target for an asset of a mod, or null when the mod has no such picture or the container is not one of
        // the two folders the game decodes.
        public static ExternalTarget Find(LoadedMod mod, Locator locator)
        {
            if (!locator.IsModAsset) return null;
            Dictionary<string, Sprite> dictionary = locator.Path == ItemsContainer ? mod.Sprites
                : locator.Path == PaperContainer ? mod.PaperSprites : null;
            Sprite original;
            if (dictionary == null || !dictionary.TryGetValue(locator.Name, out original) || original == null) return null;
            return new Target(original, dictionary, KeyOf(mod.Manifest.ID, locator.Path, locator.Name), locator.Name);
        }

        // What Suspend leaves for Resume: which picture, and the replacement to put back.
        sealed class Suspended { public string Key; public Sprite Replacement; }

        // Puts the original sprite of one item picture back, if a pack has replaced it. Returns what Resume needs, or
        // null when there was nothing to do.
        public static object Suspend(string modId, string spriteKey)
        {
            if (modId == null || spriteKey == null) return null;
            string key = KeyOf(modId, ItemsContainer, spriteKey);
            Sprite replacement = swapper.Suspend(key);
            return replacement == null ? null : new Suspended { Key = key, Replacement = replacement };
        }

        // Puts the replacement back after the shape has been built. Takes what Suspend returned.
        public static void Resume(object state)
        {
            var suspended = state as Suspended;
            if (suspended != null) swapper.Resume(suspended.Key, suspended.Replacement);
        }

        public static void Clear() { swapper.RestoreAll(); }
    }
}
