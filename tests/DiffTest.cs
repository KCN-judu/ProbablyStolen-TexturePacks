// Differential tests: each pure module (src/*.Pure.cs) against a plain reference model, on many inputs drawn with a
// fixed seed from alphabets full of the characters that sit on the boundaries ('-', '+', ';', '%', '.', non-ASCII).
//   csc -out:DiffTest.exe tests\DiffTest.cs src\PackIds.Pure.cs src\PackOrder.Pure.cs src\PackResolve.Pure.cs
using System;
using System.Collections.Generic;
using System.Linq;
using TexturePacks;

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

    static int Main()
    {
        Regressions();
        Ids();
        Codec();
        Operations();
        Resolution();
        Console.WriteLine(failed == 0 ? checks + " checks passed" : failed + " of " + checks + " checks failed");
        return failed == 0 ? 0 : 1;
    }
}
