// Checks the mod's decisions (src/*.Pure.cs) outside the game: no game or Unity assembly is needed.
//   csc -out:PureTest.exe tests\PureTest.cs src\PackIds.Pure.cs src\AssetId.Pure.cs src\AssetIdCodec.Pure.cs src\Origins.Pure.cs src\PackOrder.Pure.cs && PureTest.exe
using System;
using System.Collections.Generic;
using TexturePacks;

// Example-based checks of the pure modules, one line per case; tests/run.sh prints only the failures. The examples
// are the documented cases (the file names in AssetIdCodec, the saved line in PackOrder). DiffTest covers the same code
// on generated inputs.
static class PureTest
{
    static int failed;

    static void Check(string what, bool ok)
    {
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
    }

    // A game picture: a Resources sprite (path, and the sprite's name inside an atlas), or not an identity at all.
    static void Key(string name, string path, string sprite)
    {
        var k = AssetIdCodec.Parse(name);
        Check(name + " -> " + (path == null ? "not a key" : path + (sprite == null ? "" : " @ " + sprite)),
            path == null ? k == null
                : k != null && k.Origin.IsGame && !k.Locator.IsScene && k.Locator.Path == path && k.Locator.Name == sprite);
    }

    static void Scene(string name, string texture, int w, int h)
    {
        var k = AssetIdCodec.Parse(name);
        Check(name + " -> scene " + texture + " " + w + "x" + h,
            k != null && k.Origin.IsGame && k.Locator.IsScene && k.Locator.Path == texture && k.Locator.Width == w && k.Locator.Height == h);
    }

    const string Uuid = "3f2a9c1e-5b7d-4e2a-9c1e-0a1b2c3d4e5f";

    // A picture of a mod: the origin's token and kind, the container and the asset's name.
    static void Mod(string name, OriginKind kind, string token, string container, string asset)
    {
        var k = AssetIdCodec.Parse(name);
        Check(name + " -> mod " + token + " " + container + "/" + asset,
            k != null && k.Origin.Kind == kind && k.Origin.Token == token && k.Locator.IsModAsset &&
            k.Locator.Path == container && k.Locator.Name == asset);
    }

    static void NotAKey(string name) { Check(name + " -> not a key", AssetIdCodec.Parse(name) == null); }

    static string Spelled(string name) { return AssetIdCodec.Format(AssetIdCodec.Parse(name)); }

    static void Identity()
    {
        Mod("machine_mod+Items~crusher_body", OriginKind.ModId, "machine_mod", "Items", "crusher_body");
        Mod(Uuid + "+Paper~note", OriginKind.ModUuid, Uuid, "Paper", "note");
        Mod(Uuid.ToUpperInvariant() + "+Paper~note", OriginKind.ModUuid, Uuid, "Paper", "note");   // read in any case, kept lower
        Mod("a.b-c_d+Items~x~y", OriginKind.ModId, "a.b-c_d", "Items", "x~y");     // the name starts at the first "~"
        Mod("m+Code~a+b", OriginKind.ModId, "m", "Code", "a+b");                    // "+" may be part of the name
        NotAKey("+Items~a");            // no origin
        NotAKey("m+Items");             // no locator
        NotAKey("m+Items~");            // no name
        NotAKey("m+~a");                // no container
        NotAKey("m+It ems~a");          // a container is one word
        NotAKey("bad id+Items~a");      // a space is not in a mod id
        NotAKey(".hidden+Items~a");     // the game refuses an id that starts with a dot
        NotAKey("m+");
        Key("items~items_tool@a+b", null, null);      // "+" is never part of a game picture

        Check("a mod picture is not a scene picture", AssetIdCodec.Parse("m+Items~wall.640x360").Locator.IsModAsset);
        Check("origin marker textures are not identities", AssetIdCodec.Parse("origin." + Uuid) == null);
        Check("mod id and UUID are different origins",
            !AssetIdCodec.Parse("m+Items~a").Equals(AssetIdCodec.Parse(Uuid + "+Items~a")));
        Check("containers are told apart", !AssetIdCodec.Parse("m+Items~a").Equals(AssetIdCodec.Parse("m+Paper~a")));
        Check("the same name in two mods is two identities", !AssetIdCodec.Parse("m+Items~a").Equals(AssetIdCodec.Parse("n+Items~a")));
        Check("the game has no mod assets and a mod has no scene textures",
            AssetId.Of(Origin.Game, Locator.ModAsset("Items", "a")) == null &&
            AssetId.Of(Origin.FromModId("m"), Locator.Scene("wall", 1, 1)) == null &&
            AssetId.Of(Origin.FromModId("m"), Locator.Atlas("a/b", "c")) == null);
        Check("an id the game refuses is no origin", Origin.FromModId("bad id") == null && Origin.FromModId("") == null);

        // Format is the reverse of Parse, for the spellings it writes; the game's names are the same as before.
        foreach (string name in new[] { "items~items_tool2@blender", "window~machinery~bevel_frame", "store_mid.960x540",
                                        "m+Items~crusher_body", Uuid + "+Paper~note", "a.b.32x32" })
            Check("Format(Parse(" + name + ")) is " + name, Spelled(name) == name);
        Check("an UPPER-case UUID is written in lower case", Spelled(Uuid.ToUpperInvariant() + "+Paper~note") == Uuid + "+Paper~note");
        Check("a sprite name that would read as a size has no file name",
            AssetIdCodec.Format(AssetId.Of(Origin.Game, Locator.Atlas("a/b", "c.5x5"))) == null);
        Check("a game path with '+' has no file name", AssetIdCodec.Format(AssetId.Of(Origin.Game, Locator.Single("a+b/c"))) == null);
        Check("a scene is looked up by name and size", AssetIdCodec.SceneName("desk animations1", 360, 29) == "desk animations1.360x29");
    }

