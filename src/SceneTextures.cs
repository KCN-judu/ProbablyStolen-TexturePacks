using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TexturePacks
{
    // Replaces textures the game keeps in its scene files (backgrounds, fixtures, portraits, most of the UI). These
    // never pass through RenderHandler, so instead of swapping sprites the pack's pixels are copied into the game's
    // own texture on the GPU: every sprite, animation frame and portrait cut from it changes at once.
    //
    // A scene's textures are read from disk again each time the scene loads, so the copy is made after every scene
    // load. The original is copied aside first and put back when the packs change or the mod is disabled.
    // Sizes must match exactly; DXT textures are compressed at runtime to match, crunched ones are skipped.
    //
    // Cost: each Apply looks at every loaded texture once, but reads a texture's name (an engine call that allocates
    // a string) only when some pack file has its size. The pack's pixels are converted to the game's format once per
    // file and kept (Source.Staged), so loading a scene again is one GPU copy per texture, not a new conversion and
    // compression. That copy costs memory: one texture of the game's size and format per replaced scene texture.
    static class SceneTextures
    {
        // The winning file for one scene texture. Written: it has been copied into the game's texture at least once,
        // which is when it counts as used in the pack manager. Staged: the file in the game's format, made by the
        // first Write and reused by the later ones; destroyed when the file is removed.
        sealed class Source { public AssetId Id; public Texture2D Texture; public string Pack; public bool Written; public Texture2D Staged; }

        // A copy of the game's pixels, and the key of the texture it was taken from.
        sealed class Backup { public Texture2D Copy; public SceneKey Key; }

        static readonly Dictionary<SceneKey, Source> sources = new Dictionary<SceneKey, Source>();
        // How many sources have each size (SceneKey.Size), so Apply skips every texture of another size unread.
        static readonly Dictionary<long, int> sizes = new Dictionary<long, int>();
        // Copies of the game's pixels, by the texture they were taken from, to put back in Remove and Clear; and the
        // same textures by key, so Remove finds a file's textures without going through every backup. Textures of a
        // scene that was left are destroyed by Unity; Forget drops their entries. Clear (called from OnDisable
        // through Packs.Clear) empties it, so nothing stays after the mod is disabled.
        static readonly Dictionary<Texture2D, Backup> backups = new Dictionary<Texture2D, Backup>();
        static readonly Dictionary<SceneKey, List<Texture2D>> replaced = new Dictionary<SceneKey, List<Texture2D>>();
        static readonly HashSet<string> warned = new HashSet<string>();
        static bool hooked;

        public static int Count { get { return sources.Count; } }

        // Goes up whenever what Written answers may have changed, so Packs knows when to count again.
        public static int Version { get; private set; }

        // Subscribed while the mod is enabled; Unhook removes it so the mod leaves no delegate behind.
        public static void Hook()
        {
            if (hooked) return;
            SceneManager.sceneLoaded += OnSceneLoaded;
            hooked = true;
        }

        public static void Unhook()
        {
            if (!hooked) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            hooked = false;
        }

        // A pack's file for one scene texture, copied in by the next Apply. A file already there for the texture is
        // removed first, so the game's pixels are back before the new file is written.
        public static void Add(AssetId id, Texture2D texture, string pack)
        {
            var key = SceneKey.Of(id.Locator);
            if (sources.ContainsKey(key)) Remove(id);
            sources[key] = new Source { Id = id, Texture = texture, Pack = pack };
            int n;
            sizes.TryGetValue(key.SizeKey, out n);
            sizes[key.SizeKey] = n + 1;
            Version++;
        }

        // Puts the game's pixels back into every loaded texture of this name and size, and forgets the file.
        public static void Remove(AssetId id)
        {
            var key = SceneKey.Of(id.Locator);
            Source src;
            if (sources.TryGetValue(key, out src))
            {
                if (src.Staged != null) UnityEngine.Object.Destroy(src.Staged);
                sources.Remove(key);
                int n = sizes[key.SizeKey] - 1;
                if (n > 0) sizes[key.SizeKey] = n; else sizes.Remove(key.SizeKey);
            }
            List<Texture2D> textures;
            if (replaced.TryGetValue(key, out textures))
            {
                foreach (var t in textures)
                {
                    Backup b;
                    if (!backups.TryGetValue(t, out b)) continue;
                    backups.Remove(t);
                    if (t != null) Restore(t, b.Copy);
                    else if (b.Copy != null) UnityEngine.Object.Destroy(b.Copy);
                }
                replaced.Remove(key);
            }
            Version++;
        }

        // Whether the file for this texture has been copied into the game's texture since it was added.
        public static bool Written(AssetId id)
        {
            Source src;
            return sources.TryGetValue(SceneKey.Of(id.Locator), out src) && src.Written;
        }

        // Copies the game's pixels back from the backup and destroys the backup. The caller drops its entry.
        static void Restore(Texture2D target, Texture2D backup)
        {
            try { Graphics.CopyTexture(backup, target); }
            catch (Exception e) { Debug.LogWarning(TexturePacksMod.Tag + "could not restore " + target.name + ": " + e.Message); }
            if (backup != null) UnityEngine.Object.Destroy(backup);
        }

        // Puts every replaced texture back and forgets the sources.
        public static void Clear()
        {
            foreach (var kv in backups)
            {
                if (kv.Key != null) Restore(kv.Key, kv.Value.Copy);
                else if (kv.Value.Copy != null) UnityEngine.Object.Destroy(kv.Value.Copy);
            }
            foreach (var src in sources.Values) if (src.Staged != null) UnityEngine.Object.Destroy(src.Staged);
            backups.Clear();
            replaced.Clear();
            sources.Clear();
            sizes.Clear();
            Version++;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // The scene that could still show sprites replaced by an earlier apply is gone now.
            Packs.DestroyRetired();
            try { Apply(); }
            catch (Exception e) { Debug.LogWarning(TexturePacksMod.Tag + "could not replace scene textures: " + e); }
        }

        // Copies the pack's pixels into every loaded texture with a matching name and size. Run after every scene
        // load, and after a Packs.Rebuild that added a file. A texture already in backups has been replaced; the
        // others are replaced now.
        public static void Apply()
        {
            Forget();
            if (sources.Count == 0) return;
            if (SystemInfo.copyTextureSupport == UnityEngine.Rendering.CopyTextureSupport.None)
            {
                Warn("gpu", "this graphics device cannot copy textures, scene textures stay the game's");
                return;
            }
            var done = new List<string>();
            foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if (t == null) continue;
                int w = t.width, h = t.height;
                if (!sizes.ContainsKey(SceneKey.Size(w, h)) || backups.ContainsKey(t)) continue;
                Source src;
                var key = new SceneKey(t.name, w, h);
                if (!sources.TryGetValue(key, out src) || src.Texture == t) continue;
                if (Write(t, key, src)) done.Add(src.Id.ToString());
            }
            if (done.Count > 0)
                Debug.Log(TexturePacksMod.Tag + done.Count + " scene texture(s) replaced: " + string.Join(", ", done.ToArray()));
        }

        // Textures of a scene that was unloaded are gone; their copies are no longer needed.
        static void Forget()
        {
            var dead = new List<Texture2D>();
            foreach (var kv in backups) if (kv.Key == null) dead.Add(kv.Key);
            foreach (var k in dead)
            {
                var b = backups[k];
                backups.Remove(k);
                if (b.Copy != null) UnityEngine.Object.Destroy(b.Copy);
                List<Texture2D> textures;
                if (replaced.TryGetValue(b.Key, out textures))
                {
                    textures.Remove(k);
                    if (textures.Count == 0) replaced.Remove(b.Key);
                }
            }
        }

        // Replaces one texture (whose key is `key`) on the GPU, keeping the original aside. The file is staged in the game's format once
        // (Stage) and the staged texture is kept for the next scene load. Returns false, with one warning per
        // texture, when the texture cannot be replaced.
        static bool Write(Texture2D target, SceneKey key, Source src)
        {
            var png = src.Texture;
            string id = src.Pack + "|" + src.Id;
            if (png.width != target.width || png.height != target.height)
            {
                Warn(id, src.Pack + ": " + src.Id + " skipped, the file is " + png.width + "x" + png.height);
                return false;
            }
            var format = target.format;
            bool compress = format == TextureFormat.DXT5 || format == TextureFormat.DXT1;
            if (!compress && format != TextureFormat.RGBA32 && format != TextureFormat.ARGB32 && format != TextureFormat.BGRA32 &&
                format != TextureFormat.RGB24 && format != TextureFormat.Alpha8)
            {
                Warn(id, src.Pack + ": " + src.Id + " skipped, the game stores it as " + format + ", which cannot be replaced");
                return false;
            }
            Texture2D backup = null;
            try
            {
                // Another texture of the same name and size may use another format or mip count: stage again then.
                if (src.Staged == null || src.Staged.format != format || src.Staged.mipmapCount != target.mipmapCount)
                {
                    if (src.Staged != null) UnityEngine.Object.Destroy(src.Staged);
                    src.Staged = Stage(png, target, format, compress);
                    if (src.Staged == null)
                    {
                        Warn(id, src.Pack + ": " + src.Id + " skipped, could not match the game's " + format + " texture");
                        return false;
                    }
                }
                bool mips = target.mipmapCount > 1;
                backup = new Texture2D(target.width, target.height, format, mips);
                Graphics.CopyTexture(target, backup);
                Graphics.CopyTexture(src.Staged, target);
                backups[target] = new Backup { Copy = backup, Key = key };
                List<Texture2D> textures;
                if (!replaced.TryGetValue(key, out textures)) replaced[key] = textures = new List<Texture2D>();
                textures.Add(target);
                backup = null;
                if (!src.Written) { src.Written = true; Version++; }
                return true;
            }
            catch (Exception e)
            {
                Warn(id, src.Pack + ": " + src.Id + " skipped: " + e.Message);
                return false;
            }
            finally
            {
                if (backup != null) UnityEngine.Object.Destroy(backup);
            }
        }

        // The pack's pixels as a texture of the target's size, format and mip count. A DXT texture is staged as
        // RGBA32 and compressed, because Graphics.CopyTexture needs the same format and mip count on both sides.
        // Null when the result does not match the target.
        static Texture2D Stage(Texture2D png, Texture2D target, TextureFormat format, bool compress)
        {
            bool mips = target.mipmapCount > 1;
            Texture2D staged = null;
            try
            {
                staged = new Texture2D(target.width, target.height, compress ? TextureFormat.RGBA32 : format, mips);
                staged.SetPixels32(png.GetPixels32());
                staged.Apply(mips, false);
                if (compress) staged.Compress(true);
                if (staged.format != format || staged.mipmapCount != target.mipmapCount) return null;
                var result = staged;
                staged = null;
                return result;
            }
            finally
            {
                if (staged != null) UnityEngine.Object.Destroy(staged);
            }
        }

        static void Warn(string id, string message)
        {
            if (warned.Add(id)) Debug.LogWarning(TexturePacksMod.Tag + message);
        }
    }
}
