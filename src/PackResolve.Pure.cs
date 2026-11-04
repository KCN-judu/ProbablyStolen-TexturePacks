using System;
using System.Collections.Generic;

namespace TexturePacks
{
    // Which pack's picture is used for one game picture. Every switched-on pack that has a file for it is a
    // candidate, highest priority first. A candidate the game cannot use (no such picture, wrong size) is rejected
    // before priority is decided, so a broken file in a pack high in the list never hides a good one further down.
    // A pure file, tested by tests/PureTest.cs and tests/DiffTest.cs. Generic so the tests need no Unity types.
    public sealed class Resolution<T>
    {
        public bool HasWinner;
        public T Winner;
        public readonly List<T> Covered = new List<T>();    // usable, but a pack higher in the list won
        public readonly List<T> Rejected = new List<T>();   // not usable at all
    }

    public static class PackResolve
    {
        // One pass in priority order: the first usable candidate wins, later usable ones are only counted as covered.
        public static Resolution<T> Choose<T>(IEnumerable<T> candidatesTopFirst, Func<T, bool> usable)
        {
            var r = new Resolution<T>();
            foreach (var c in candidatesTopFirst)
            {
                if (!usable(c)) r.Rejected.Add(c);
                else if (!r.HasWinner) { r.HasWinner = true; r.Winner = c; }
                else r.Covered.Add(c);
            }
            return r;
        }
    }
}
