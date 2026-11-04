// Differential tests: each pure module (src/*.Pure.cs) against a plain reference model, on many inputs drawn with a
// fixed seed from alphabets full of the characters that sit on the boundaries ('-', '+', ';', '%', '.', non-ASCII).
//   csc -out:DiffTest.exe tests\DiffTest.cs src\PackIds.Pure.cs src\AssetId.Pure.cs src\AssetIdCodec.Pure.cs src\Origins.Pure.cs src\Swapper.Pure.cs src\PackOrder.Pure.cs src\PackResolve.Pure.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TexturePacks;

// The reference models below are written the plain way (split and join, linear scans), not the way the mod's code
// is written, so a bug has to appear in both to go unnoticed.
static class DiffTest
{
    const int Rounds = 20000;
    static int failed, checks;

    static void Same<T>(string what, T expected, T actual, string input)
    {
        checks++;
        if (EqualityComparer<T>.Default.Equals(expected, actual)) return;
        if (++failed <= 20) Console.WriteLine("FAIL  " + what + "  input: " + input + "  expected: " + expected + "  got: " + actual);
    }

    static string Show(IEnumerable<PackState> l) { return "[" + string.Join(" ", l.Select(p => (p.Enabled ? "+" : "-") + "<" + p.Id + ">").ToArray()) + "]"; }
    static string Show(IEnumerable<string> l) { return "[" + string.Join(" ", l.Select(s => "<" + s + ">").ToArray()) + "]"; }

    // ------------------------------------------------------------------ inputs

    static readonly Random rng = new Random(20261104);
    const string Edges = "-+;%.%_";
    const string Plain = "abAZ09";
    const string Wild = "é文 \t";
    const string Hex = "2535Bb";

    static char AnyChar()
    {
        int k = rng.Next(10);
        string pool = k < 4 ? Edges : k < 7 ? Plain : k < 9 ? Hex : Wild;
        return pool[rng.Next(pool.Length)];
    }

    static string AnyString(int max)
    {
        int n = rng.Next(max + 1);
        var c = new char[n];
        for (int i = 0; i < n; i++) c[i] = AnyChar();
        return new string(c);
    }

    // Ids the codec must carry: anything non-empty, boundary characters included.
    static List<PackState> AnyOrder(int max)
    {
        var seen = new HashSet<string>();
        var list = new List<PackState>();
        int n = rng.Next(max + 1);
        while (list.Count < n)
        {
            string id = AnyString(6);
            if (id.Length > 0 && seen.Add(id)) list.Add(new PackState(id, rng.Next(2) == 0));
        }
        return list;
    }

    // ------------------------------------------------------------------ reference models

