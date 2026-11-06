using System;
using System.Collections.Generic;

namespace TexturePacks
{
    // The middle of the loader. Packs.Rebuild runs in three steps, and this file is the one between the other two:
    //
    //   read    Packs reads the game into plain inputs: the switched-on packs, top first, with their files
    //           (PackInput), and a lookup of what the game has under an identity (Target);
    //   plan    Planner.Make decides, picture by picture, which file is used and why each other one is not. The
    //           result, a Plan, is a value: it holds the decision and nothing of how it is put in place;
    //   apply   Packs puts the plan in place. It keeps the choices that are in place now, and PlanDiff gives what to
    //           take out and what to put in to get from those to the new plan. A choice that has not changed is not
    //           touched, so applying the same plan again does nothing, and switching one pack off only touches the
    //           pictures that pack had.
    //
    // A pack's numbers in the manager (covered, skipped) and the warnings in Player.log are read from the Plan too.
    //
    // Generic over the file (TFile, a Texture2D in the game) and over the game's object behind an identity (THandle:
    // the original sprite, or another mod's picture), so tests/PureTest.cs and tests/DiffTest.cs run it with strings.

    // Why a pack's file is not used. None when it is.
    public enum Problem : byte
    {
        None,
        NoSuchPicture,      // the game has no picture with this name
        OriginNotEnabled,   // no enabled mod has the origin the name gives
        NoSuchModPicture,   // the mod has no picture with this name
        WrongSize,          // the picture exists with another size
    }

    // What the game has under one identity: its object and size, or why there is none.
    public struct Target<THandle>
    {
        public THandle Handle;
        public Problem Missing;     // None when the picture exists
        public int Width, Height;

        public bool Found { get { return Missing == Problem.None; } }

        public static Target<THandle> Of(THandle handle, int width, int height)
        {
            return new Target<THandle> { Handle = handle, Missing = Problem.None, Width = width, Height = height };
        }

        public static Target<THandle> None(Problem why)
        {
            return new Target<THandle> { Missing = why };
        }
    }

    // One file of a pack: what it replaces and its size.
    public sealed class PackFile<TFile>
    {
        public readonly AssetId Id;
        public readonly TFile File;
        public readonly int Width, Height;

        public PackFile(AssetId id, TFile file, int width, int height) { Id = id; File = file; Width = width; Height = height; }
    }

    // One switched-on pack and its files.
    public sealed class PackInput<TFile>
    {
        public readonly string Id;
        public readonly IList<PackFile<TFile>> Files;

        public PackInput(string id, IList<PackFile<TFile>> files) { Id = id; Files = files; }
    }

    // The file used for one picture: from which pack, and the game's object it goes on. Immutable.
    public sealed class Choice<TFile, THandle>
    {
        public readonly AssetId Id;
        public readonly string Pack;
        public readonly TFile File;
        public readonly THandle Handle;

        public Choice(AssetId id, string pack, TFile file, THandle handle) { Id = id; Pack = pack; File = file; Handle = handle; }

        // The same picture from the same file of the same pack, on the same object of the game.
        public bool Same(Choice<TFile, THandle> other, Func<THandle, THandle, bool> sameHandle)
        {
            return other != null && Id.Equals(other.Id) && Pack == other.Pack &&
                EqualityComparer<TFile>.Default.Equals(File, other.File) && sameHandle(Handle, other.Handle);
        }
    }

    // A file that is not used, and why.
    public sealed class Rejection
    {
        public readonly string Pack;
        public readonly AssetId Id;
        public readonly Problem Why;
        public readonly int Width, Height;              // the file's size
        public readonly int TargetWidth, TargetHeight;  // the picture's size, for WrongSize

        public Rejection(string pack, AssetId id, Problem why, int width, int height, int targetWidth, int targetHeight)
        {
            Pack = pack; Id = id; Why = why; Width = width; Height = height; TargetWidth = targetWidth; TargetHeight = targetHeight;
        }

        // What Player.log says after "<pack>: <file> skipped, ".
        public string Describe()
        {
            switch (Why)
            {
                case Problem.NoSuchPicture: return "no such sprite in the game";
                case Problem.OriginNotEnabled: return "no enabled mod has the origin " + Id.Origin;
                case Problem.NoSuchModPicture: return "the mod has no picture " + Id.Locator.Path + "/" + Id.Locator.Name;
                case Problem.WrongSize:
                    return "is " + Width + "x" + Height + ", the " + (Id.Origin.IsMod ? "mod's" : "game's") + " picture is " +
                        TargetWidth + "x" + TargetHeight;
                default: return Why.ToString();
            }
        }
    }

