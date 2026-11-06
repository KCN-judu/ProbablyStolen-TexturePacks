using System.Collections.Generic;

namespace TexturePacks
{
    // A pure file (no Unity types): tested headless by tests/PureTest.cs and tests/DiffTest.cs, with strings for T.
    // Replaces values in dictionaries that other code reads, and puts the originals back. This is the bookkeeping
    // behind ModSprites: the game reads a mod's picture from LoadedMod.Sprites each time it needs it, so a pack's
    // sprite takes effect by replacing the entry, and the entry holds the original again whenever something must see it.
    //
    // Each replacement is held under a key the caller chooses. The rules:
    //   - the original is whatever the entry held the first time the key was replaced, so replacing twice never
    //     mistakes the first replacement for the original;
    //   - Restore and RestoreAll write the original back only if the entry still holds our replacement, so a value
    //     someone else has put there since is left alone;
    //   - Suspend writes the original back for a while and returns the replacement; Resume writes it again, unless
    //     the key was restored in between.
    public sealed class Swapper<T> where T : class
    {
        sealed class Held
        {
            public IDictionary<string, T> Dictionary;
            public string Name;
            public T Original, Replacement;
        }

        readonly Dictionary<string, Held> held = new Dictionary<string, Held>();

        public int Count { get { return held.Count; } }

        public bool IsReplaced(string key) { return held.ContainsKey(key); }

        // Puts `replacement` where the dictionary holds `name`. False, and nothing changed, when there is no entry.
        public bool Replace(string key, IDictionary<string, T> dictionary, string name, T replacement)
        {
            T current;
            if (dictionary == null || replacement == null || !dictionary.TryGetValue(name, out current)) return false;
            Held h;
            T original = held.TryGetValue(key, out h) ? h.Original : current;
            dictionary[name] = replacement;
            held[key] = new Held { Dictionary = dictionary, Name = name, Original = original, Replacement = replacement };
            return true;
        }

        // The value the entry holds when no replacement of ours is in it: the original while it still holds our
        // replacement, otherwise what it holds now (someone may have written a new value since). Null when there is
        // no entry. Packs are planned again without being taken out first, so this is how the original is read.
        public T OriginalIn(string key, IDictionary<string, T> dictionary, string name)
        {
            T now;
            if (dictionary == null || !dictionary.TryGetValue(name, out now)) return null;
            Held h;
            return held.TryGetValue(key, out h) && ReferenceEquals(h.Dictionary, dictionary) && ReferenceEquals(now, h.Replacement)
                ? h.Original : now;
        }

        // Puts the original back and forgets the key.
        public void Restore(string key)
        {
            Held h;
            if (!held.TryGetValue(key, out h)) return;
            held.Remove(key);
            T now;
            if (h.Dictionary.TryGetValue(h.Name, out now) && ReferenceEquals(now, h.Replacement)) h.Dictionary[h.Name] = h.Original;
        }

        public void RestoreAll()
        {
            foreach (var key in new List<string>(held.Keys)) Restore(key);
        }

        // Writes the original back and returns the replacement, to be given to Resume; null when the key holds none.
        public T Suspend(string key)
        {
            Held h;
            if (!held.TryGetValue(key, out h)) return null;
            h.Dictionary[h.Name] = h.Original;
            return h.Replacement;
        }

        // Writes the replacement that Suspend returned, if the key is still held.
        public void Resume(string key, T replacement)
        {
            Held h;
            if (replacement != null && held.TryGetValue(key, out h) && ReferenceEquals(h.Replacement, replacement)) h.Dictionary[h.Name] = replacement;
        }
    }
}
