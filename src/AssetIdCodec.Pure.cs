namespace TexturePacks
{
    // A pure file (no Unity types): the file-name rules are tested headless by tests/PureTest.cs and tests/DiffTest.cs.
    // The name of a texture in a pack's Textures folder says what it replaces. The game only reads the top of that
    // folder, so everything is spelled in the file name (without ".png"). This class is the only place that knows
    // the spelling of an AssetId; the rest of the mod, and every tool, goes through Parse and Format.
    //
    // A picture of the game:
    //   items~items_tool2@blender         sprite "blender" in the Resources atlas items/items_tool2 ("~" for "/")
    //   window~machinery~bevel_frame      the single Resources sprite at window/machinery/bevel_frame
    //   store_mid.960x540                 the whole scene texture "store_mid" of that size
    // Scene textures carry their size because several share a name ("wall" is 960x540 and 640x360).
    //
    // A picture of a mod, the origin first and then where it is inside the mod:
    //   machine_mod+Items~crusher_body    asset "crusher_body" in the Items folder of the mod with id machine_mod
    //   3f2a9c1e-...-0a1b2c3d4e5f+Paper~note   the same for the mod that declared that UUID (Paper folder)
    // "+" is in no mod id, no UUID and no game name (catalog.tsv has none), so the first "+" always ends the origin and
    // a name with one is never a game picture. The first "~" after it ends the container (letters, digits, '_', '-').
    // Everything after that is the asset's name, as it is in the mod's folder or code.
    //
    // Two different identities never share a file name, and Parse(Format(x)) equals x: Format checks that by
    // parsing what it wrote, and returns null for an identity that has no such name.
    public static class AssetIdCodec
    {
        public const char OriginSeparator = '+';
        public const char PathSeparator = '~';
        public const char SpriteSeparator = '@';
        public const char SizeSeparator = '.';

        // Null when the name is not an identity: a pack may hold other textures (its icon, an origin marker), and
        // those stay out of the way.
        public static AssetId Parse(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            int plus = name.IndexOf(OriginSeparator);
            return plus >= 0 ? ParseMod(name, plus) : ParseGame(name);
        }

        // The string form of an identity, or null when it cannot be written so that Parse reads it back unchanged
        // (a game path with "+" or "~" in a segment, or a sprite name that ends like a size).
        public static string Format(AssetId id)
        {
            if (id == null) return null;
            string text = Spell(id);
            AssetId back = Parse(text);
            return back != null && back.Equals(id) ? text : null;
        }

        // The name a scene texture is looked up by: the game's texture name and its pixel size. SceneTextures asks
        // for it with the name and size of each texture in memory, so it builds no AssetId per texture.
        public static string SceneName(string texture, int width, int height)
        {
            return texture + SizeSeparator + width + "x" + height;
        }

        static string Spell(AssetId id)
        {
            Locator l = id.Locator;
            if (id.Origin.IsMod) return id.Origin.Token + OriginSeparator + l.Path + PathSeparator + l.Name;
            if (l.IsScene) return SceneName(l.Path, l.Width, l.Height);
            string path = l.Path.Replace('/', PathSeparator);
            return l.IsAtlas ? path + SpriteSeparator + l.Name : path;
        }

        static AssetId ParseMod(string name, int plus)
        {
            Origin origin = ParseOrigin(name.Substring(0, plus));
            string rest = name.Substring(plus + 1);
            int tilde = rest.IndexOf(PathSeparator);
            if (origin == null || tilde <= 0) return null;
            return AssetId.Of(origin, Locator.ModAsset(rest.Substring(0, tilde), rest.Substring(tilde + 1)));
        }

        // A UUID (any case, with hyphens) is the identity of a mod that declared one; anything else must be a mod id.
        static Origin ParseOrigin(string token)
        {
            System.Guid uuid;
            if (token.Length == 36 && System.Guid.TryParseExact(token, "D", out uuid)) return Origin.FromUuid(uuid);
            return Origin.FromModId(token);
        }

        static AssetId ParseGame(string name)
        {
            int w, h;
            int dot = name.LastIndexOf(SizeSeparator);
            if (dot > 0 && ParseSize(name.Substring(dot + 1), out w, out h))
                return AssetId.Of(Origin.Game, Locator.Scene(name.Substring(0, dot), w, h));

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
            string resources = path.Replace(PathSeparator, '/');
            return AssetId.Of(Origin.Game, sprite != null ? Locator.Atlas(resources, sprite) : Locator.Single(resources));
        }

        // "960x540": two positive whole numbers of at most five digits (so the int cannot overflow).
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
    }
}
