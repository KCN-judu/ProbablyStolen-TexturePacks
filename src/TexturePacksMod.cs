using System;
using HarmonyLib;
using UnityEngine;

namespace TexturePacks
{
    // Replaces the game's sprites with the ones in enabled texture packs (see Packs).
    //
    // Nearly every sprite reaches the game through RenderHandler: LoadFromAtlasRaw for sprites in an atlas
    // (LoadFromAtlas and TryLoadFromAtlas both call it) and LoadFromFile for single ones. A postfix on each swaps the
    // original for its replacement. Item shapes come from the alpha of the item's sprite, so GameItem's shape
    // builder always sees the original: a pack changes how an item looks, never which cells it takes.
    // Textures stored in the scene files are replaced in place after every scene load (see SceneTextures).
    // The settings button of Texture Packs in the Mods menu opens the pack manager (see Manager) instead of the
    // game's settings window.
    public sealed class TexturePacksMod : IMod
    {
        public const string ModId = "texture_packs";
        internal const string Tag = "[TexturePacks] ";
        const string HarmonyId = "mod." + ModId;

        static string modId = ModId;
        static Harmony harmony;
        static bool assetsHooked;

        public void Init(ModManifest manifest)
        {
            if (manifest != null && !string.IsNullOrEmpty(manifest.ID)) modId = manifest.ID;
        }

        public void OnEnable()
        {
            if (harmony == null)
            {
                harmony = new Harmony(HarmonyId);
                Patch("RenderHandler.LoadFromAtlasRaw", typeof(RenderHandler).GetMethod("LoadFromAtlasRaw", Any), null, "SpritePostfix", null);
                Patch("RenderHandler.LoadFromFile", typeof(RenderHandler).GetMethod("LoadFromFile", Any), null, "SpritePostfix", null);
                Patch("RenderHandler.Awake", typeof(RenderHandler).GetMethod("Awake", Any), null, "RenderHandlerAwakePostfix", null);
                Patch("GameItem.BuildShapeFromSprite", typeof(GameItem).GetMethod("BuildShapeFromSprite", Any), "BypassPrefix", null, "BypassFinalizer");
                Patch("ModConfigWindow.Open", typeof(ModConfigWindow).GetMethod("Open", Any), "SettingsPrefix", null, null);
            }
            Settings.Define(modId);
            // Each mod's textures are decoded before its assets-loaded event fires; the packs are read again then.
            // The loader itself is off limits to mods (the game's scanner refuses reflection on ModLoader).
            if (!assetsHooked) { ModHook.OnModAssetsLoaded += OnModAssetsLoaded; assetsHooked = true; }
            SceneTextures.Hook();
            // Enabled from the Mods menu after start-up: the game has loaded every mod's textures already.
            if (!Packs.Ready && ModLoader.Instance != null && ModLoader.Mods != null && ModLoader.Mods.Count > 0 &&
                UnityEngine.Object.FindObjectOfType<RenderHandler>() != null)
                Rebuild();
        }

        public void OnDisable()
        {
            Manager.Close();
            if (assetsHooked) { ModHook.OnModAssetsLoaded -= OnModAssetsLoaded; assetsHooked = false; }
            if (harmony != null) { harmony.UnpatchAll(HarmonyId); harmony = null; }
            SceneTextures.Unhook();
            Packs.Clear();
            Packs.ApplyToRenderHandler();
        }

        const System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

        static void Patch(string label, System.Reflection.MethodInfo target, string prefix, string postfix, string finalizer)
        {
            try
            {
                if (target == null) { Debug.LogWarning(Tag + label + " not found"); return; }
                harmony.Patch(target, Hook(prefix), Hook(postfix), null, Hook(finalizer));
            }
            catch (Exception e) { Debug.LogWarning(Tag + "could not patch " + label + ": " + e); }
        }

        static HarmonyMethod Hook(string name)
        {
            return name == null ? null : new HarmonyMethod(typeof(TexturePacksMod).GetMethod(name, Any));
        }

        internal static void Rebuild()
        {
            try { Packs.Rebuild(modId); }
            catch (Exception e) { Debug.LogWarning(Tag + "could not load the texture packs: " + e); }
        }

        internal static string L(string key, params object[] args)
        {
            return ModHelper.GetLocalized(modId, key, args);
        }

        // ---------------------------------------------------------------- patches

        static void SpritePostfix(ref Sprite __result)
        {
            if (Packs.Ready) __result = Packs.Replace(__result);
        }

        // The Mods menu's settings button for this mod opens the pack manager instead of the generic settings window.
        static bool SettingsPrefix(LoadedMod mod, ref CustomUIWindow __result)
        {
            if (mod == null || mod.Manifest == null || mod.Manifest.ID != modId) return true;
            try { __result = Manager.Open(); }
            catch (Exception e) { Debug.LogWarning(Tag + "could not open the pack manager: " + e); return true; }
            return false;
        }

        static void BypassPrefix() { Packs.Bypass++; }
        static void BypassFinalizer() { Packs.Bypass--; }

        // Only a pack's own assets can change what is replaced; other mods finishing are ignored.
        static void OnModAssetsLoaded(LoadedMod mod)
        {
            if (mod == null || mod.Manifest == null || mod.Manifest.Prerequisites == null || !mod.Manifest.Prerequisites.Contains(modId)) return;
            Rebuild();
        }

        static void RenderHandlerAwakePostfix()
        {
            if (!Packs.Ready) return;
            try { Packs.ApplyToRenderHandler(); }
            catch (Exception e) { Debug.LogWarning(Tag + "could not replace the window sprites: " + e); }
        }
    }
}