    // A pack's share of a plan.
    public sealed class PackCount
    {
        public int Chosen;      // its file is used
        public int Covered;     // usable, but a pack higher in the list is used
        public int Rejected;    // not usable (Rejection)
    }

    public sealed class Plan<TFile, THandle>
    {
        public readonly Dictionary<AssetId, Choice<TFile, THandle>> Chosen = new Dictionary<AssetId, Choice<TFile, THandle>>();
        public readonly List<Rejection> Rejected = new List<Rejection>();
        // Every switched-on pack, with or without files.
        public readonly Dictionary<string, PackCount> Counts = new Dictionary<string, PackCount>();
        // The mods the switched-on packs have files for, used or not: when one of them finishes loading, the packs
        // must be planned again (its pictures exist from then on).
        public readonly HashSet<Origin> Origins = new HashSet<Origin>();

        public PackCount CountOf(string pack)
        {
            PackCount c;
            return Counts.TryGetValue(pack, out c) ? c : new PackCount();
        }
    }

    public static class Planner
    {
        sealed class Candidate<TFile> { public string Pack; public PackFile<TFile> File; }

        // The decision for every picture any switched-on pack has a file for. `target` is asked once per picture.
        // Per picture: the files of the packs in order, top first (and in a pack's own order when it has two for one
        // picture); a file that does not fit the picture is rejected before priority is decided (PackResolve).
        public static Plan<TFile, THandle> Make<TFile, THandle>(IList<PackInput<TFile>> topFirst, Func<AssetId, Target<THandle>> target)
        {
            var plan = new Plan<TFile, THandle>();
            var order = new List<AssetId>();
            var candidates = new Dictionary<AssetId, List<Candidate<TFile>>>();
            foreach (var pack in topFirst)
            {
                if (!plan.Counts.ContainsKey(pack.Id)) plan.Counts[pack.Id] = new PackCount();
                foreach (var f in pack.Files)
                {
                    List<Candidate<TFile>> list;
                    if (!candidates.TryGetValue(f.Id, out list)) { candidates[f.Id] = list = new List<Candidate<TFile>>(); order.Add(f.Id); }
                    list.Add(new Candidate<TFile> { Pack = pack.Id, File = f });
                    if (f.Id.Origin.IsMod) plan.Origins.Add(f.Id.Origin);
                }
            }

            foreach (var id in order)
            {
                var t = target(id);
                var r = PackResolve.Choose(candidates[id], c => t.Found && c.File.Width == t.Width && c.File.Height == t.Height);
                foreach (var c in r.Covered) plan.Counts[c.Pack].Covered++;
                foreach (var c in r.Rejected)
                {
                    plan.Counts[c.Pack].Rejected++;
                    plan.Rejected.Add(new Rejection(c.Pack, id, t.Found ? Problem.WrongSize : t.Missing,
                        c.File.Width, c.File.Height, t.Width, t.Height));
                }
                if (!r.HasWinner) continue;
                plan.Counts[r.Winner.Pack].Chosen++;
                plan.Chosen[id] = new Choice<TFile, THandle>(id, r.Winner.Pack, r.Winner.File.File, t.Handle);
            }
            return plan;
        }
    }

    // What to take out and what to put in, to go from the choices in place now to the next ones. A picture whose
    // choice changed is in both: its old choice is taken out, its new one put in. Take out first, then put in.
    public sealed class PlanDiff<TFile, THandle>
    {
        public readonly List<Choice<TFile, THandle>> Removed = new List<Choice<TFile, THandle>>();
        public readonly List<Choice<TFile, THandle>> Added = new List<Choice<TFile, THandle>>();
        public int Kept;

        public bool Empty { get { return Removed.Count == 0 && Added.Count == 0; } }

        public static PlanDiff<TFile, THandle> Between(IDictionary<AssetId, Choice<TFile, THandle>> now,
            IDictionary<AssetId, Choice<TFile, THandle>> next, Func<THandle, THandle, bool> sameHandle)
        {
            var d = new PlanDiff<TFile, THandle>();
            foreach (var kv in now)
            {
                Choice<TFile, THandle> n;
                if (next.TryGetValue(kv.Key, out n) && kv.Value.Same(n, sameHandle)) d.Kept++;
                else d.Removed.Add(kv.Value);
            }
            foreach (var kv in next)
            {
                Choice<TFile, THandle> o;
                if (!now.TryGetValue(kv.Key, out o) || !o.Same(kv.Value, sameHandle)) d.Added.Add(kv.Value);
            }
            return d;
        }
    }
}
