// Builds the texture catalog: every texture a pack can replace, with its file name, size and category, and no art.
//   Catalog.exe <inventory.json> <catalog.tsv> [placeholder folder]
// The inventory lists what the game ships (Resources atlases with their sprites, and the textures in the menu and
// shop scene files) with the facts the rules below need. With a placeholder folder, also draws one flat rectangle of
// the original size for every scene texture, sorted into the same category folders a creator works in.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

// Run by tools/kit.sh with the inventory from tools/export/inventory.py. The categories are decided here, by name
// patterns checked against what the inventory says (size, how much of a texture is opaque); the inventory holds
// only facts. After a game update, a new picture falls into "ui" (in-game or main-menu) unless a rule claims it.
static class Catalog
{
    // One catalog row; written as one line of catalog.tsv, which PackBuilder and tools/export/export.py read.
    sealed class Entry
    {
        public string Key, Kind, Category, Group, Source, Name;
        public int Width, Height;
    }

    // ------------------------------------------------------------------ rules
    // Categories agreed for creators. A sprite or texture the game never shows, and the engine's own textures
    // (noise, anti-aliasing tables, fonts), are left out.
    const RegexOptions I = RegexOptions.IgnoreCase;

    static readonly HashSet<string> FixtureAtlases = new HashSet<string> { "items/dossier", "items/items_furniture", "items/items_vending" };
    // Noise and anti-aliasing tables, glow and scan-line overlays, and the font atlases.
    static readonly Regex Engine = new Regex(@"^(ldr_|areatex|searchtex|font texture|scanlines|dust$|halo_glow)|(sdf atlas|\batlas)( \d+)?$", I);

    // Pictures the game ships but never shows, by "name.WxH" (several names exist in more than one size). Found by
    // following each one to the scene node or code that would show it.
    static readonly Dictionary<string, string> Unused = new Dictionary<string, string>
    {
        { "finalelog.32x32", "studio logo node is never switched on" },
        { "option_line.394x480", "old options panel line, never switched on" },
        { "perk_p.32x32", "old perk panel" }, { "selected_perk.455x80", "old perk panel" }, { "uiback1LDarkTan.64x64", "old perk panel" },
        { "uiback1LTan.64x64", "buttons that are never switched on" },
        { "Back_White.32x32", "removed negotiation entry" }, { "button_glass.32x32", "removed negotiation button" },
        { "button_integoration.32x32", "removed negotiation button" },
        { "business_card_template.200x125", "old paper template" }, { "guide.320x400", "old paper template" },
        { "tutorial_sheet.220x320", "old paper template" },
        { "ending.48x48", "game-over node is never switched on" }, { "inventor_main.48x32", "test node" },
        { "label.128x32", "InjectorUI never switches it on" },
        { "letter incorrect7.137x96", "StampUI repeats LetterM6 in its place" }, { "letter incorrect8.137x96", "StampUI repeats LetterM6 in its place" },
        { "refresh.16x16", "scanner refresh button was removed" }, { "rep_indicator.32x32", "ReputationElement never shows it" },
        { "Serial.28x18", "FuelUI shows the serial as text" },
        { "spy.288x211", "portrait key the code never uses" }, { "trailer_rev2.288x211", "portrait key the code never uses" },
        { "uiback1LGreen.64x64", "commissary title panel is never switched on" },
        { "water_container.512x512", "old guide page" }, { "wild_pat.135x145", "test node" },
    };
    static readonly Regex UnusedPattern = new Regex(@"^wanted_portait\d\.34x32$");   // the wanted poster's faces are painted into it

