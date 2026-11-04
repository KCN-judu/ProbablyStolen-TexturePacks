using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TexturePacks
{
    // Finds the installed texture packs and builds one replacement sprite for every original sprite the switched-on
    // ones cover; whole scene textures are handed to SceneTextures, and pictures of other mods to ExternalAssets.
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

        // One pack's file for one picture, kept while the candidates for it are compared.
        sealed class Source { public AssetId Id; public Texture2D Texture; public Pack Pack; }

        public const string IconName = "pack";

        // The replacements, by the game's original Sprite. The sprites in it were created by this mod; Clear moves
        // them to retired. Nothing here is a store object: the keys are sprites of the game's atlases, and Clear
        // empties all of it when the mod is disabled.
        static readonly Dictionary<Sprite, Sprite> byOriginal = new Dictionary<Sprite, Sprite>();
        static readonly List<Sprite> retired = new List<Sprite>();
        static readonly List<Pack> installed = new List<Pack>();
        // The mods the switched-on packs have files for (their origins), as of the last Rebuild.
        static readonly HashSet<Origin> referenced = new HashSet<Origin>();

        // False until the first Rebuild, and again after Clear: the patches then pass the game's sprites through.
        public static bool Ready { get; private set; }

        // Every installed pack, switched on or not, in the Mods menu's order.
        public static IList<Pack> Installed { get { return installed; } }

        public static Pack Find(string id) { return installed.FirstOrDefault(p => p.Id == id); }

        // Whether a switched-on pack has files for this mod. Its pictures exist once the mod has finished loading, so
        // the packs are applied again then (TexturePacksMod.OnModAssetsLoaded), whichever finished first.
        public static bool Refers(LoadedMod mod)
        {
            Origin origin = ModOrigins.OriginOf(mod);
            return origin != null && referenced.Contains(origin);
        }

        // While set, the game's loaders return the originals (see the patches in TexturePacksMod).
        public static int Bypass;

        // Called by the postfix on every sprite the game loads, so it is one dictionary lookup and nothing else.
        public static Sprite Replace(Sprite original)
        {
            Sprite s;
            return original != null && Bypass == 0 && byOriginal.TryGetValue(original, out s) && s != null ? s : original;
        }

        // Reads every enabled pack again and decides, picture by picture, what replaces what. Runs at start-up (as
        // each pack's assets finish loading), after Apply, and when the mod is enabled from the Mods menu.
        // Bypass is raised while the originals are looked up: Original() goes through the patched loaders and
        // must get the game's sprite, not a replacement from an earlier run.
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

            // Which mod has which origin, and what the mods offer from code, for the pictures of other mods.
            ExternalAssets.Begin();

            // Every switched-on pack's file for each picture, highest priority first.
            var candidates = new Dictionary<AssetId, List<Source>>();
            var order = Settings.ApplyOrder(installed.Select(p => p.Id).ToList());
            order.Reverse();
            foreach (var id in order)
            {
                var pack = Find(id);
                foreach (var t in mods[id].Textures)
                {
                    var key = AssetIdCodec.Parse(t.Key);
                    if (key == null || t.Value == null) continue;
                    List<Source> list;
                    if (!candidates.TryGetValue(key, out list)) candidates[key] = list = new List<Source>();
                    list.Add(new Source { Id = key, Texture = t.Value, Pack = pack });
                    if (key.Origin.IsMod) referenced.Add(key.Origin);
                }
            }

            // Each candidate is checked against the game first; the highest usable one wins (see PackResolve).
            Bypass++;
            try
            {
                foreach (var list in candidates.Values)
                {
                    var id = list[0].Id;
                    if (id.Origin.IsMod) { ApplyExternal(list); continue; }
                    Locator key = id.Locator;
                    Sprite original = key.IsScene ? null : Original(key);
                    var r = PackResolve.Choose(list, src => Usable(src, original));
                    foreach (var src in r.Covered) src.Pack.Covered++;
                    foreach (var src in r.Rejected) { src.Pack.Skipped++; Warn(src, Problem(src, original)); }
                    if (!r.HasWinner) continue;
                    if (key.IsScene) { SceneTextures.Add(id, r.Winner.Texture, r.Winner.Pack); continue; }
                    byOriginal[original] = Make(original, r.Winner.Texture);
                    r.Winner.Pack.Used++;
                }
            }
            finally { Bypass--; }
            Ready = true;
            SceneTextures.Apply();

            Debug.Log(TexturePacksMod.Tag + installed.Count + " pack(s), " + byOriginal.Count + " sprite(s) replaced, " +
                SceneTextures.Count + " scene texture(s) in use, " + ExternalAssets.Count + " picture(s) of other mods replaced" +
                (installed.Count > 0 ? ": " + string.Join(", ", installed.Select(Summary).ToArray()) : ""));
            ApplyToRenderHandler();
        }

        // One pack's counts for the Player.log line written by Rebuild.
        static string Summary(Pack p)
        {
            return p.Id + (Settings.IsEnabled(p.Id) ? "" : " off") + " (" + p.Used + " used" +
                (p.Covered > 0 ? ", " + p.Covered + " covered" : "") + (p.Skipped > 0 ? ", " + p.Skipped + " skipped" : "") + ")";
        }

        // Reads the manifest and counts the pictures of a pack whose names the mod understands. A texture named
        // "pack" is the pack's icon in the manager, not a replacement (IconName).
        static Pack Describe(LoadedMod mod)
        {
            var m = mod.Manifest;
            int textures = 0;
            Texture2D icon = null;
            foreach (var t in mod.Textures)
            {
                if (t.Value == null) continue;
                if (t.Key == IconName) icon = t.Value;
                else if (AssetIdCodec.Parse(t.Key) != null) textures++;
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
            ExternalAssets.Clear(retired);
            referenced.Clear();
            installed.Clear();
            Ready = false;
        }

        // Called after a scene load (SceneTextures.OnSceneLoaded), when sprites retired by Clear can no longer be shown.
        public static void DestroyRetired()
        {
            foreach (var s in retired) if (s != null) UnityEngine.Object.Destroy(s);
            retired.Clear();
        }

        // A scene file names its own size; a sprite file must match the game's sprite. Which scene textures exist is
        // only known once their scene loads, so that part is checked there (SceneTextures).
        static bool Usable(Source src, Sprite original)
        {
            Locator key = src.Id.Locator;
            if (key.IsScene) return src.Texture.width == key.Width && src.Texture.height == key.Height;
            return original != null && SameSize(src.Texture, original);
        }

        // The pictures of one other mod: the highest usable file wins and goes where the mod keeps its picture.
        // Same rules as for the game's own sprites (check first, then priority), with the mod's sprite as the original.
        static void ApplyExternal(List<Source> list)
        {
            var id = list[0].Id;
            ExternalTarget target = ExternalAssets.Find(id);
            Sprite original = target == null ? null : target.Original;
            var r = PackResolve.Choose(list, src => original != null && SameSize(src.Texture, original));
            foreach (var src in r.Covered) src.Pack.Covered++;
            foreach (var src in r.Rejected)
            {
                src.Pack.Skipped++;
                Warn(src, original == null ? ExternalAssets.Missing(id) : SizeProblem(src, original));
            }
            if (r.HasWinner && ExternalAssets.Replace(target, Make(original, r.Winner.Texture))) r.Winner.Pack.Used++;
        }

        // The reason a file was rejected, for the warning in Player.log.
        static string Problem(Source src, Sprite original)
        {
            Locator key = src.Id.Locator;
            if (key.IsScene || original != null)
            {
                int w = key.IsScene ? key.Width : Mathf.RoundToInt(original.rect.width);
                int h = key.IsScene ? key.Height : Mathf.RoundToInt(original.rect.height);
                return "is " + src.Texture.width + "x" + src.Texture.height + ", the game's picture is " + w + "x" + h;
            }
            return "no such sprite in the game";
        }

        static string SizeProblem(Source src, Sprite original)
        {
            return "is " + src.Texture.width + "x" + src.Texture.height + ", the mod's picture is " +
                Mathf.RoundToInt(original.rect.width) + "x" + Mathf.RoundToInt(original.rect.height);
        }

        static bool SameSize(Texture2D texture, Sprite original)
        {
            Rect r = original.rect;
            return texture.width == Mathf.RoundToInt(r.width) && texture.height == Mathf.RoundToInt(r.height);
        }

        // The game's own sprite for a key, or null when there is none. The loaders can throw for a name that is not
        // in the atlas, and a pack may name anything, so every failure means "no such sprite".
        static Sprite Original(Locator key)
        {
            try
            {
                return key.IsAtlas ? RenderHandler.TryLoadFromAtlas(key.Path, key.Name) : Resources.Load<Sprite>(key.Path);
            }
            catch (Exception) { return null; }
        }

        // Same size as the original, and the original's pivot, pixels per unit and nine-slice border, so the game
        // lays out and slices it exactly as before. Item shapes are built from the originals in any case.
        // Callers have checked the size (Usable).
        static Sprite Make(Sprite original, Texture2D texture)
        {
            Rect r = original.rect;
            texture.filterMode = original.texture != null ? original.texture.filterMode : FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            var pivot = new Vector2(original.pivot.x / r.width, original.pivot.y / r.height);
            var s = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), pivot, original.pixelsPerUnit,
                0, SpriteMeshType.FullRect, original.border);
            s.name = original.name;
            return s;
        }

        // One warning per pack and picture, so a rebuild does not repeat it.
        static readonly HashSet<string> warned = new HashSet<string>();
        static void Warn(Source src, string why)
        {
            if (warned.Add(src.Pack.Id + "|" + src.Id))
                Debug.LogWarning(TexturePacksMod.Tag + src.Pack.Id + ": " + src.Id + " skipped, " + why);
        }

        // RenderHandler keeps the window frame and grid sprites in fields it fills once, straight from Resources.
        const BindingFlags AllFields = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Dictionary<FieldInfo, Sprite> fieldOriginals = new Dictionary<FieldInfo, Sprite>();
        static RenderHandler fieldsOf;

        // Puts the replacements into the Sprite fields of RenderHandler, found by reflection because there are many
        // of them. The first value seen in each field is the original (kept in fieldOriginals), so a later apply,
        // or OnDisable, can swap or restore it. A new RenderHandler instance (a new scene) starts the table again.
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
