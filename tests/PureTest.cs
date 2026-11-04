// Checks the mod's decisions (src/*.Pure.cs) outside the game: no game or Unity assembly is needed.
//   csc -out:PureTest.exe tests\PureTest.cs src\PackKey.Pure.cs && PureTest.exe
using System;
using System.Collections.Generic;
using TexturePacks;

static class PureTest
{
    static int failed;

    static void Check(string what, bool ok)
    {
        if (!ok) failed++;
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what);
    }

    static void Key(string name, string path, string sprite)
    {
        var k = PackKey.Parse(name);
        Check(name + " -> " + (path == null ? "not a key" : path + (sprite == null ? "" : " @ " + sprite)),
            path == null ? k == null : k != null && k.Path == path && k.Sprite == sprite);
    }

    static void Scene(string name, string texture, int w, int h)
    {
        var k = PackKey.Parse(name);
        Check(name + " -> scene " + texture + " " + w + "x" + h,
            k != null && k.IsSceneTexture && !k.IsAtlas && k.Path == texture && k.Width == w && k.Height == h);
    }

    static string Ids(IEnumerable<PackState> packs) { return PackOrder.Format(packs); }
    static string Join(IEnumerable<string> ids) { return string.Join(",", ids); }

    static void Order()
    {
        Check("parse and format round-trip", Ids(PackOrder.Parse("a;-b;c")) == "a;-b;c");
        Check("blank parts, spaces and repeats are dropped", Ids(PackOrder.Parse(" a ;;- b;a;-")) == "a;-b");
        Check("empty text is an empty order", PackOrder.Parse("").Count == 0 && PackOrder.Parse(null).Count == 0);

        var installed = new List<string> { "c", "new1", "a", "new2" };   // Mods-menu order
        var merged = PackOrder.Merge(PackOrder.Parse("a;-b;c"), installed);
        Check("new packs go on top, switched on, in Mods-menu order; saved order kept below",
            Ids(merged) == "new1;new2;a;-b;c");
        Check("an uninstalled pack stays in the saved line", merged.Exists(p => p.Id == "b"));
        Check("the manager shows installed packs only", Ids(PackOrder.Visible(merged, installed)) == "new1;new2;a;c");

        // Top wins: applied lowest first, switched-off and uninstalled packs left out.
        var list = PackOrder.Parse("top;-off;gone;bottom");
        var here = new HashSet<string> { "top", "off", "bottom" };
        Check("apply order is lowest priority first", Join(PackOrder.ApplyOrder(list, here)) == "bottom,top");

        // A move passes the next installed pack and steps over ones that are not installed.
        Check("move down past the next installed pack", PackOrder.Move(list, "off", 1, here) && Ids(list) == "top;gone;bottom;-off");
        Check("visible order after moving down", Ids(PackOrder.Visible(list, here)) == "top;bottom;-off");
        Check("move back up", PackOrder.Move(list, "off", -1, here) && Ids(list) == "top;gone;-off;bottom");
        Check("the top pack cannot move up", !PackOrder.Move(list, "top", -1, here) && Ids(list) == "top;gone;-off;bottom");
        Check("the bottom pack cannot move down", !PackOrder.Move(list, "bottom", 1, here));
        Check("an unknown pack does not move", !PackOrder.Move(list, "nope", 1, here));
        Check("moving to the top steps over an uninstalled pack",
            PackOrder.Move(list, "bottom", -1, here) && PackOrder.Move(list, "bottom", -1, here) &&
            Ids(PackOrder.Visible(list, here)) == "bottom;top;-off");
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
            PackKey.Parse("Items~a@b").Id != PackKey.Parse("Items~a").Id);

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
            PackKey.Parse("wall.640x360").Id != PackKey.Parse("wall.960x540").Id);
        Check("scene id round-trips", PackKey.Parse(PackKey.SceneId("desk animations1", 360, 29)).Id == "desk animations1.360x29");
        Check("Resources keys are not scene keys", !PackKey.Parse("Items~items_tool@wrench").IsSceneTexture);

        Order();
        Console.WriteLine(failed == 0 ? "all passed" : failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