    static void Origins()
    {
        Guid u = new Guid(Uuid), other = new Guid("00000000-0000-0000-0000-000000000001");
        string[] none = new string[0];
        Check("a marker key is origin. and the UUID", OriginMarker.KeyFor(u) == "origin." + Uuid);
        Guid back;
        Check("a marker key reads back", OriginMarker.TryParse("origin." + Uuid.ToUpperInvariant(), out back) && back == u);
        Check("other keys are no marker", !OriginMarker.TryParse("origin.", out back) && !OriginMarker.TryParse("origin.123", out back) &&
            !OriginMarker.TryParse("pack", out back) && !OriginMarker.TryParse(null, out back) && !OriginMarker.TryParse(Uuid, out back));

        var plain = OriginResolver.Resolve("my_mod", new[] { "icon", "wall.1x1" }, null);
        Check("without a declaration the mod id stands in", plain.Origin.Kind == OriginKind.ModId && plain.Origin.Token == "my_mod" && plain.Problem == null);
        var marked = OriginResolver.Resolve("my_mod", new[] { "icon", OriginMarker.KeyFor(u) }, null);
        Check("a marker picture gives the UUID", marked.Origin.Equals(Origin.FromUuid(u)) && marked.Problem == null);
        Check("the same marker twice is one marker", OriginResolver.Resolve("m", new[] { OriginMarker.KeyFor(u), OriginMarker.KeyFor(u) }, null).Problem == null);
        var declared = OriginResolver.Resolve("my_mod", none, u);
        Check("a declaration from code gives the UUID", declared.Origin.Equals(Origin.FromUuid(u)) && declared.Problem == null);
        Check("a marker that agrees with the declaration is fine", OriginResolver.Resolve("m", new[] { OriginMarker.KeyFor(u) }, u).Problem == null);
        var clash = OriginResolver.Resolve("m", new[] { OriginMarker.KeyFor(other) }, u);
        Check("the declaration wins over a marker that disagrees, and says so", clash.Origin.Equals(Origin.FromUuid(u)) && clash.Problem != null);
        var many = OriginResolver.Resolve("m", new[] { OriginMarker.KeyFor(u), OriginMarker.KeyFor(other) }, null);
        Check("two different markers give no UUID and a problem", many.Origin.Kind == OriginKind.ModId && many.Problem != null);
        Check("a mod id the game refuses has no origin", OriginResolver.Resolve("bad id", none, null).Origin == null);
    }

    static string Ids(IEnumerable<PackState> packs) { return PackOrder.Format(packs); }
    static string Join(IEnumerable<string> ids) { return string.Join(",", ids); }

