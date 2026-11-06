using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TexturePacks
{
    // Finds the installed texture packs and puts the switched-on ones in place: one replacement sprite for every
    // original sprite they cover, whole scene textures through SceneTextures, pictures of other mods through
    // ExternalAssets.
    //
    // A pack is an enabled mod that lists this mod among its prerequisites. The game has already decoded its
    // Textures folder into LoadedMod.Textures, so nothing is read from disk here. The packs are applied in the
    // player's order from the manager (see PackOrder and Settings): where two replace the same picture, the one
    // higher in the list wins.
    //
    // Rebuild runs in three steps (see Plan.Pure.cs): read the packs and the game into plain inputs, plan which file
    // is used for each picture, and apply the plan. Apply keeps what is in place now (`applied`) and only takes out
    // and puts in the choices that differ from the new plan, so a Rebuild that changes nothing touches nothing. That
    // matters at start-up, when every pack that finishes loading asks for a Rebuild: each one only adds what that
    // pack brings, instead of making every sprite of every pack again.
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
            public int Covered;             // left out because a pack higher in the list replaces the same picture
            public int Skipped;             // unknown name or wrong size
            // The files whose names the mod understands, read once by Describe (the icon is not one of them).
            public List<PackFile<Texture2D>> Files;
            // Replaced in the game right now (a scene texture once it has been copied in).
            public int Used { get { return UsedBy(Id); } }
        }

        public const string IconName = "pack";

        // The replacements, by the game's original Sprite, for the sprites among `applied`; the sprites in it were
        // created by this mod. Nothing here is a store object: the keys are sprites of the game's atlases, and Clear
        // empties all of it when the mod is disabled.
        static readonly Dictionary<Sprite, Sprite> byOriginal = new Dictionary<Sprite, Sprite>();
        // What is in place now: the plan's choice for each picture, and the sprite made for it (none for scene
        // textures). A choice's Handle is the game's object it goes on: the original Sprite of a game picture, the
        // ExternalTarget of another mod's picture, null for a scene texture.
        static readonly Dictionary<AssetId, Choice<Texture2D, object>> applied = new Dictionary<AssetId, Choice<Texture2D, object>>();
        static readonly Dictionary<AssetId, Sprite> made = new Dictionary<AssetId, Sprite>();
        // Sprites taken out of use, destroyed once the scene that may still show them is gone (DestroyRetired).
        static readonly List<Sprite> retired = new List<Sprite>();
        static readonly List<Pack> installed = new List<Pack>();
        static readonly Dictionary<string, Pack> installedById = new Dictionary<string, Pack>();
        // The mods the switched-on packs have files for (their origins), as of the last Rebuild.
        static readonly HashSet<Origin> referenced = new HashSet<Origin>();

        // False until the first Rebuild, and again after Clear: the patches then pass the game's sprites through.
        public static bool Ready { get; private set; }

        // Every installed pack, switched on or not, in the Mods menu's order.
        public static IList<Pack> Installed { get { return installed; } }

        // An installed pack by id, or null.
        public static Pack Find(string id)
        {
            Pack p;
            return id != null && installedById.TryGetValue(id, out p) ? p : null;
        }

        // Every pack's number of files in the game now: its choices in place, a scene texture only once it has been
        // copied into a loaded texture (Pack.Used). Counted for all packs in one pass over `applied`, and again only
        // when `applied` (appliedVersion) or what SceneTextures has written (its Version) has changed since, so the
        // log line and the manager's rows read it for free.
        static readonly Dictionary<string, int> usedCounts = new Dictionary<string, int>();
        static int appliedVersion, countedApplied = -1, countedScene = -1;

        static int UsedBy(string pack)
        {
            if (countedApplied != appliedVersion || countedScene != SceneTextures.Version)
            {
                usedCounts.Clear();
                foreach (var c in applied.Values)
                {
                    if (IsScene(c.Id) && !SceneTextures.Written(c.Id)) continue;
                    int n;
                    usedCounts.TryGetValue(c.Pack, out n);
                    usedCounts[c.Pack] = n + 1;
                }
                countedApplied = appliedVersion;
                countedScene = SceneTextures.Version;
            }
            int used;
            return usedCounts.TryGetValue(pack, out used) ? used : 0;
        }

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

        // Reads every enabled pack again, plans what replaces what, and changes what is in place to match. Runs at
        // start-up (as each pack's assets finish loading), after Apply in the manager, and when the mod is enabled
        // from the Mods menu.
        public static void Rebuild(string modId)
        {
            // Read: the installed packs, and the switched-on ones' files, top first.
            installed.Clear();
            installedById.Clear();
            foreach (var mod in ModLoader.Mods)
            {
                if (mod == null || !mod.IsEnabled || mod.Manifest == null || mod.Textures == null) continue;
                var pre = mod.Manifest.Prerequisites;
                if (pre == null || !pre.Contains(modId) || installedById.ContainsKey(mod.Manifest.ID)) continue;
                var pack = Describe(mod);
                installed.Add(pack);
                installedById[pack.Id] = pack;
            }
            var order = Settings.ApplyOrder(installed.Select(p => p.Id).ToList());
            order.Reverse();
            var inputs = order.Select(id => new PackInput<Texture2D>(id, installedById[id].Files)).ToList();

            // Which mod has which origin, and what the mods offer from code, for the pictures of other mods (read
            // when the plan first asks about one).
            ExternalAssets.Begin();

            // Plan. Bypass is raised while the originals are looked up: Original() goes through the patched loaders
            // and must get the game's sprite, not a replacement already in place.
            Plan<Texture2D, object> plan;
            Bypass++;
            try { plan = Planner.Make<Texture2D, object>(inputs, Look); }
            finally { Bypass--; }

            foreach (var p in installed)
            {
                var count = plan.CountOf(p.Id);
                p.Covered = count.Covered;
                p.Skipped = count.Rejected;
            }
            foreach (var r in plan.Rejected) Warn(r.Pack, r.Id, r.Describe());
            referenced.Clear();
            referenced.UnionWith(plan.Origins);

            // Apply: take out what changed or went, then put in what changed or came.
            var diff = PlanDiff<Texture2D, object>.Between(applied, plan.Chosen, SameHandle);
            foreach (var c in diff.Removed) TakeOut(c);
            foreach (var c in diff.Added) PutIn(c);
            Ready = true;
            // Only new scene files need the loaded textures searched; one taken out has put the game's pixels back.
            if (diff.Added.Any(c => IsScene(c.Id))) SceneTextures.Apply();

            int external = applied.Keys.Count(id => id.Origin.IsMod);
            Debug.Log(TexturePacksMod.Tag + installed.Count + " pack(s), " + byOriginal.Count + " sprite(s) replaced, " +
                SceneTextures.Count + " scene texture(s) in use, " + external + " picture(s) of other mods replaced (" +
                diff.Added.Count + " put in, " + diff.Removed.Count + " taken out, " + diff.Kept + " kept)" +
                (installed.Count > 0 ? ": " + string.Join(", ", installed.Select(Summary).ToArray()) : ""));
            // The fields hold what byOriginal held before; unchanged, they are right already. A new RenderHandler
            // gets them in its Awake (TexturePacksMod.RenderHandlerAwakePostfix).
            if (!diff.Empty) ApplyToRenderHandler();
        }

        // What the game has under an identity. A scene file names its own size; which scene textures exist is only
        // known once their scene loads, so that part is checked there (SceneTextures).
        static Target<object> Look(AssetId id)
        {
            if (id.Origin.IsMod)
            {
                ExternalTarget target = ExternalAssets.Find(id);
                if (target == null) return Target<object>.None(ExternalAssets.Missing(id));
                return Target<object>.Of(target, Width(target.Original), Height(target.Original));
            }
            Locator key = id.Locator;
            if (key.IsScene) return Target<object>.Of(null, key.Width, key.Height);
            Sprite original;
            if (!originals.TryGetValue(key, out original) || original == null)
            {
                original = Original(key);
                if (original != null) originals[key] = original;
            }
            return original == null ? Target<object>.None(Problem.NoSuchPicture)
                : Target<object>.Of(original, Width(original), Height(original));
        }

        // The game's sprites found by Look, kept across Rebuilds: a Resources atlas does not change while the game
        // runs, so each name is asked of the game's loaders once instead of once per Rebuild. A name the game does
        // not have is not kept and is asked again (at start-up the loaders may not be ready yet). The sprites are
        // assets of the game, not store objects; Clear empties it.
        static readonly Dictionary<Locator, Sprite> originals = new Dictionary<Locator, Sprite>();

        // A sprite's size in pixels, as a pack file must have it.
        static int Width(Sprite s) { return Mathf.RoundToInt(s.rect.width); }
        static int Height(Sprite s) { return Mathf.RoundToInt(s.rect.height); }

        // A whole texture of the game's scene files, put in by SceneTextures rather than as a sprite.
        static bool IsScene(AssetId id) { return id.Origin.IsGame && id.Locator.IsScene; }

        // Whether a choice in place on `a` is still in place on `b`: the same sprite of the game, or the same place
        // in another mod (ExternalTarget.Same). Scene textures have no object (both null).
        static bool SameHandle(object a, object b)
        {
            var ta = a as ExternalTarget;
            if (ta != null) return ta.Same(b as ExternalTarget);
            return a == null ? b == null : a.Equals(b);
        }

        // Puts one choice of the plan in the game and records it in `applied`: a scene texture is handed to
        // SceneTextures (copied in by its Apply); a sprite is made from the pack's texture and goes into byOriginal,
        // or into the other mod's place (ExternalAssets.Set). A choice that could not be put in is not recorded, so
        // the next Rebuild tries it again.
        static void PutIn(Choice<Texture2D, object> c)
        {
            if (IsScene(c.Id))
            {
                SceneTextures.Add(c.Id, c.File, c.Pack);
                applied[c.Id] = c;
                appliedVersion++;
                return;
            }
            var target = c.Handle as ExternalTarget;
            Sprite original = target != null ? target.Original : (Sprite)c.Handle;
            Sprite s = Make(original, c.File);
            if (target != null && !ExternalAssets.Set(target, s)) { retired.Add(s); return; }
            if (target == null) byOriginal[original] = s;
            made[c.Id] = s;
            applied[c.Id] = c;
            appliedVersion++;
        }

        // Undoes PutIn: the game's or the mod's own picture goes back, and the sprite made for the choice is retired.
        static void TakeOut(Choice<Texture2D, object> c)
        {
            if (IsScene(c.Id)) SceneTextures.Remove(c.Id);
            else if (c.Handle is ExternalTarget) ExternalAssets.Restore((ExternalTarget)c.Handle);
            else byOriginal.Remove((Sprite)c.Handle);
            Sprite s;
            if (made.TryGetValue(c.Id, out s)) { retired.Add(s); made.Remove(c.Id); }
            applied.Remove(c.Id);
            appliedVersion++;
        }

        // One pack's counts for the Player.log line written by Rebuild.
        static string Summary(Pack p)
        {
            return p.Id + (Settings.IsEnabled(p.Id) ? "" : " off") + " (" + p.Used + " used" +
                (p.Covered > 0 ? ", " + p.Covered + " covered" : "") + (p.Skipped > 0 ? ", " + p.Skipped + " skipped" : "") + ")";
        }

        // Reads the manifest and the pack's files whose names the mod understands, each name parsed once. A texture
        // named "pack" is the pack's icon in the manager, not a replacement (IconName).
        static Pack Describe(LoadedMod mod)
        {
            var m = mod.Manifest;
            var files = new List<PackFile<Texture2D>>();
            Texture2D icon = null;
            foreach (var t in mod.Textures)
            {
                if (t.Value == null) continue;
                if (t.Key == IconName) { icon = t.Value; continue; }
                var key = AssetIdCodec.Parse(t.Key);
                if (key != null) files.Add(new PackFile<Texture2D>(key, t.Value, t.Value.width, t.Value.height));
            }
            return new Pack
            {
                Id = m.ID, Name = string.IsNullOrEmpty(m.Name) ? m.ID : m.Name, Author = m.Author ?? "", Version = m.ModVersion ?? "",
                Description = m.Description ?? "", Icon = icon, Textures = files.Count, Files = files
            };
        }

        // Takes every choice out, so the game's and the mods' own pictures are back. Called when the mod is disabled.
        // Sprites already handed to the game may still be on screen, so they are only destroyed once the scene that
        // shows them is gone (see DestroyRetired).
        public static void Clear()
        {
            foreach (var c in applied.Values.ToList()) TakeOut(c);
            foreach (var s in byOriginal.Values) if (s != null) retired.Add(s);
            byOriginal.Clear();
            SceneTextures.Clear();
            ExternalAssets.Clear();
            referenced.Clear();
            installed.Clear();
            installedById.Clear();
            originals.Clear();
            appliedVersion++;
            Ready = false;
        }

        // Called after a scene load (SceneTextures.OnSceneLoaded), when sprites retired by Clear can no longer be shown.
        public static void DestroyRetired()
        {
            foreach (var s in retired) if (s != null) UnityEngine.Object.Destroy(s);
            retired.Clear();
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
        // The plan has checked the size.
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
        static void Warn(string pack, AssetId id, string why)
        {
            if (warned.Add(pack + "|" + id))
                Debug.LogWarning(TexturePacksMod.Tag + pack + ": " + id + " skipped, " + why);
        }

        // RenderHandler keeps the window frame and grid sprites in fields it fills once, straight from Resources.
        const BindingFlags AllFields = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Dictionary<FieldInfo, Sprite> fieldOriginals = new Dictionary<FieldInfo, Sprite>();
        static RenderHandler fieldsOf;
        // RenderHandler's Sprite fields, found by reflection once: the type does not change while the game runs.
        static List<FieldInfo> spriteFields;

        // Puts the replacements into the Sprite fields of RenderHandler, found by reflection because there are many
        // of them. The first value seen in each field is the original (kept in fieldOriginals), so a later apply,
        // or OnDisable, can swap or restore it. A new RenderHandler instance (a new scene) starts the table again.
        public static void ApplyToRenderHandler()
        {
            var handler = UnityEngine.Object.FindObjectOfType<RenderHandler>();
            if (handler == null) return;
            if (handler != fieldsOf) { fieldOriginals.Clear(); fieldsOf = handler; }
            if (spriteFields == null)
                spriteFields = typeof(RenderHandler).GetFields(AllFields).Where(f => f.FieldType == typeof(Sprite)).ToList();
            foreach (var f in spriteFields)
            {
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
