// Turns a folder of pictures sorted into category folders into a texture pack the game can load.
//   PackBuilder.exe <source folder> <pack folder> [pack id] [pack name]
// The game reads only the top of a mod's Textures folder, so every PNG under the source folder is copied there
// flat. Each file is checked against catalog.tsv (next to this program): unknown names are reported, a size that
// differs from the game's stops the build. The pack gets TexturePack.dll and a manifest.xml if it has none yet.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

static class PackBuilder
{
    static int Main(string[] a)
    {
        if (a.Length < 2)
        {
            Console.Error.WriteLine("usage: PackBuilder.exe <source folder> <pack folder> [pack id] [pack name]");
            return 2;
        }
        string source = Path.GetFullPath(a[0]), pack = Path.GetFullPath(a[1]);
        string here = AppDomain.CurrentDomain.BaseDirectory;
        if (!Directory.Exists(source)) { Console.Error.WriteLine("source folder not found: " + source); return 2; }

        var catalog = ReadCatalog(Path.Combine(here, "catalog.tsv"));
        var files = Directory.GetFiles(source, "*.png", SearchOption.AllDirectories);
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int errors = 0, unknown = 0;
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
            int[] want;
            if (catalog == null) continue;
            if (!catalog.TryGetValue(name, out want))
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
            string id = a.Length > 2 ? a[2] : SafeId(Path.GetFileName(pack));
            string title = a.Length > 3 ? a[3] : Path.GetFileName(pack);
            File.WriteAllText(manifest, Manifest(id, title), new UTF8Encoding(false));
            Console.WriteLine("wrote manifest.xml with the id '" + id + "'; edit the name, author and description");
        }
        Console.WriteLine(byName.Count + " texture(s) in " + textures + (unknown > 0 ? ", " + unknown + " not in the catalog" : ""));
        return 0;
    }

    static string Rel(string root, string path) { return path.Substring(root.Length).TrimStart('\\', '/'); }

    // catalog.tsv: file, kind, category, group, width, height, ...
    static Dictionary<string, int[]> ReadCatalog(string path)
    {
        if (!File.Exists(path)) return null;
        var d = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(path, Encoding.UTF8).Skip(1))
        {
            var c = line.Split('\t');
            if (c.Length >= 6) d[c[0]] = new[] { int.Parse(c[4]), int.Parse(c[5]) };
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

    // Mod ids allow letters, digits, '_', '-' and '.'.
    static string SafeId(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' ? c : '_');
        return sb.Length > 0 ? sb.ToString() : "my_texture_pack";
    }

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