    static void Order()
    {
        Check("every pack is written with its mark", Ids(PackOrder.Parse("+a;-b;+c")) == "+a;-b;+c");
        Check("lines from before the marks read the same", Ids(PackOrder.Parse("a;-b;c")) == "+a;-b;+c");
        Check("an id may start with a mark itself", Ids(PackOrder.Parse("+-a;-+b")) == "+-a;-+b" &&
            PackOrder.Parse("+-a;-+b")[0].Id == "-a" && PackOrder.Parse("+-a;-+b")[0].Enabled);
        Check("';' and '%' inside an id are escaped", Ids(new[] { new PackState("a;b%", true) }) == "+a%3Bb%25" &&
            PackOrder.Parse("+a%3Bb%25")[0].Id == "a;b%");
        Check("blank parts and repeats are dropped", Ids(PackOrder.Parse(";;+a;;-a;+;-")) == "+a");
        Check("empty text is an empty order", PackOrder.Parse("").Count == 0 && PackOrder.Parse(null).Count == 0);

        var installed = new List<string> { "c", "new1", "a", "new2" };   // Mods-menu order
        var merged = PackOrder.Merge(PackOrder.Parse("+a;-b;+c"), installed);
        Check("new packs go on top, switched on, in Mods-menu order; saved order kept below",
            Ids(merged) == "+new1;+new2;+a;-b;+c");
        Check("an uninstalled pack stays in the saved line", merged.Exists(p => p.Id == "b"));
        Check("the manager shows installed packs only", Ids(PackOrder.Visible(merged, installed)) == "+new1;+new2;+a;+c");

        // Top wins: applied lowest first, switched-off and uninstalled packs left out.
        var list = PackOrder.Parse("+top;-off;+gone;+bottom");
        var here = new HashSet<string> { "top", "off", "bottom" };
        Check("apply order is lowest priority first", Join(PackOrder.ApplyOrder(list, here)) == "bottom,top");

        // A move passes the next installed pack and steps over ones that are not installed.
        Check("move down past the next installed pack", PackOrder.Move(list, "off", 1, here) && Ids(list) == "+top;+gone;+bottom;-off");
        Check("visible order after moving down", Ids(PackOrder.Visible(list, here)) == "+top;+bottom;-off");
        Check("move back up", PackOrder.Move(list, "off", -1, here) && Ids(list) == "+top;+gone;-off;+bottom");
        Check("the top pack cannot move up", !PackOrder.Move(list, "top", -1, here) && Ids(list) == "+top;+gone;-off;+bottom");
        Check("the bottom pack cannot move down", !PackOrder.Move(list, "bottom", 1, here));
        Check("an unknown pack does not move", !PackOrder.Move(list, "nope", 1, here));
        Check("moving to the top steps over an uninstalled pack",
            PackOrder.Move(list, "bottom", -1, here) && PackOrder.Move(list, "bottom", -1, here) &&
            Ids(PackOrder.Visible(list, here)) == "+bottom;+top;-off");
        Check("the top pack then wins", Join(PackOrder.ApplyOrder(list, here)) == "top,bottom");
    }

    static int Main()
    {
        Key("Items~items_tool@wrench", "Items/items_tool", "wrench");
        Key("Window~elements_outer@background_fill", "Window/elements_outer", "background_fill");
        Key("Backgrounds~space_nebula", "Backgrounds/space_nebula", null);
        Key("Items~characteratlas@guard_idle_0", "Items/characteratlas", "guard_idle_0");
        Key("logo", null, null);                       // a pack's own texture, not a sprite key
        Key("Items@wrench", null, null);               // no folder in the path
        Key("Items~@wrench", null, null);
        Key("~items_tool@wrench", null, null);
        Key("Items~items_tool@", null, null);
        Key("Items~items_tool@a@b", null, null);
        Key("", null, null);
        Check("atlas and single-sprite ids differ",
            Spelled("Items~a@b") != Spelled("Items~a") && !AssetIdCodec.Parse("Items~a@b").Equals(AssetIdCodec.Parse("Items~a")));

        Scene("store_mid.960x540", "store_mid", 960, 540);
        Scene("wall.640x360", "wall", 640, 360);
        Scene("letter incorrect7.137x96", "letter incorrect7", 137, 96);   // scene names may hold spaces
        Scene("noun-beaker-7821176-FFFFFF.100x100", "noun-beaker-7821176-FFFFFF", 100, 100);
        Scene("a.b.32x32", "a.b", 32, 32);                // only the last dot starts the size
        Key("wall.640", null, null);                       // not a size
        Key("wall.x360", null, null);
        Key("wall.0x360", null, null);
        Key(".32x32", null, null);                         // no name
        Key("wall.640x360x2", null, null);
        Check("same name, different size, different id",
            Spelled("wall.640x360") != Spelled("wall.960x540") && !AssetIdCodec.Parse("wall.640x360").Equals(AssetIdCodec.Parse("wall.960x540")));
        Check("scene name round-trips", Spelled(AssetIdCodec.SceneName("desk animations1", 360, 29)) == "desk animations1.360x29");
        Check("Resources keys are not scene keys", !AssetIdCodec.Parse("Items~items_tool@wrench").Locator.IsScene);

        Identity();
        Origins();
        Order();
        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
