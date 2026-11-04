namespace TexturePacks
{
    // The name of a texture in a pack's Textures folder says what it replaces. The game only reads the top of that
    // folder, so everything is spelled in the file name:
    //   Items~items_tool@wrench.png       sprite "wrench" in the Resources atlas Items/items_tool ("~" for "/")
    //   Backgrounds~space_nebula.png      the single Resources sprite at Backgrounds/space_nebula
    //   store_mid.960x540.png             the whole scene texture "store_mid" of that size
    // Scene textures carry their size because several share a name ("wall" is 960x540 and 640x360).
    public sealed class PackKey
    {
        public const char PathSeparator = '~';
        public const char SpriteSeparator = '@';
        public const char SizeSeparator = '.';

        public readonly string Path;    // Resources path with "/", or the scene texture's name
        public readonly string Sprite;  // sprite name inside a Resources atlas; null otherwise
        public readonly int Width, Height;  // a scene texture's size; 0 for Resources keys

        PackKey(string path, string sprite, int width, int height) { Path = path; Sprite = sprite; Width = width; Height = height; }

        public bool IsAtlas { get { return Sprite != null; } }
        public bool IsSceneTexture { get { return Width > 0; } }

        // Null when the name is not a key: a pack may hold other textures, and those stay the game's.
        public static PackKey Parse(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            int w, h;
            int dot = name.LastIndexOf(SizeSeparator);
            if (dot > 0 && ParseSize(name.Substring(dot + 1), out w, out h))
                return new PackKey(name.Substring(0, dot), null, w, h);

            if (name.IndexOf(PathSeparator) < 0) return null;
            string path = name, sprite = null;
            int at = name.IndexOf(SpriteSeparator);
            if (at >= 0)
            {
                path = name.Substring(0, at);
                sprite = name.Substring(at + 1);
                if (sprite.Length == 0 || sprite.IndexOf(SpriteSeparator) >= 0) return null;
            }
            foreach (string part in path.Split(PathSeparator))
                if (part.Length == 0) return null;
            return new PackKey(path.Replace(PathSeparator, '/'), sprite, 0, 0);
        }

        // "960x540": two positive whole numbers.
        static bool ParseSize(string s, out int w, out int h)
        {
            w = h = 0;
            int x = s.IndexOf('x');
            if (x <= 0 || x == s.Length - 1) return false;
            return ParseInt(s.Substring(0, x), out w) && ParseInt(s.Substring(x + 1), out h) && w > 0 && h > 0;
        }

        static bool ParseInt(string s, out int v)
        {
            v = 0;
            if (s.Length == 0 || s.Length > 5) return false;
            foreach (char c in s)
            {
                if (c < '0' || c > '9') return false;
                v = v * 10 + (c - '0');
            }
            return true;
        }

        public static string SceneId(string texture, int width, int height)
        {
            return texture + SizeSeparator + width + "x" + height;
        }

        public string Id
        {
            get { return IsSceneTexture ? SceneId(Path, Width, Height) : IsAtlas ? Path + "@" + Sprite : Path; }
        }

        public override string ToString() { return Id; }
    }
}