    // The new-game screens (specialization, perks, difficulty) in the main menu scene, by "name.WxH".
    static readonly Dictionary<string, string> NewGame = Groups(
        "specializations", "icons.190x38 slot_normal.64x64 slot_selected.64x64",
        "shared", "arrow_next_hover.32x32 arrow_next_normal.32x32 arrow_prev_hover.32x32 arrow_prev_normal.32x32 " +
                  "frame_2_square.6x6 new_game_bg.556x480 tab.129x31 uibacktop1.16x16",
        "perk-screen", "perk_n.32x32 perk_slot_hover.64x72 perk_slot_lock.16x18 perk_slot_normal.64x72 perk_tooltip_lock.16x18 " +
                       "perks_tooltip.442x178",
        "perk-icons", "2perkSet2.828x36 addiction_narcotic.32x32 additionc_alcohol.32x32 additionc_gambling.32x32 " +
                      "additionc_nicotine.32x32 alert.32x32 bad_lease.32x32 blabbermouth.32x32 blackmail.32x32 cassette.32x32 " +
                      "colorful.32x32 connected.32x32 convict.32x32 diplomatic.32x32 illiterate.32x32 intimidating.32x32 " +
                      "lucky.36x36 minimalist.32x32 model_citizen.32x32 noun-beaker-7821176-FFFFFF.100x100 " +
                      "noun-flag-7646741-FFFFFF.100x100 noun-flyer-7880108-FFFFFF.100x100 noun-people-6240743-FFFFFF.256x256 " +
                      "prime_real_estate.32x32 rev.32x32 saving.32x32 sawyer_crew.32x32 scav.32x32 secret_admirer.32x32 " +
                      "sentinel.32x32 short_sleeper.32x32 side_street.32x32 snitch.36x36 stopwatch.100x100 vending.32x32 " +
                      "welfare_program.32x32 working_pipes.32x32");

