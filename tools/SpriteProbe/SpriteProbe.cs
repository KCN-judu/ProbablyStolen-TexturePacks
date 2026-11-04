using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpriteProbe
{
    // Development tool for building the texture-pack asset list: logs every sprite the game actually puts on
    // screen, once, with the node that showed it. Play through the game with it enabled, then read Player.log:
    //   [SpriteProbe] seen <texture> <w>x<h> | <sprite> | <Image|SpriteRenderer> | <node path>
    // Polling enabled components four times a second catches panels, button hover and press states (which swap
    // Image.overrideSprite) and sprite animation frames that stay up for a moment.
    // The mod's entry point. It keeps a hidden object with the Probe component across scenes and adds a debug-menu
    // entry that logs a summary. Not part of the released mod; build it with tools/SpriteProbe/build.sh.
    public sealed class ProbeMod : IMod
    {
        internal const string Tag = "[SpriteProbe] ";
        static GameObject host;
        static string modId = "sprite_probe";

        public void Init(ModManifest manifest)
        {
            if (manifest != null && !string.IsNullOrEmpty(manifest.ID)) modId = manifest.ID;
        }

        public void OnEnable()
        {
            if (host == null)
            {
                host = new GameObject("SpriteProbe");
                UnityEngine.Object.DontDestroyOnLoad(host);
                host.hideFlags = HideFlags.HideAndDontSave;
                host.AddComponent<Probe>();
            }
            try
            {
                ModHelper.AddDebugFunction(modId, ModHelper.DEBUG_CATEGORY_MODDING, "Sprite probe: log summary", Probe.Summary, false);
            }
            catch (Exception e) { Debug.LogWarning(Tag + "debug menu entry unavailable: " + e.Message); }
            Debug.Log(Tag + "watching");
        }

        public void OnDisable()
        {
            try { ModHelper.RemoveDebugFunctions(modId); } catch (Exception) { }
            if (host != null) { UnityEngine.Object.Destroy(host); host = null; }
        }
    }

    // Polls, instead of patching the game's UI code, so it sees every sprite however it was set. It keeps only
    // names (strings), never the sprites, images or nodes, so it holds nothing of the store between scenes.
    public sealed class Probe : MonoBehaviour
    {
        static readonly HashSet<string> seen = new HashSet<string>();
        static readonly HashSet<string> textures = new HashSet<string>();
        float next;
        bool warned;

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.25f;
            try
            {
                foreach (var img in FindObjectsOfType<Image>())
                {
                    if (!img.enabled) continue;
                    Note(img.sprite, "Image", img.transform);
                    if (img.overrideSprite != img.sprite) Note(img.overrideSprite, "Image", img.transform);
                }
                foreach (var sr in FindObjectsOfType<SpriteRenderer>())
                    if (sr.enabled) Note(sr.sprite, "SpriteRenderer", sr.transform);
            }
            catch (Exception e)
            {
                if (!warned) { warned = true; Debug.LogWarning(ProbeMod.Tag + "scan failed: " + e); }
            }
        }

        // Logs a sprite the first time its texture and name are seen. Sprites without a texture are still logged.
        static void Note(Sprite s, string kind, Transform at)
        {
            if (s == null) return;
            var t = s.texture;
            string tex = t != null ? t.name + " " + t.width + "x" + t.height : "(no texture)";
            if (!seen.Add(tex + "|" + s.name)) return;
            textures.Add(tex);
            Debug.Log(ProbeMod.Tag + "seen " + tex + " | " + s.name + " | " + kind + " | " + PathOf(at));
        }

        // The node's path from the root, capped at 30 levels in case of a loop.
        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (int i = 0; t != null && i < 30; i++, t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        public static void Summary()
        {
            Debug.Log(ProbeMod.Tag + "summary: " + seen.Count + " sprite(s) on " + textures.Count + " texture(s) seen so far");
        }
    }
}
