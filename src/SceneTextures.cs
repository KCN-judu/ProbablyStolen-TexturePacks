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
        sealed class Source { public PackKey Key; public Texture2D Texture; public Packs.Pack Pack; public bool Counted; }

        static readonly Dictionary<string, Source> sources = new Dictionary<string, Source>();
        static readonly Dictionary<Texture2D, Texture2D> backups = new Dictionary<Texture2D, Texture2D>();
        static readonly HashSet<string> warned = new HashSet<string>();
        static bool hooked;

        public static int Count { get { return sources.Count; } }

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

        public static void Add(PackKey key, Texture2D texture, Packs.Pack pack)
        {
            sources[key.Id] = new Source { Key = key, Texture = texture, Pack = pack };
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
                if (!sources.TryGetValue(PackKey.SceneId(t.name, t.width, t.height), out src) || src.Texture == t) continue;
                if (Write(t, src)) done.Add(src.Key.Id);
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

        static bool Write(Texture2D target, Source src)
        {
            var png = src.Texture;
            string id = src.Pack.Id + "|" + src.Key.Id;
            if (png.width != target.width || png.height != target.height)
            {
                Warn(id, src.Pack.Id + ": " + src.Key.Id + " skipped, the file is " + png.width + "x" + png.height);
                return false;
            }
            var format = target.format;
            bool compress = format == TextureFormat.DXT5 || format == TextureFormat.DXT1;
            if (!compress && format != TextureFormat.RGBA32 && format != TextureFormat.ARGB32 && format != TextureFormat.BGRA32 &&
                format != TextureFormat.RGB24 && format != TextureFormat.Alpha8)
            {
                Warn(id, src.Pack.Id + ": " + src.Key.Id + " skipped, the game stores it as " + format + ", which cannot be replaced");
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
                    Warn(id, src.Pack.Id + ": " + src.Key.Id + " skipped, could not match the game's " + format + " texture");
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
                Warn(id, src.Pack.Id + ": " + src.Key.Id + " skipped: " + e.Message);
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
