using System.Collections.Generic;
using System.Text;

namespace TexturePacks
{
    // The player's order of texture packs. The pack at the top wins: where two packs replace the same picture, the
    // one higher in the list is used. Each pack can be switched off without uninstalling it.
    //
    // Saved as one line, top first, each pack marked "+" (on) or "-" (off):  +hd_items;-old_ui;+dark_shop
    // The mark is always written, so an id may start with "-" or "+" itself; "%" and ";" inside an id are written as
    // %25 and %3B. Lines from before the marks (a bare id is on) still read the same.
    // Packs that are not installed right now stay in the line, so a pack that comes back takes its old place.
    public sealed class PackState
    {
        public readonly string Id;
        public bool Enabled;

        public PackState(string id, bool enabled) { Id = id; Enabled = enabled; }
    }

    public static class PackOrder
    {
        const char Separator = ';', On = '+', Off = '-', Escape = '%';

        public static List<PackState> Parse(string text)
        {
            var list = new List<PackState>();
            var seen = new HashSet<string>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (var part in text.Split(Separator))
            {
                if (part.Length == 0) continue;
                bool enabled = part[0] != Off;
                string id = Unescape(part[0] == On || part[0] == Off ? part.Substring(1) : part);
                if (id.Length > 0 && seen.Add(id)) list.Add(new PackState(id, enabled));
            }
            return list;
        }

        public static string Format(IEnumerable<PackState> packs)
        {
            var sb = new StringBuilder();
            foreach (var p in packs)
            {
                if (string.IsNullOrEmpty(p.Id)) continue;
                if (sb.Length > 0) sb.Append(Separator);
                sb.Append(p.Enabled ? On : Off).Append(EscapeId(p.Id));
            }
            return sb.ToString();
        }

        static string EscapeId(string id)
        {
            return id.Replace("%", "%25").Replace(";", "%3B");
        }

        // %XX with two hex digits is a character; anything else is kept as written.
        static string Unescape(string s)
        {
            if (s.IndexOf(Escape) < 0) return s;
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                int hi, lo;
                if (s[i] == Escape && i + 2 < s.Length && Hex(s[i + 1], out hi) && Hex(s[i + 2], out lo))
                {
                    sb.Append((char)(hi * 16 + lo));
                    i += 2;
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        static bool Hex(char c, out int v)
        {
            v = c >= '0' && c <= '9' ? c - '0' : c >= 'A' && c <= 'F' ? c - 'A' + 10 : c >= 'a' && c <= 'f' ? c - 'a' + 10 : -1;
            return v >= 0;
        }

        // The saved order with the installed packs merged in. A newly installed pack goes on top, switched on; when
        // several are new at once they keep their order in the Mods menu. Saved packs that are not installed stay.
        public static List<PackState> Merge(IList<PackState> saved, IList<string> installed)
        {
            var known = new HashSet<string>();
            foreach (var p in saved) known.Add(p.Id);
            var merged = new List<PackState>();
            foreach (var id in installed)
                if (!known.Contains(id)) { merged.Add(new PackState(id, true)); known.Add(id); }
            foreach (var p in saved) merged.Add(new PackState(p.Id, p.Enabled));
            return merged;
        }

        // Moves a pack one place up (delta -1) or down (+1) among the installed packs; packs that are not installed
        // are stepped over. Returns false when it is already at that end.
        public static bool Move(List<PackState> packs, string id, int delta, ICollection<string> installed)
        {
            int from = IndexOf(packs, id);
            if (from < 0 || delta == 0) return false;
            int to = from;
            do to += delta < 0 ? -1 : 1;
            while (to >= 0 && to < packs.Count && !installed.Contains(packs[to].Id));
            if (to < 0 || to >= packs.Count) return false;
            var p = packs[from];
            packs.RemoveAt(from);
            packs.Insert(to, p);
            return true;
        }

        // The installed packs that are switched on, lowest priority first: applying them in this order lets each
        // pack replace what the ones below it chose.
        public static List<string> ApplyOrder(IList<PackState> packs, ICollection<string> installed)
        {
            var order = new List<string>();
            for (int i = packs.Count - 1; i >= 0; i--)
                if (packs[i].Enabled && installed.Contains(packs[i].Id)) order.Add(packs[i].Id);
            return order;
        }

        // The installed packs in list order, top first: what the manager shows.
        public static List<PackState> Visible(IList<PackState> packs, ICollection<string> installed)
        {
            var list = new List<PackState>();
            foreach (var p in packs) if (installed.Contains(p.Id)) list.Add(p);
            return list;
        }

        static int IndexOf(IList<PackState> packs, string id)
        {
            for (int i = 0; i < packs.Count; i++) if (packs[i].Id == id) return i;
            return -1;
        }
    }
}