    // The game's ModLoader.IsValidModId, transcribed from its IL.
    static bool RefIsValid(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64 || id[0] == '.') return false;
        foreach (char c in id)
            if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.'))
                return false;
        return id != "." && id != "..";
    }

    // The order operations on plain lists, written the obvious way.
    static List<PackState> RefMerge(IList<PackState> saved, IList<string> installed)
    {
        var known = new HashSet<string>(saved.Select(p => p.Id));
        var fresh = installed.Where(id => !known.Contains(id)).Distinct().Select(id => new PackState(id, true));
        return fresh.Concat(saved.Select(p => new PackState(p.Id, p.Enabled))).ToList();
    }

    static List<PackState> RefVisible(IList<PackState> packs, ICollection<string> installed)
    {
        return packs.Where(p => installed.Contains(p.Id)).ToList();
    }

    static List<string> RefApplyOrder(IList<PackState> packs, ICollection<string> installed)
    {
        return packs.Where(p => p.Enabled && installed.Contains(p.Id)).Select(p => p.Id).Reverse().ToList();
    }

    // Moving swaps a pack with its installed neighbour: it goes right before (up) or right after (down) it.
    static bool RefMove(List<PackState> packs, string id, int delta, ICollection<string> installed)
    {
        var visible = RefVisible(packs, installed);
        int k = visible.FindIndex(p => p.Id == id);
        if (k < 0 || !installed.Contains(id)) return MoveHidden(packs, id, delta, installed);
        int n = k + Math.Sign(delta);
        if (delta == 0 || n < 0 || n >= visible.Count) return false;
        var me = packs.First(p => p.Id == id);
        var neighbour = visible[n];
        packs.Remove(me);
        int at = packs.IndexOf(neighbour);
        packs.Insert(delta < 0 ? at : at + 1, me);
        return true;
    }

    // A pack that is not installed is never shown, but the operation is still defined: it jumps over the packs
    // between it and the next installed one in that direction.
    static bool MoveHidden(List<PackState> packs, string id, int delta, ICollection<string> installed)
    {
        int from = packs.FindIndex(p => p.Id == id);
        if (from < 0 || delta == 0) return false;
        int step = Math.Sign(delta), to = from + step;
        while (to >= 0 && to < packs.Count && !installed.Contains(packs[to].Id)) to += step;
        if (to < 0 || to >= packs.Count) return false;
        var me = packs[from];
        packs.RemoveAt(from);
        packs.Insert(to, me);
        return true;
    }

    // The previous codec, kept as the reference for lines written before the marks.
    static List<PackState> RefLegacyParse(string text)
    {
        var list = new List<PackState>();
        var seen = new HashSet<string>();
        if (string.IsNullOrEmpty(text)) return list;
        foreach (var raw in text.Split(';'))
        {
            string part = raw;
            bool enabled = true;
            if (part.Length > 0 && part[0] == '-') { enabled = false; part = part.Substring(1); }
            if (part.Length > 0 && seen.Add(part)) list.Add(new PackState(part, enabled));
        }
        return list;
    }

    // ------------------------------------------------------------------ tests

    static void Ids()
    {
        // Every string up to length 3 over a boundary alphabet, then random longer ones.
        const string small = "a.-_;é Z9+";
        var all = new List<string> { "" };
        for (int len = 1; len <= 3; len++)
            all = all.Concat(all.Where(s => s.Length == len - 1).SelectMany(s => small.Select(c => s + c))).ToList();
        foreach (var s in all) Same("IsValid", RefIsValid(s), PackIds.IsValid(s), "<" + s + ">");
        for (int i = 0; i < Rounds; i++)
        {
            string s = rng.Next(20) == 0 ? new string('a', 60 + rng.Next(10)) : AnyString(12);
            Same("IsValid", RefIsValid(s), PackIds.IsValid(s), "<" + s + ">");
            string id = PackIds.FromName(s);
            Same("FromName gives a valid id", true, RefIsValid(id), "<" + s + "> -> <" + id + ">");
            Same("FromName is stable", id, PackIds.FromName(id), "<" + id + ">");
        }
    }

    static void Codec()
    {
        for (int i = 0; i < Rounds; i++)
        {
            var order = AnyOrder(6);
            string line = PackOrder.Format(order);
            Same("parse(format(x)) == x", Show(order), Show(PackOrder.Parse(line)), line);
        }
        // Lines written before the marks read as they did, for the ids the game allows.
        for (int i = 0; i < Rounds; i++)
        {
            var ids = Enumerable.Range(0, rng.Next(6)).Select(_ => PackIds.FromName(AnyString(6))).Where(id => id[0] != '-').Distinct();
            string line = string.Join(";", ids.Select(id => (rng.Next(2) == 0 ? "-" : "") + id).ToArray());
            Same("old lines read the same", Show(RefLegacyParse(line)), Show(PackOrder.Parse(line)), line);
        }
        // Arbitrary text never throws and never yields an empty or repeated id.
        for (int i = 0; i < Rounds; i++)
        {
            string text = AnyString(20);
            var parsed = PackOrder.Parse(text);
            Same("no empty ids", true, parsed.All(p => p.Id.Length > 0), text);
            Same("no repeated ids", parsed.Count, parsed.Select(p => p.Id).Distinct().Count(), text);
        }
    }

    static void Operations()
    {
        for (int i = 0; i < Rounds; i++)
        {
            var saved = AnyOrder(6);
            var installed = saved.Where(_ => rng.Next(3) > 0).Select(p => p.Id).Concat(AnyOrder(2).Select(p => p.Id)).ToList();
            installed = installed.OrderBy(_ => rng.Next()).ToList();
            var set = new HashSet<string>(installed);
            string input = Show(saved) + " installed " + Show(installed);

            var a = PackOrder.Merge(saved, installed);
            var b = RefMerge(saved, installed);
            Same("Merge", Show(b), Show(a), input);
            Same("Visible", Show(RefVisible(a, set)), Show(PackOrder.Visible(a, set)), input);
            Same("ApplyOrder", Show(RefApplyOrder(a, set)), Show(PackOrder.ApplyOrder(a, set)), input);

            // A random walk of moves, applied to both.
            for (int step = 0; step < 8 && a.Count > 0; step++)
            {
                string id = rng.Next(8) == 0 ? "missing" : a[rng.Next(a.Count)].Id;
                int delta = rng.Next(3) - 1;
                bool movedA = PackOrder.Move(a, id, delta, set), movedB = RefMove(b, id, delta, set);
                string at = input + " move <" + id + "> " + delta;
                Same("Move result", movedB, movedA, at);
                Same("Move order", Show(b), Show(a), at);
            }
            Same("round trip after moves", Show(a), Show(PackOrder.Parse(PackOrder.Format(a))), Show(a));
        }
    }

    // The whole choice for one picture: packs in the player's order, some switched off, each with or without a
    // file for it, each file usable or not. The reference picks, by brute force, the usable file of the switched-on
    // pack with the best rank.
    static void Resolution()
    {
        for (int i = 0; i < Rounds; i++)
        {
            var order = AnyOrder(6);
            var installed = new HashSet<string>(order.Where(_ => rng.Next(5) > 0).Select(p => p.Id));
            var has = order.ToDictionary(p => p.Id, _ => rng.Next(3) > 0);
            var usable = order.ToDictionary(p => p.Id, _ => rng.Next(3) > 0);
            string input = Show(order) + " installed " + Show(installed) + " files " +
                string.Join(",", order.Select(p => p.Id + (has[p.Id] ? usable[p.Id] ? ":ok" : ":bad" : ":none")).ToArray());

            // As the mod does it: apply order (lowest first), reversed to top first, files only, then Choose.
            var topFirst = PackOrder.ApplyOrder(order, installed);
            topFirst.Reverse();
            var r = PackResolve.Choose(topFirst.Where(id => has[id]), id => usable[id]);

            var eligible = order.Select((p, rank) => new { p.Id, p.Enabled, rank })
                .Where(x => x.Enabled && installed.Contains(x.Id) && has[x.Id]).ToList();
            var good = eligible.Where(x => usable[x.Id]).OrderBy(x => x.rank).ToList();
            Same("winner", good.Count > 0 ? good[0].Id : "(none)", r.HasWinner ? r.Winner : "(none)", input);
            Same("covered", Show(good.Skip(1).Select(x => x.Id)), Show(r.Covered), input);
            Same("rejected", Show(eligible.Where(x => !usable[x.Id]).OrderBy(x => x.rank).Select(x => x.Id)), Show(r.Rejected), input);
        }
    }

    // The two cases that started this file.
    static void Regressions()
    {
        var dash = new List<PackState> { new PackState("-foo", true), new PackState("bar", false) };
        Same("an id starting with '-' keeps its state", Show(dash), Show(PackOrder.Parse(PackOrder.Format(dash))), Show(dash));
        var r = PackResolve.Choose(new[] { "top_wrong_size", "below_good" }, id => id != "top_wrong_size");
        Same("a broken file high up does not hide a good one below", "below_good", r.Winner, "top_wrong_size, below_good");
        Same("the broken file is rejected, not covered", "[<top_wrong_size>]", Show(r.Rejected), "");
        Same("folder names in other scripts give valid ids", true, PackIds.IsValid(PackIds.FromName("我的材质包")), "我的材质包");
        Same("a leading dot is dropped", "hidden_pack", PackIds.FromName(".hidden_pack"), ".hidden_pack");
    }

    // ------------------------------------------------------------------ identity: Parse against a regex model

    // A readable form of an identity, so two can be compared as strings.
    static string Repr(AssetId id)
    {
        if (id == null) return "(none)";
        Locator l = id.Locator;
        return (id.Origin.IsGame ? "game" : id.Origin.Kind + ":" + id.Origin.Token) + "|" + l.Kind + "|" + l.Path + "|" + l.Name + "|" + l.Width + "x" + l.Height;
    }

    static readonly Regex SceneRx = new Regex(@"^(.+)\.([0-9]{1,5})x([0-9]{1,5})$");
    static readonly Regex GameRx = new Regex(@"^([^@~]+(?:~[^@~]+)*)(?:@([^@]+))?$");
    static readonly Regex ModRx = new Regex(@"^([^+]*)\+([^~]+)~(.+)$", RegexOptions.Singleline);
    static readonly Regex UuidRx = new Regex(@"^[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$");
    static readonly Regex ContainerRx = new Regex(@"^[A-Za-z0-9_-]+$");

    // The spellings as they are documented, read by regular expressions instead of the scan the mod uses. The game's
    // names are the ones the old PackKey read, now that a name with '+' is never a game picture.
    static string RefParse(string name)
    {
        if (name.Length == 0) return "(none)";
        if (name.IndexOf('+') >= 0)
        {
            var m = ModRx.Match(name);
            if (!m.Success || !ContainerRx.IsMatch(m.Groups[2].Value)) return "(none)";
            string token = m.Groups[1].Value, origin;
            if (UuidRx.IsMatch(token)) origin = "ModUuid:" + token.ToLowerInvariant();
            else if (RefIsValid(token)) origin = "ModId:" + token;
            else return "(none)";
            return origin + "|ModAsset|" + m.Groups[2].Value + "|" + m.Groups[3].Value + "|0x0";
        }
        var s = SceneRx.Match(name);
        if (s.Success)
        {
            int w = int.Parse(s.Groups[2].Value), h = int.Parse(s.Groups[3].Value);
            if (w > 0 && h > 0) return "game|SceneTexture|" + s.Groups[1].Value + "||" + w + "x" + h;
        }
        if (name.IndexOf('~') < 0) return "(none)";
        var g = GameRx.Match(name);
        if (!g.Success) return "(none)";
        string path = g.Groups[1].Value.Replace('~', '/');
        return g.Groups[2].Success ? "game|AtlasSprite|" + path + "|" + g.Groups[2].Value + "|0x0" : "game|SingleSprite|" + path + "||0x0";
    }

    const string Alphabet = "ab_-.xZ09~@+ ";

    static string AnyName(int max)
    {
        int n = rng.Next(max + 1);
        var c = new char[n];
        for (int i = 0; i < n; i++) c[i] = Alphabet[rng.Next(Alphabet.Length)];
        return new string(c);
    }

    static string AnyToken()
    {
        switch (rng.Next(4))
        {
            case 0: return Guid.NewGuid().ToString("D");
            case 1: return Guid.NewGuid().ToString("D").ToUpperInvariant();
            case 2: return PackIds.FromName(AnyName(8));
            default: return AnyName(8);
        }
    }

    // Raw strings, and strings shaped like each documented spelling with one part spoiled now and then.
    static string AnyKeyName()
    {
        switch (rng.Next(5))
        {
            case 0: return AnyName(14);
            case 1: return AnyToken() + "+" + AnyName(4) + "~" + AnyName(8);
            case 2: return AnyName(5) + "." + rng.Next(0, 1200) + "x" + rng.Next(0, 1200);
            case 3: return AnyName(4) + "~" + AnyName(4) + (rng.Next(2) == 0 ? "@" + AnyName(5) : "");
            default: return AnyToken() + "+" + (rng.Next(2) == 0 ? "Items" : "Paper") + "~" + AnyName(8);
        }
    }

    static AssetId AnyId()
    {
        switch (rng.Next(5))
        {
            case 0: return AssetId.Of(Origin.Game, Locator.Atlas(AnyName(6), AnyName(4)));
            case 1: return AssetId.Of(Origin.Game, Locator.Single(AnyName(6)));
            case 2: return AssetId.Of(Origin.Game, Locator.Scene(AnyName(6), rng.Next(-1, 700), rng.Next(-1, 700)));
            case 3: return AssetId.Of(Origin.FromModId(AnyToken()), Locator.ModAsset(AnyName(3), AnyName(6)));
            default:
                Guid u;
                return Guid.TryParse(AnyToken(), out u) ? AssetId.Of(Origin.FromUuid(u), Locator.ModAsset(rng.Next(2) == 0 ? "Items" : "Paper", AnyName(6))) : null;
        }
    }

    static void Identity()
    {
        for (int i = 0; i < Rounds; i++)
        {
            string name = AnyKeyName();
            AssetId id = AssetIdCodec.Parse(name);
            Same("Parse", RefParse(name), Repr(id), "<" + name + ">");
            if (id == null) continue;
            // What Parse reads, Format writes in a form that reads back as the same identity.
            string spelled = AssetIdCodec.Format(id);
            Same("a parsed identity has a name", true, spelled != null, "<" + name + ">");
            if (spelled != null) Same("Parse(Format(x)) == x", Repr(id), Repr(AssetIdCodec.Parse(spelled)), "<" + name + "> -> <" + spelled + ">");
        }

        // Two different identities never share a file name; an identity that is written reads back as itself.
        var written = new Dictionary<string, string>();
        for (int i = 0; i < Rounds * 2; i++)
        {
            AssetId id = AnyId();
            if (id == null) continue;
            string name = AssetIdCodec.Format(id);
            if (name == null) continue;
            Same("Parse(Format(x)) == x for a built identity", Repr(id), Repr(AssetIdCodec.Parse(name)), "<" + name + ">");
            string seen;
            if (written.TryGetValue(name, out seen)) Same("one name, one identity", seen, Repr(id), "<" + name + ">");
            else written[name] = Repr(id);
        }
    }

    // The origin of a mod from its marker pictures and its declaration, against a plain model.
    static void OriginChoice()
    {
        for (int i = 0; i < Rounds; i++)
        {
            var uuids = new List<Guid>();
            for (int k = rng.Next(4); k > 0; k--) uuids.Add(rng.Next(3) == 0 && uuids.Count > 0 ? uuids[0] : Guid.NewGuid());
            var extra = new[] { "icon", "origin.", "wall.1x1" }.Where(_ => rng.Next(2) == 0);
            var keys = uuids.Select(u => OriginMarker.KeyFor(u)).Concat(extra).OrderBy(_ => rng.Next()).ToList();
            Guid? declared = rng.Next(3) == 0 ? (Guid?)Guid.NewGuid() : rng.Next(4) == 0 && uuids.Count > 0 ? (Guid?)uuids[0] : null;
            string modId = rng.Next(10) == 0 ? "not valid" : "my_mod";
            var info = OriginResolver.Resolve(modId, keys, declared);

            var distinct = uuids.Distinct().ToList();
            string expectedOrigin, expectedProblem;
            if (declared.HasValue)
            {
                expectedOrigin = "ModUuid:" + declared.Value.ToString("D");
                expectedProblem = distinct.Count == 0 || (distinct.Count == 1 && distinct[0] == declared.Value) ? "no" : "yes";
            }
            else if (distinct.Count == 1) { expectedOrigin = "ModUuid:" + distinct[0].ToString("D"); expectedProblem = "no"; }
            else { expectedOrigin = modId == "my_mod" ? "ModId:my_mod" : "(none)"; expectedProblem = distinct.Count > 1 ? "yes" : "no"; }
            string input = modId + " keys " + Show(keys) + " declared " + (declared.HasValue ? declared.Value.ToString("D") : "-");
            Same("origin", expectedOrigin, info.Origin == null ? "(none)" : info.Origin.Kind + ":" + info.Origin.Token, input);
            Same("problem", expectedProblem, info.Problem == null ? "no" : "yes", input);
        }
    }

    // ------------------------------------------------------------------ Swapper: what the dictionaries hold, always

    static Dictionary<string, string> Fresh()
    {
        var d = new Dictionary<string, string>();
        for (int i = 0; i < 5; i++) if (rng.Next(4) > 0) d["n" + i] = "o" + rng.Next(1000);
        return d;
    }

    static string Dump(Dictionary<string, string> d)
    {
        return Show(d.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value));
    }

    // Random runs of replace, restore, suspend and resume over a few dictionaries; a key always stands for one entry
    // (as ModSprites builds it from mod, container and name). After every step each entry holds what the rules say,
    // and after RestoreAll every dictionary is as it was at the start.
    static void Swapping()
    {
        for (int round = 0; round < Rounds / 4; round++)
        {
            var dicts = new[] { Fresh(), Fresh(), Fresh() };
            var start = dicts.Select(d => new Dictionary<string, string>(d)).ToArray();
            var swapper = new Swapper<string>();
            var latest = new Dictionary<string, string>();     // key -> the replacement it holds now
            var suspended = new Dictionary<string, string>();  // key -> the replacement to resume
            for (int step = 0; step < 14; step++)
            {
                int d = rng.Next(dicts.Length);
                string name = "n" + rng.Next(5), key = "k" + d + name;
                string input = "round " + round + " step " + step + " key " + key;
                switch (rng.Next(4))
                {
                    case 0:
                    {
                        string repl = "r" + rng.Next(1000);
                        bool had = start[d].ContainsKey(name);
                        bool ok = swapper.Replace(key, dicts[d], name, repl);
                        Same("Replace succeeds exactly when the entry exists", had, ok, input);
                        if (ok)
                        {
                            Same("Replace writes the replacement", repl, dicts[d][name], input);
                            latest[key] = repl;
                            suspended.Remove(key);
                        }
                        break;
                    }
                    case 1:
                        swapper.Restore(key);
                        latest.Remove(key);
                        suspended.Remove(key);
                        Same("Restore puts the original back", start[d].ContainsKey(name) ? start[d][name] : null,
                            dicts[d].ContainsKey(name) ? dicts[d][name] : null, input);
                        break;
                    case 2:
                    {
                        string repl = swapper.Suspend(key);
                        string expected;
                        Same("Suspend returns the replacement exactly when the key is held", latest.TryGetValue(key, out expected), repl != null, input);
                        if (repl != null)
                        {
                            Same("Suspend returns the latest replacement", expected, repl, input);
                            Same("Suspend shows the original", start[d][name], dicts[d][name], input);
                            suspended[key] = repl;
                        }
                        break;
                    }
                    default:
                    {
                        string repl;
                        if (suspended.TryGetValue(key, out repl))
                        {
                            swapper.Resume(key, repl);
                            Same("Resume writes the replacement again", repl, dicts[d][name], input);
                            suspended.Remove(key);
                        }
                        break;
                    }
                }
                Same("the number held is the number of keys with a replacement", latest.Count, swapper.Count, input);
            }
            foreach (var kv in suspended) swapper.Resume(kv.Key, kv.Value);
            swapper.RestoreAll();
            Same("nothing is held after RestoreAll", 0, swapper.Count, "round " + round);
            for (int i = 0; i < dicts.Length; i++)
                Same("RestoreAll leaves each dictionary as it was", Dump(start[i]), Dump(dicts[i]), "round " + round + " dict " + i);
        }

        // Replacing twice under one key keeps the first original; a value someone else wrote in the meantime survives Restore.
        var twice = new Dictionary<string, string> { { "a", "orig" } };
        var s = new Swapper<string>();
        s.Replace("k", twice, "a", "first");
        s.Replace("k", twice, "a", "second");
        s.Restore("k");
        Same("replacing twice keeps the first original", "orig", twice["a"], "");
        var theirs = new Dictionary<string, string> { { "a", "orig" } };
        s.Replace("k", theirs, "a", "mine");
        theirs["a"] = "reloaded";
        s.Restore("k");
        Same("Restore leaves a newer value alone", "reloaded", theirs["a"], "");
        s.Replace("k", theirs, "a", "mine");
        var back = s.Suspend("k");
        s.Restore("k");
        s.Resume("k", back);
        Same("Resume after Restore writes nothing", "reloaded", theirs["a"], "");   // the entry held "reloaded" when it was replaced again
    }

    static int Main()
    {
        Regressions();
        Identity();
        OriginChoice();
        Swapping();
        Ids();
        Codec();
        Operations();
        Resolution();
        Console.WriteLine(failed == 0 ? checks + " checks passed" : failed + " of " + checks + " checks failed");
        return failed == 0 ? 0 : 1;
    }
}
