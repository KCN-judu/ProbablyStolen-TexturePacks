// Turns a folder of pictures sorted into category folders into a texture pack the game can load.
//   PackBuilder.exe <source folder> <pack folder> [pack id] [pack name] [--schema FILE]...
// The game reads only the top of a mod's Textures folder, so every PNG under the source folder is copied there
// flat. Each file is checked against catalog.tsv (next to this program): unknown names are reported, a size that
// differs from the game's stops the build. The pack gets TexturePack.dll and a manifest.xml if it has none yet.
// Pictures of other mods are checked the same way against schemas (textures.schema.tsv, written by tools/schema):
// the ones given with --schema, and every *.schema.tsv in the schemas folder next to this program.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// Built by tools/kit.sh together with src/PackIds.Pure.cs, which supplies the mod-id rules the game applies, so the
// builder never writes an id the game would refuse, and with the identity files (AssetId.Pure.cs,
// AssetIdCodec.Pure.cs, Origins.Pure.cs), so a file name is read by the same codec as in the game and a picture is
// looked up by its identity, not by its spelling. It ships in the creator kit next to catalog.tsv, the schemas
// folder and TexturePack.dll, which it looks for in its own folder.
static class PackBuilder
{
    // Checks everything first and writes nothing if any file is wrong, so a failed build never leaves a half pack.
    // A file the catalog does not know is only a note: the game ignores unknown names, but a pack may keep other PNGs.
    static int Main(string[] args)
    {
        // --schema FILE may stand anywhere; what is left are the positional arguments.
        var schemaFiles = new List<string>();
        var rest = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] != "--schema") { rest.Add(args[i]); continue; }
            if (i + 1 >= args.Length) { Console.Error.WriteLine("--schema needs a file"); return 2; }
            schemaFiles.Add(Path.GetFullPath(args[++i]));
        }
        string[] a = rest.ToArray();
        if (a.Length < 2)
        {
            Console.Error.WriteLine("usage: PackBuilder.exe <source folder> <pack folder> [pack id] [pack name]");
            Console.Error.WriteLine("       and any number of: --schema <textures.schema.tsv of a mod>, for pictures of other mods");
            return 2;
        }
        string source = Path.GetFullPath(a[0]), pack = Path.GetFullPath(a[1]);
        string here = AppDomain.CurrentDomain.BaseDirectory;
        if (!Directory.Exists(source)) { Console.Error.WriteLine("source folder not found: " + source); return 2; }
        if (a.Length > 2 && !TexturePacks.PackIds.IsValid(a[2]))
        {
            Console.Error.WriteLine("'" + a[2] + "' is not a mod id the game accepts: 1 to " + TexturePacks.PackIds.MaxLength +
                " ASCII letters, digits, '_', '-' and '.', not starting with '.'");
            return 2;
        }

        var catalog = ReadCatalog(Path.Combine(here, "catalog.tsv"));
        string schemaFolder = Path.Combine(here, "schemas");
        if (Directory.Exists(schemaFolder))
            schemaFiles.AddRange(Directory.GetFiles(schemaFolder, "*.schema.tsv").OrderBy(f => f, StringComparer.Ordinal));
        var schemas = new Schemas();
        foreach (var file in schemaFiles)
        {
            string problem = schemas.Read(file);
            if (problem != null) { Console.Error.WriteLine("error: " + problem); return 2; }
        }
        var modFiles = new Dictionary<string, string>(StringComparer.Ordinal);   // identity of a mod's picture -> its file

        var files = Directory.GetFiles(source, "*.png", SearchOption.AllDirectories);
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int errors = 0, unknown = 0, unknownMod = 0;
        foreach (var f in files)
        {
            string name = Path.GetFileName(f);
            string other;
            if (byName.TryGetValue(name, out other))
            {
                Console.Error.WriteLine("error: " + name + " is in two places: " + Rel(source, other) + " and " + Rel(source, f));
                errors++;
                continue;
            }
            byName[name] = f;
            int w, h;
            if (!PngSize(f, out w, out h)) { Console.Error.WriteLine("error: " + Rel(source, f) + " is not a PNG file"); errors++; continue; }
            string stem = Path.GetFileNameWithoutExtension(name);
            var id = TexturePacks.AssetIdCodec.Parse(stem);
            if (id != null && id.Origin.IsMod)
            {
                CheckMod(id, stem, name, Rel(source, f), w, h, schemas, modFiles, ref errors, ref unknownMod);
                continue;
            }
            int[] want;
            if (catalog == null) continue;
            if (!catalog.TryGetValue(Key(stem), out want))
            {
                Console.WriteLine("note: " + Rel(source, f) + " is not in the catalog; the game will ignore it unless the name is right");
                unknown++;
            }
            else if (want[0] != w || want[1] != h)
            {
                Console.Error.WriteLine("error: " + Rel(source, f) + " is " + w + "x" + h + ", the game's texture is " + want[0] + "x" + want[1]);
                errors++;
            }
        }
        if (catalog == null) Console.WriteLine("note: catalog.tsv not found next to PackBuilder.exe; names and sizes were not checked");
        if (errors > 0) { Console.Error.WriteLine(errors + " error(s); nothing was written"); return 1; }

        string textures = Path.Combine(pack, "Textures");
        if (Directory.Exists(textures))
            foreach (var old in Directory.GetFiles(textures, "*.png")) File.Delete(old);
        Directory.CreateDirectory(textures);
        foreach (var kv in byName) File.Copy(kv.Value, Path.Combine(textures, kv.Key), true);

        string dll = Path.Combine(pack, "TexturePack.dll");
        if (!File.Exists(dll))
        {
            string stub = Path.Combine(here, "TexturePack.dll");
            if (File.Exists(stub)) File.Copy(stub, dll);
            else Console.WriteLine("note: TexturePack.dll not found next to PackBuilder.exe; copy it into the pack yourself");
        }
        string manifest = Path.Combine(pack, "manifest.xml");
        if (!File.Exists(manifest))
        {
            string id = a.Length > 2 ? a[2] : TexturePacks.PackIds.FromName(Path.GetFileName(pack));
            string title = a.Length > 3 ? a[3] : Path.GetFileName(pack);
            File.WriteAllText(manifest, Manifest(id, title), new UTF8Encoding(false));
            Console.WriteLine("wrote manifest.xml with the id '" + id + "'; edit the name, author and description");
        }
        Console.WriteLine(byName.Count + " texture(s) in " + textures + (unknown > 0 ? ", " + unknown + " not in the catalog" : "") +
            (unknownMod > 0 ? ", " + unknownMod + " of other mods not in a schema" : ""));
        return 0;
    }

    // The lookup key of a picture of the game: the codec's spelling of its identity, so a spelling the codec reads
    // the same way meets the catalog's. A name that is no identity keeps its own spelling.
    static string Key(string stem)
    {
        var id = TexturePacks.AssetIdCodec.Parse(stem);
        return (id == null ? null : TexturePacks.AssetIdCodec.Format(id)) ?? stem;
    }

    // What a schema says about one picture of a mod.
    sealed class Expected
    {
        public int Width, Height;
        public string Mod, Version;
    }

    // The schemas: pictures of mods by identity, and the UUID of each mod that has one, by mod id.
    sealed class Schemas
    {
        public readonly Dictionary<string, Expected> Pictures = new Dictionary<string, Expected>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> UuidOfMod = new Dictionary<string, string>(StringComparer.Ordinal);

        // textures.schema.tsv: a header line naming the columns, then one picture per line. Returns a problem, or null.
        public string Read(string path)
        {
            if (!File.Exists(path)) return "schema not found: " + path;
            var lines = File.ReadAllLines(path, Encoding.UTF8);
            if (lines.Length == 0) return path + " is empty";
            var col = new Dictionary<string, int>();
            var head = lines[0].Split('\t');
            for (int i = 0; i < head.Length; i++) col[head[i]] = i;
            foreach (var need in new[] { "file", "width", "height", "group", "version" })
                if (!col.ContainsKey(need)) return path + " has no '" + need + "' column; write it with tools/schema/schema.py";
            for (int n = 1; n < lines.Length; n++)
            {
                if (lines[n].Length == 0) continue;
                var c = lines[n].Split('\t');
                int w = 0, h = 0;
                if (c.Length < head.Length || !int.TryParse(c[col["width"]], out w) || !int.TryParse(c[col["height"]], out h))
                    return path + " line " + (n + 1) + " is not a row of the schema";
                string file = c[col["file"]];
                var id = TexturePacks.AssetIdCodec.Parse(Path.GetFileNameWithoutExtension(file));
                string key = id == null || !id.Origin.IsMod ? null : TexturePacks.AssetIdCodec.Format(id);
                if (key == null) return path + " line " + (n + 1) + ": '" + file + "' is not the file name of a mod's picture";
                Pictures[key] = new Expected { Width = w, Height = h, Mod = c[col["group"]], Version = c[col["version"]] };
                if (id.Origin.Kind == TexturePacks.OriginKind.ModUuid) UuidOfMod[c[col["group"]]] = id.Origin.Token;
            }
            return null;
        }
    }

    // Checks the file of a mod's picture against the schemas. A wrong size stops the build; the rest is advice. A
    // mod that is newer or older than its schema may have other pictures, so an unknown one is only a note.
    static void CheckMod(TexturePacks.AssetId id, string stem, string name, string shown, int w, int h, Schemas schemas,
        Dictionary<string, string> seen, ref int errors, ref int unknownMod)
    {
        string key = TexturePacks.AssetIdCodec.Format(id) ?? stem;
        string other;
        if (seen.TryGetValue(key, out other))
        {
            Console.Error.WriteLine("error: " + shown + " and " + other + " name the same picture");
            errors++;
            return;
        }
        seen[key] = name;

        Expected want;
        schemas.Pictures.TryGetValue(key, out want);
        string uuid;
        if (want == null && id.Origin.Kind == TexturePacks.OriginKind.ModId && schemas.UuidOfMod.TryGetValue(id.Origin.Token, out uuid))
        {
            // The schema knows this mod by a UUID: the name should carry it.
            var right = TexturePacks.AssetId.Of(TexturePacks.Origin.FromUuid(new Guid(uuid)), id.Locator);
            string rightKey = TexturePacks.AssetIdCodec.Format(right);
            Console.WriteLine("warning: " + shown + " names the mod by its id, and the mod has the UUID " + uuid +
                "; the file should be named " + rightKey + ".png");
            if (rightKey != null) schemas.Pictures.TryGetValue(rightKey, out want);
        }
        if (want == null)
        {
            Console.WriteLine("note: " + shown + " is not in any schema; the mod may be newer or older, or no schema was given for it");
            unknownMod++;
        }
        else if (want.Width != w || want.Height != h)
        {
            Console.Error.WriteLine("error: " + shown + " is " + w + "x" + h + ", the picture is " + want.Width + "x" + want.Height +
                " in " + want.Mod + (want.Version.Length > 0 ? " " + want.Version : ""));
            errors++;
        }
    }

    // The path of a file relative to the source folder, for messages.
    static string Rel(string root, string path) { return path.Substring(root.Length).TrimStart('\\', '/'); }

    // catalog.tsv: file, kind, category, group, width, height, ...
    static Dictionary<string, int[]> ReadCatalog(string path)
    {
        if (!File.Exists(path)) return null;
        var d = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(path, Encoding.UTF8).Skip(1))
        {
            var c = line.Split('\t');
            if (c.Length >= 6) d[Key(Path.GetFileNameWithoutExtension(c[0]))] = new[] { int.Parse(c[4]), int.Parse(c[5]) };
        }
        return d;
    }

    // The size sits in the PNG header (IHDR), so the picture itself is not decoded.
    static bool PngSize(string path, out int w, out int h)
    {
        w = h = 0;
        var b = new byte[24];
        using (var s = File.OpenRead(path))
            if (s.Read(b, 0, 24) < 24) return false;
        if (b[0] != 0x89 || b[1] != (byte)'P' || b[2] != (byte)'N' || b[3] != (byte)'G') return false;
        w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
        h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
        return true;
    }


    // The manifest of a new pack. The prerequisite on texture_packs is what makes the mod load the pack, and
    // 0.46D is the game version the mod lists in its own manifest.xml.
    static string Manifest(string id, string name)
    {
        name = System.Security.SecurityElement.Escape(name);
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Manifest>\n  <ID>" + id + "</ID>\n  <Name>" + name + "</Name>\n" +
            "  <Author>Your name</Author>\n  <ModVersion>1.0.0</ModVersion>\n" +
            "  <Description>A texture pack for the Texture Packs mod.</Description>\n" +
            "  <GameVersions>\n    <Version>0.46D</Version>\n  </GameVersions>\n" +
            "  <Prerequisites>\n    <Mod>texture_packs</Mod>\n  </Prerequisites>\n" +
            "  <Assemblies>\n    <Assembly>TexturePack.dll</Assembly>\n  </Assemblies>\n  <Capabilities />\n</Manifest>\n";
    }
}
