using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TexturePacks
{
    // Finds the installed texture packs and builds one replacement sprite for every original sprite the switched-on
    // ones cover; whole scene textures are handed to SceneTextures.
    //
    // A pack is an enabled mod that lists this mod among its prerequisites. The game has already decoded its
    // Textures folder into LoadedMod.Textures, so nothing is read from disk here. The packs are applied in the
    // player's order from the manager (see PackOrder and Settings): where two replace the same picture, the one
    // higher in the list wins.
    //
    // Replacements are keyed by the original Sprite object. The game's loaders return the same object for the same
    // sprite every time, so one lookup covers sprites fetched through any of them.
    static class Packs
    {
        public sealed class Pack
        {
            public string Id, Name, Author, Version, Description;
            public Texture2D Icon;          // the pack's own "pack.png", if it has one
            public int Textures;            // pictures in the pack that name something the game has
            public int Used;                // replaced in the game right now
            public int Covered;             // left out because a pack higher in the list replaces the same picture
            public int Skipped;             // unknown name or wrong size
        }

        sealed class Source { public PackKey Key; public Texture2D Texture; public Pack Pack; }

        public const string IconName = "pack";

        static readonly Dictionary<Sprite, Sprite> byOriginal = new Dictionary<Sprite, Sprite>();
        static readonly List<Sprite> retired = new List<Sprite>();
        static readonly List<Pack> installed = new List<Pack>();

        public static bool Ready { get; private set; }

        // Every installed pack, switched on or not, in the Mods menu's order.
        public static IList<Pack> Installed { get { return installed; } }

        public static Pack Find(string id) { return installed.FirstOrDefault(p => p.Id == id); }

        // While set, the game's loaders return the originals (see the patches in TexturePacksMod).
        public static int Bypass;

        public static Sprite Replace(Sprite original)
        {
            Sprite s;
            return original != null && Bypass == 0 && byOriginal.TryGetValue(original, out s) && s != null ? s : original;
        }

        public static void Rebuild(string modId)
        {
            Clear();
            var mods = new Dictionary<string, LoadedMod>();
            foreach (var mod in ModLoader.Mods)
            {
                if (mod == null || !mod.IsEnabled || mod.Manifest == null || mod.Textures == null) continue;
                var pre = mod.Manifest.Prerequisites;
                if (pre == null || !pre.Contains(modId) || mods.ContainsKey(mod.Manifest.ID)) continue;
                mods[mod.Manifest.ID] = mod;
                installed.Add(Describe(mod));
            }

            // Lowest priority first, so each pack overwrites what the packs below it chose.
            var winners = new Dictionary<string, Source>();
            foreach (var id in Settings.ApplyOrder(installed.Select(p => p.Id).ToList()))
            {
                var pack = Find(id);
                foreach (var t in mods[id].Textures)
                {
                    var key = PackKey.Parse(t.Key);
                    if (key == null || t.Value == null) continue;
                    Source below;
                    if (winners.TryGetValue(key.Id, out below)) below.Pack.Covered++;
                    winners[key.Id] = new Source { Key = key, Texture = t.Value, Pack = pack };
                }
            }

            Bypass++;
            try
            {
                foreach (var src in winners.Values)
                {
                    if (src.Key.IsSceneTexture) { SceneTextures.Add(src.Key, src.Texture, src.Pack); continue; }
                    var original = Original(src.Key);
                    if (original == null) { src.Pack.Skipped++; Warn(src, "no such sprite in the game"); continue; }
                    var made = Make(original, src.Texture);
                    if (made == null)
                    {
                        src.Pack.Skipped++;
                        Warn(src, "is " + src.Texture.width + "x" + src.Texture.height + ", the game's sprite is " +
                            (int)original.rect.width + "x" + (int)original.rect.height);
                        continue;
                    }
                    byOriginal[original] = made;
                    src.Pack.Used++;
                }
            }
            finally { Bypass--; }
            Ready = true;
            SceneTextures.Apply();

            Debug.Log(TexturePacksMod.Tag + installed.Count + " pack(s), " + byOriginal.Count + " sprite(s) replaced, " +
                SceneTextures.Count + " scene texture(s) in use" +
                (installed.Count > 0 ? ": " + string.Join(", ", installed.Select(Summary).ToArray()) : ""));
            ApplyToRenderHandler();
        }

        static string Summary(Pack p)
        {
            return p.Id + (Settings.IsEnabled(p.Id) ? "" : " off") + " (" + p.Used + " used" +
                (p.Covered > 0 ? ", " + p.Covered + " covered" : "") + (p.Skipped > 0 ? ", " + p.Skipped + " skipped" : "") + ")";
        }

        static Pack Describe(LoadedMod mod)
        {
            var m = mod.Manifest;
            int textures = 0;
            Texture2D icon = null;
            foreach (var t in mod.Textures)
            {
                if (t.Value == null) continue;
                if (t.Key == IconName) icon = t.Value;
                else if (PackKey.Parse(t.Key) != null) textures++;
            }
            return new Pack
            {
                Id = m.ID, Name = string.IsNullOrEmpty(m.Name) ? m.ID : m.Name, Author = m.Author ?? "", Version = m.ModVersion ?? "",
                Description = m.Description ?? "", Icon = icon, Textures = textures
            };
        }

        // Sprites already handed to the game may still be on screen, so they are only destroyed once the scene
        // that shows them is gone (see DestroyRetired).
        public static void Clear()
        {
            foreach (var s in byOriginal.Values) if (s != null) retired.Add(s);
            byOriginal.Clear();
            SceneTextures.Clear();
            installed.Clear();
            Ready = false;
        }

        public static void DestroyRetired()
        {
            foreach (var s in retired) if (s != null) UnityEngine.Object.Destroy(s);
            retired.Clear();
        }

        static Sprite Original(PackKey key)
        {
            try
            {
                return key.IsAtlas ? RenderHandler.TryLoadFromAtlas(key.Path, key.Sprite) : Resources.Load<Sprite>(key.Path);
            }
            catch (Exception) { return null; }
        }

        // Same size as the original, and the original's pivot, pixels per unit and nine-slice border, so the game
        // lays out and slices it exactly as before. Item shapes are built from the originals in any case.
        static Sprite Make(Sprite original, Texture2D texture)
        {
            Rect r = original.rect;
            if (texture.width != Mathf.RoundToInt(r.width) || texture.height != Mathf.RoundToInt(r.height)) return null;
            texture.filterMode = original.texture != null ? original.texture.filterMode : FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            var pivot = new Vector2(original.pivot.x / r.width, original.pivot.y / r.height);
            var s = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), pivot, original.pixelsPerUnit,
                0, SpriteMeshType.FullRect, original.border);
            s.name = original.name;
            return s;
        }

        static readonly HashSet<string> warned = new HashSet<string>();
        static void Warn(Source src, string why)
        {
            if (warned.Add(src.Pack.Id + "|" + src.Key.Id))
                Debug.LogWarning(TexturePacksMod.Tag + src.Pack.Id + ": " + src.Key.Id + " skipped, " + why);
        }

        // RenderHandler keeps the window frame and grid sprites in fields it fills once, straight from Resources.
        const BindingFlags AllFields = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Dictionary<FieldInfo, Sprite> fieldOriginals = new Dictionary<FieldInfo, Sprite>();
        static RenderHandler fieldsOf;

        public static void ApplyToRenderHandler()
        {
            var handler = UnityEngine.Object.FindObjectOfType<RenderHandler>();
            if (handler == null) return;
            if (handler != fieldsOf) { fieldOriginals.Clear(); fieldsOf = handler; }
            foreach (var f in typeof(RenderHandler).GetFields(AllFields))
            {
                if (f.FieldType != typeof(Sprite)) continue;
                Sprite original;
                if (!fieldOriginals.TryGetValue(f, out original))
                {
                    original = f.GetValue(f.IsStatic ? null : handler) as Sprite;
                    fieldOriginals[f] = original;
                }
                if (original == null) continue;
                Sprite s;
                f.SetValue(f.IsStatic ? null : handler, byOriginal.TryGetValue(original, out s) ? s : original);
            }
        }
    }
}
