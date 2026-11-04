using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TexturePacks
{
    // The texture pack manager: the installed packs in priority order, each with a switch, its numbers and buttons
    // to move it up or down. The pack at the top wins where two replace the same picture. Changes are kept in a
    // draft until Apply, which saves the order and puts the packs in place at once.
    //
    // Built entirely from the game's own UI builder and the options window's frame, so a texture pack restyles the
    // manager like the rest of the menus. It opens from the settings button Texture Packs gets in the Mods menu.
    static class Manager
    {
        const string WindowId = "texture_packs_manager";
        const float MoveWidth = 96f, IconSize = 40f;

        // The order and switches as edited in the window. Null while the window is closed. Only Apply writes it
        // to Settings. A list of PackState (ids and flags, no game objects), so it keeps no store object alive.
        static List<PackState> draft;
        static bool rebuilding;   // the window is closed only to be built again: keep the draft
        // One Sprite per pack icon texture, so rebuilding the window does not create a new Sprite for every row.
        static readonly Dictionary<Texture2D, Sprite> icons = new Dictionary<Texture2D, Sprite>();

        static string L(string key, params object[] args) { return TexturePacksMod.L(key, args); }

        static List<string> InstalledIds() { return Packs.Installed.Select(p => p.Id).ToList(); }

        // Called from the settings button's prefix (TexturePacksMod.SettingsPrefix). Starts a draft from the saved
        // order unless the window is already open.
        public static CustomUIWindow Open()
        {
            if (draft == null) draft = Settings.Load(InstalledIds());
            return Build();
        }

        // Drops the draft, so changes that were not applied are lost, and closes the window.
        public static void Close()
        {
            draft = null;
            var m = CustomUIManager.Instance;
            if (m != null && m.IsOpen(WindowId)) m.CloseWindow(WindowId);
        }

        // Builds the whole window from the draft. The game's UI builder has no way to change rows in place, so a
        // move or an Apply closes the window (keeping the draft, see rebuilding) and builds it again.
        // "meta" is the layer used inside the store, "menu" the layer of the main menu.
        static CustomUIWindow Build()
        {
            var m = CustomUIManager.Instance;
            if (m == null) return null;
            if (m.IsOpen(WindowId))
            {
                rebuilding = true;
                try { m.CloseWindow(WindowId); }
                finally { rebuilding = false; }
            }

            var installed = new HashSet<string>(InstalledIds());
            var visible = PackOrder.Visible(draft, installed);
            string layer = StoreUIManager.Instance != null ? "meta" : "menu";
            var b = m.CreateWindow(WindowId, L("manager_title"), layer, m.optionUIPrefab, CustomUIManager.Style.Auto)
                .SetCloseOnEscape(true)
                .OnClose(() => { if (!rebuilding) draft = null; })
                .AddLabel(L(visible.Count > 0 ? "manager_intro" : "manager_empty"), "intro")
                .AddSeparator("intro_line", 4)
                .BeginScrollFlexible(240f);
            for (int i = 0; i < visible.Count; i++) Row(b, visible[i], i, visible.Count);
            b.End();

            b.AddSeparator("buttons_line", 4)
                .BeginRow(16f, TextAnchor.MiddleCenter)
                .AddFlexibleSpace()
                .AddButton(L("manager_apply"), Apply, "apply");
            b.Last.SetFixedWidth(180f);
            b.AddButton(L("manager_close"), Close, "close");
            b.Last.SetFixedWidth(180f);
            return b.AddFlexibleSpace().End().Show();
        }

        // One pack: icon, switch, numbers and the Up and Down buttons. The first Up and the last Down are disabled.
        static void Row(CustomUIBuilder b, PackState state, int index, int count)
        {
            var pack = Packs.Find(state.Id);
            string id = state.Id;
            b.BeginRow(12f, TextAnchor.MiddleLeft);
            var icon = Icon(pack);
            if (icon != null) b.AddImage(icon, IconSize, IconSize, "icon_" + index);
            b.AddToggle(pack != null ? pack.Name : id, state.Enabled, on => state.Enabled = on, "on_" + index);
            if (pack != null) b.WithTooltip(Details(pack));
            b.AddFlexibleSpace();
            if (pack != null) b.AddLabel(Numbers(pack, state.Enabled), "numbers_" + index);
            b.AddButton(L("manager_up"), () => Move(id, -1), "up_" + index);
            b.Last.SetFixedWidth(MoveWidth);
            if (index == 0) b.Last.SetInteractable(false);
            b.AddButton(L("manager_down"), () => Move(id, 1), "down_" + index);
            b.Last.SetFixedWidth(MoveWidth);
            if (index == count - 1) b.Last.SetInteractable(false);
            b.End();
        }

        // The tooltip of a pack's switch: name and version, author, the manifest's description, picture count.
        static string Details(Packs.Pack p)
        {
            var lines = new List<string> { p.Name + (p.Version.Length > 0 ? "  v" + p.Version : "") };
            if (p.Author.Length > 0) lines.Add(L("manager_author", p.Author));
            if (p.Description.Length > 0) lines.Add(p.Description);
            lines.Add(L("manager_pictures", p.Textures));
            return string.Join("\n", lines.ToArray());
        }

        // What the pack does right now, as last applied.
        static string Numbers(Packs.Pack p, bool on)
        {
            if (!on || !Settings.IsEnabled(p.Id)) return L("manager_off", p.Textures);
            var parts = new List<string> { L("manager_used", p.Used, p.Textures) };
            if (p.Covered > 0) parts.Add(L("manager_covered", p.Covered));
            if (p.Skipped > 0) parts.Add(L("manager_skipped", p.Skipped));
            return string.Join("   ", parts.ToArray());
        }

        // The pack's pack.png as a Sprite, cached; null when the pack has no icon.
        static Sprite Icon(Packs.Pack p)
        {
            if (p == null || p.Icon == null) return null;
            Sprite s;
            if (!icons.TryGetValue(p.Icon, out s) || s == null)
            {
                s = Sprite.Create(p.Icon, new Rect(0, 0, p.Icon.width, p.Icon.height), new Vector2(0.5f, 0.5f));
                icons[p.Icon] = s;
            }
            return s;
        }

        // Up or down by one place among the installed packs (PackOrder.Move), then redraws the rows.
        static void Move(string id, int delta)
        {
            if (draft == null) return;
            if (PackOrder.Move(draft, id, delta, new HashSet<string>(InstalledIds()))) Build();
        }

        // Saves the draft, rebuilds every pack's replacements at once (TexturePacksMod.Rebuild), then reloads the
        // draft so the window shows the numbers of what was just applied.
        static void Apply()
        {
            if (draft == null) return;
            Settings.Save(draft);
            TexturePacksMod.Rebuild();
            draft = Settings.Load(InstalledIds());
            Build();
        }
    }
}