    static Dictionary<string, string> Groups(params string[] pairs)
    {
        var d = new Dictionary<string, string>();
        for (int i = 0; i < pairs.Length; i += 2)
            foreach (var key in pairs[i + 1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) d[key] = pairs[i];
        return d;
    }
    static readonly Regex People = new Regex(@"^(eleanor|georgio|jackson|landlord|lun|odin|sawyer|serpico|spy|wild|mentor|nellis|grandpa_cheng|lou_sec|case_enjoyer|waffle_enjoyer|courrier|trailer_|wanted\d|femgenerics|special$)", I);
    static readonly Regex Structure = new Regex(@"^(store_|wall\d?$|door$|door lock|window$|window_top|glass_door|shutter_door|lighting|front_counter|desk$|screen shadow|hidden_space)", I);
    // The wires light up to point at a fixture, so they go with the fixtures.
    static readonly Regex HighlightWires = new Regex(@"^(wires|wires2|wires_table|calendar wire|connectimg wire)$", I);
    static readonly Regex MenuBackdrops = new Regex(@"^(bazaar|MenuMockupExported|MenuMockupExportedNoText)$");
    static readonly Regex Fixtures = new Regex(@"^(calendar$|phone$|radio$|poster|marketplace_pc|negociation_tablet|sleeping_bag|display_case|client_display|credit_display|reputation_box$|bell_button_sheet|shutter_button_sheet)", I);
    // What the player sees when appraising an item close up belongs with the item: a pack that redraws the item has
    // to redraw these too.
    static readonly Regex Closeup = new Regex(@"^(box_ok$|\d(steel|carbon|gold)_|berry_|bar_code|bottom_bun|top_bun|double_meat|ribs$|pickles|warp_normal|wrap_open|facility$|davre_logo|ribwich_logo|card$|card_rev|card_wild|gp_2x|red_imp|text box|cartridge|condition_|color_(high|normal|poor)|chip|letter[ -]|letter-chinese|logo[ -]|logo-|seal[ -_]|seal-|serial$|injector_|indicator_|slot$|cap$)", I);
    // Check panels opened while appraising, and guide illustrations that picture an item's details.
    static readonly Regex Reference = new Regex(@"^(cigarette_color_card_item|logo_checker|test_strips|colorChecker|water_purity_2x|cropped-|example_|guide_(davre|ribwich)_logo|guide_indicator_|guide_color_guide)", I);
    static readonly Regex Silhouettes = new Regex(@"^(far|mid)(Backpack|Bandana|Gasmask|Puncho|TrechCoat)$");
    static readonly Regex Animated = new Regex(@"^(walker|client_shadow|desk animations|tape anim|recorder-sheet|clientele-sheet|pc_count-sheet|client_counter-sheet)", I);
    static readonly Regex Magnifier = new Regex(@"^magnifying[ _](glass|mask|screen)$", I);
    // A tool item opened full screen: its body goes with the item, the buttons and marks on it stay UI.
    static readonly Regex OpenedTools = new Regex(@"^(aqua_scan|chem_scan|plant_scanner|labeler|tray)$", I);
    static readonly Regex Clothing = new Regex(@"^(male|female|scav(male|female))[A-Z]");
    static readonly Regex Bodies = new Regex(@"^((male|female)(upper|lower)|shot_animation|body$|one_hand)", I);

    // Category and group of a Resources atlas, from its path; null leaves the atlas out of the catalog.
    static string[] ResourceCategory(string path)
    {
        if (path.StartsWith("window/")) return new[] { "ui", "window-elements" };
        if (FixtureAtlases.Contains(path)) return new[] { "scene", "fixtures" };
        if (path.StartsWith("items/")) return new[] { "item", "atlases" };
        return null;
    }

    // Category and group of a scene texture, or null to leave it out. The order of the tests matters: the first
    // rule that matches decides, so the specific groups come before the "ui" fallback at the end. "menu" is the
    // main menu scene file, the other is the shop; the shop-only groups are not tried on menu textures.
    static string[] SceneCategory(string name, int w, int h, bool menu, double coverage)
    {
        string l = name.ToLowerInvariant(), key = name + "." + w + "x" + h;
        if (Engine.IsMatch(name) || Unused.ContainsKey(key) || UnusedPattern.IsMatch(key)) return null;
        if (menu && MenuBackdrops.IsMatch(name)) return new[] { "scene", "illustrations" };
        if (!menu)
        {
            if (Silhouettes.IsMatch(name) || Animated.IsMatch(name)) return new[] { "scene", "animation" };
            if (l == "wall" && w == 640) return new[] { "scene", "illustrations" };   // the run-summary illustration
            if (HighlightWires.IsMatch(name)) return new[] { "scene", "fixtures" };
            if (w >= 640 && w * 9 == h * 16 && coverage > 0.5) return new[] { "scene", "backgrounds" };
            if (Structure.IsMatch(name) && w == 960) return new[] { "scene", "shop-structure" };
            if (Fixtures.IsMatch(name) && (w == 960 || l.Contains("sheet"))) return new[] { "scene", "fixtures" };
        }
        if (Clothing.IsMatch(name) || Bodies.IsMatch(name) || (People.IsMatch(name) && w == 288 && h == 211))
            return new[] { "character", "people" };
        if (!menu && Magnifier.IsMatch(name)) return new[] { "item", "appraisal-closeups" };
        if (!menu && OpenedTools.IsMatch(name)) return new[] { "item", "opened-tools" };
        if (!menu && Reference.IsMatch(name)) return new[] { "item", "appraisal-references" };
        if (!menu && Closeup.IsMatch(name) && l != "logo_checker") return new[] { "item", "appraisal-closeups" };
        string group;
        if (menu && NewGame.TryGetValue(key, out group)) return new[] { "new-game", group };
        return new[] { "ui", menu ? "main-menu" : "in-game" };
    }

    // ------------------------------------------------------------------ main
    // Reads the inventory, writes catalog.tsv sorted by category, group and name, and prints the count of each group.
    static int Main(string[] a)
    {
        if (a.Length < 2) { Console.Error.WriteLine("usage: Catalog.exe <inventory.json> <catalog.tsv> [placeholder folder]"); return 2; }
        var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        var inv = (Dictionary<string, object>)json.DeserializeObject(File.ReadAllText(a[0], Encoding.UTF8));
        var entries = new List<Entry>();
        var seen = new HashSet<string>();   // a name stored twice: one file replaces both

        foreach (Dictionary<string, object> atlas in (IEnumerable)inv["resources"])
        {
            string path = (string)atlas["path"];
            var cat = ResourceCategory(path);
            if (cat == null) continue;
            var sprites = ((IEnumerable)atlas["sprites"]).Cast<Dictionary<string, object>>().ToList();
            bool single = sprites.Count == 1 && path.Split('/').Last() == (string)sprites[0]["name"];
            foreach (var s in sprites)
            {
                if (Flag(s, "empty")) continue;
                string name = (string)s["name"];
                string key = path.Replace('/', '~') + (single ? "" : "@" + name);
                if (!seen.Add(key)) continue;
                entries.Add(new Entry
                {
                    Key = key, Kind = "resources", Category = cat[0], Group = cat[1],
                    Source = path, Name = name, Width = Int(s, "w"), Height = Int(s, "h")
                });
            }
        }

        foreach (Dictionary<string, object> file in (IEnumerable)inv["scenes"])
        {
            bool menu = (string)file["label"] == "menu";
            foreach (Dictionary<string, object> t in (IEnumerable)file["textures"])
            {
                // Empty textures, and Resources atlases that happen to sit in a scene file (listed under resources).
                if (Flag(t, "empty") || Flag(t, "dup")) continue;
                string name = (string)t["name"];
                int w = Int(t, "w"), h = Int(t, "h");
                if (w == 0 || h == 0) continue;
                var cat = SceneCategory(name, w, h, menu, t.ContainsKey("cov") ? Convert.ToDouble(t["cov"]) : 0);
                if (cat == null) continue;
                string key = name + "." + w + "x" + h;
                if (!seen.Add(key)) continue;
                entries.Add(new Entry { Key = key, Kind = "scene", Category = cat[0], Group = cat[1], Source = (string)file["file"], Name = name, Width = w, Height = h });
            }
        }

        entries = entries.OrderBy(e => e.Category).ThenBy(e => e.Group).ThenBy(e => e.Key, StringComparer.OrdinalIgnoreCase).ToList();
        var sb = new StringBuilder("file\tkind\tcategory\tgroup\twidth\theight\tsource\tname\n");
        foreach (var e in entries)
            sb.Append(e.Key + ".png\t" + e.Kind + "\t" + e.Category + "\t" + e.Group + "\t" + e.Width + "\t" + e.Height + "\t" + e.Source + "\t" + e.Name + "\n");
        File.WriteAllText(a[1], sb.ToString(), new UTF8Encoding(false));

        foreach (var g in entries.GroupBy(e => e.Category + "/" + e.Group))
            Console.WriteLine(g.Key.PadRight(32) + g.Count(e => e.Kind == "resources").ToString().PadLeft(5) + " resources" +
                g.Count(e => e.Kind == "scene").ToString().PadLeft(5) + " scene");
        Console.WriteLine("total " + entries.Count);

        if (a.Length > 2) Placeholders(entries.Where(e => e.Kind == "scene"), a[2]);
        return 0;
    }

    // Reads an optional boolean field of an inventory record; absent means false.
    static bool Flag(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v is bool && (bool)v; }
    static int Int(Dictionary<string, object> d, string k) { return Convert.ToInt32(d[k]); }

    // ------------------------------------------------------------------ placeholders
    static readonly Dictionary<string, Color> Colors = new Dictionary<string, Color>
    {
        { "item", Color.FromArgb(76, 154, 106) }, { "ui", Color.FromArgb(74, 120, 181) }, { "scene", Color.FromArgb(181, 137, 74) },
        { "character", Color.FromArgb(160, 85, 140) }, { "new-game", Color.FromArgb(110, 98, 181) }
    };

    // A flat rectangle of the category's colour with a one-pixel darker edge, so the bounds show at any size.
    static void Placeholders(IEnumerable<Entry> scene, string root)
    {
        int n = 0;
        foreach (var e in scene)
        {
            string dir = Path.Combine(root, Path.Combine(e.Category, e.Group));
            Directory.CreateDirectory(dir);
            var fill = Colors[e.Category];
            var edge = Color.FromArgb(fill.R * 3 / 5, fill.G * 3 / 5, fill.B * 3 / 5);
            using (var bmp = new Bitmap(e.Width, e.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(fill);
                    if (e.Width > 2 && e.Height > 2)
                        using (var pen = new Pen(edge, 1)) g.DrawRectangle(pen, 0, 0, e.Width - 1, e.Height - 1);
                }
                bmp.Save(Path.Combine(dir, e.Key + ".png"), ImageFormat.Png);
            }
            n++;
        }
        Console.WriteLine(n + " placeholder(s) in " + root);
    }
}
