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
    static class SceneTextures
    {
        // The winning file for one scene texture. Counted: the pack's Used count has gone up for it already, so
        // loading the scene again does not count it twice.
        sealed class Source { public AssetId Id; public Texture2D Texture; public Packs.Pack Pack; public bool Counted; }

        static readonly Dictionary<string, Source> sources = new Dictionary<string, Source>();
        // Copies of the game's pixels, by the texture they were taken from, to put back in Clear. Textures of a
        // scene that was left are destroyed by Unity; Forget drops their entries. Clear (called from OnDisable
        // through Packs.Clear) empties it, so nothing stays after the mod is disabled.
        static readonly Dictionary<Texture2D, Texture2D> backups = new Dictionary<Texture2D, Texture2D>();
        static readonly HashSet<string> warned = new HashSet<string>();
        static bool hooked;

        public static int Count { get { return sources.Count; } }

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

        // The sources are looked up by the texture's name and size (see Apply), so that is their key.
        public static void Add(AssetId id, Texture2D texture, Packs.Pack pack)
        {
            Locator scene = id.Locator;
            sources[AssetIdCodec.SceneName(scene.Path, scene.Width, scene.Height)] = new Source { Id = id, Texture = texture, Pack = pack };
        }

        // Puts every replaced texture back and forgets the sources.
        public static void Clear()
        {
            foreach (var kv in backups)
            {
                try { if (kv.Key != null) Graphics.CopyTexture(kv.Value, kv.Key); }
                catch (Exception e) { Debug.LogWarning(TexturePacksMod.Tag + "could not restore " + kv.Key.name + ": " + e.Message); }
                if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
            }
            backups.Clear();
            sources.Clear();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // The scene that could still show sprites replaced by an earlier apply is gone now.
            Packs.DestroyRetired();
            try { Apply(); }
            catch (Exception e) { Debug.LogWarning(TexturePacksMod.Tag + "could not replace scene textures: " + e); }
        }

        // Copies the pack's pixels into every loaded texture with a matching name and size. Run after every scene
        // load and after Packs.Rebuild. A texture already in backups has been replaced; the others are replaced now.
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
                if (t == null || backups.ContainsKey(t)) continue;
                Source src;
                if (!sources.TryGetValue(AssetIdCodec.SceneName(t.name, t.width, t.height), out src) || src.Texture == t) continue;
                if (Write(t, src)) done.Add(src.Id.ToString());
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
                if (b != null) UnityEngine.Object.Destroy(b);
            }
        }

        // Replaces one texture on the GPU, keeping the original aside. A DXT texture is staged as RGBA32 and
        // compressed, because Graphics.CopyTexture needs the same format and mip count on both sides.
        // Returns false, with one warning per texture, when the texture cannot be replaced.
        static bool Write(Texture2D target, Source src)
        {
            var png = src.Texture;
            string id = src.Pack.Id + "|" + src.Id;
            if (png.width != target.width || png.height != target.height)
            {
                Warn(id, src.Pack.Id + ": " + src.Id + " skipped, the file is " + png.width + "x" + png.height);
                return false;
            }
            var format = target.format;
            bool compress = format == TextureFormat.DXT5 || format == TextureFormat.DXT1;
            if (!compress && format != TextureFormat.RGBA32 && format != TextureFormat.ARGB32 && format != TextureFormat.BGRA32 &&
                format != TextureFormat.RGB24 && format != TextureFormat.Alpha8)
            {
                Warn(id, src.Pack.Id + ": " + src.Id + " skipped, the game stores it as " + format + ", which cannot be replaced");
                return false;
            }
            bool mips = target.mipmapCount > 1;
            Texture2D staged = null, backup = null;
            try
            {
                staged = new Texture2D(target.width, target.height, compress ? TextureFormat.RGBA32 : format, mips);
                staged.SetPixels32(png.GetPixels32());
                staged.Apply(mips, false);
                if (compress) staged.Compress(true);
                if (staged.format != format || staged.mipmapCount != target.mipmapCount)
                {
                    Warn(id, src.Pack.Id + ": " + src.Id + " skipped, could not match the game's " + format + " texture");
                    return false;
                }
                backup = new Texture2D(target.width, target.height, format, mips);
                Graphics.CopyTexture(target, backup);
                Graphics.CopyTexture(staged, target);
                backups[target] = backup;
                backup = null;
                if (!src.Counted) { src.Counted = true; src.Pack.Used++; }
                return true;
            }
            catch (Exception e)
            {
                Warn(id, src.Pack.Id + ": " + src.Id + " skipped: " + e.Message);
                return false;
            }
            finally
            {
                if (staged != null) UnityEngine.Object.Destroy(staged);
                if (backup != null) UnityEngine.Object.Destroy(backup);
            }
        }

        static void Warn(string id, string message)
        {
            if (warned.Add(id)) Debug.LogWarning(TexturePacksMod.Tag + message);
        }
    }
}
