using System;

namespace TexturePacks
{
    // A pure file (no Unity types): the identity rules are tested headless by tests/PureTest.cs and tests/DiffTest.cs.
    //
    // The identity of a picture a pack can replace is a pair of values, not a string:
    //   Origin   where the picture comes from: the game itself, or one mod;
    //   Locator  where it is inside that origin: a sprite of a Resources atlas, a single Resources sprite, a whole
    //            texture of a scene file (all three only in the game), or an asset of a mod (Items, Paper, ...).
    // Priority, de-duplication and the pack manager's numbers key on AssetId. The string form, which is a pack's
    // file name, exists only at the edge and is owned by AssetIdCodec.

    public enum OriginKind { Game, ModUuid, ModId }

    // Where a picture comes from. Equal by value.
    //   Game     the game itself.
    //   ModUuid  a mod that has declared a UUID: the UUID is the identity, so a renamed mod, or two mods sharing an
    //            id, never mix, and a local copy and its Workshop copy are one origin.
    //   ModId    a mod without a UUID: its mod id stands in for it (works, without that protection).
    public sealed class Origin : IEquatable<Origin>
    {
        public readonly OriginKind Kind;
        public readonly string Token;   // "" for the game; the lower-case hyphenated UUID; the mod id

        Origin(OriginKind kind, string token) { Kind = kind; Token = token; }

        public static readonly Origin Game = new Origin(OriginKind.Game, "");

        public static Origin FromUuid(Guid uuid) { return new Origin(OriginKind.ModUuid, uuid.ToString("D")); }

        // Null when the id is not one the game accepts (PackIds.IsValid), so every Origin can be written in a name.
        public static Origin FromModId(string modId) { return PackIds.IsValid(modId) ? new Origin(OriginKind.ModId, modId) : null; }

        public bool IsGame { get { return Kind == OriginKind.Game; } }
        public bool IsMod { get { return Kind != OriginKind.Game; } }

        public bool Equals(Origin other) { return other != null && other.Kind == Kind && other.Token == Token; }
        public override bool Equals(object obj) { return Equals(obj as Origin); }
        public override int GetHashCode() { return (int)Kind * 397 ^ Token.GetHashCode(); }
        public override string ToString() { return IsGame ? "game" : Token; }
    }

    public enum LocatorKind { AtlasSprite, SingleSprite, SceneTexture, ModAsset }

    // Where a picture is inside its origin. One class with a kind (a closed set of four shapes) so equality and
    // hashing are plain value comparisons; the factories return null for a shape that cannot exist.
    public sealed class Locator : IEquatable<Locator>
    {
        public readonly LocatorKind Kind;
        public readonly string Path;    // Resources path with "/" (atlas, single); the texture's name (scene); the container (mod)
        public readonly string Name;    // sprite name inside the atlas; the asset's name (mod); null otherwise
        public readonly int Width, Height;  // a scene texture's size; 0 otherwise

        Locator(LocatorKind kind, string path, string name, int width, int height)
        { Kind = kind; Path = path; Name = name; Width = width; Height = height; }

        // A sprite in a Resources atlas, such as items/items_tool2 and "blender".
        public static Locator Atlas(string path, string sprite)
        {
            return ValidPath(path) && !string.IsNullOrEmpty(sprite) ? new Locator(LocatorKind.AtlasSprite, path, sprite, 0, 0) : null;
        }

        // A single Resources sprite, such as window/machinery/bevel_frame.
        public static Locator Single(string path)
        {
            return ValidPath(path) ? new Locator(LocatorKind.SingleSprite, path, null, 0, 0) : null;
        }

        // A whole texture of a scene file. Several share a name, so the size is part of the identity.
        public static Locator Scene(string texture, int width, int height)
        {
            return !string.IsNullOrEmpty(texture) && width > 0 && height > 0
                ? new Locator(LocatorKind.SceneTexture, texture, null, width, height) : null;
        }

        // An asset of a mod: Items or Paper (the folders the game decodes), or any container a provider names.
        public static Locator ModAsset(string container, string name)
        {
            return IsContainer(container) && !string.IsNullOrEmpty(name) ? new Locator(LocatorKind.ModAsset, container, name, 0, 0) : null;
        }

        public bool IsAtlas { get { return Kind == LocatorKind.AtlasSprite; } }
        public bool IsSingle { get { return Kind == LocatorKind.SingleSprite; } }
        public bool IsScene { get { return Kind == LocatorKind.SceneTexture; } }
        public bool IsModAsset { get { return Kind == LocatorKind.ModAsset; } }

        // Segments separated by "/", none empty.
        static bool ValidPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            foreach (string part in path.Split('/')) if (part.Length == 0) return false;
            return true;
        }

        // Letters, digits, '_' and '-': a container is one word of the file name, and never holds the separators.
        public static bool IsContainer(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-')) return false;
            return true;
        }

        public bool Equals(Locator o)
        {
            return o != null && o.Kind == Kind && o.Path == Path && o.Name == Name && o.Width == Width && o.Height == Height;
        }
        public override bool Equals(object obj) { return Equals(obj as Locator); }
        public override int GetHashCode()
        {
            int h = (int)Kind;
            h = h * 397 ^ Path.GetHashCode();
            h = h * 397 ^ (Name == null ? 0 : Name.GetHashCode());
            return (h * 397 ^ Width) * 397 ^ Height;
        }
    }

    // The identity of one picture. Only a game locator goes with the game origin and only a mod asset with a mod.
    public sealed class AssetId : IEquatable<AssetId>
    {
        public readonly Origin Origin;
        public readonly Locator Locator;

        AssetId(Origin origin, Locator locator) { Origin = origin; Locator = locator; }

        // Null when the pair does not belong together (the game has no mod assets, a mod has no Resources sprites).
        public static AssetId Of(Origin origin, Locator locator)
        {
            if (origin == null || locator == null) return null;
            return origin.IsGame != locator.IsModAsset ? new AssetId(origin, locator) : null;
        }

        public bool Equals(AssetId o) { return o != null && o.Origin.Equals(Origin) && o.Locator.Equals(Locator); }
        public override bool Equals(object obj) { return Equals(obj as AssetId); }
        public override int GetHashCode() { return Origin.GetHashCode() * 397 ^ Locator.GetHashCode(); }

        // The pack file name; a readable form for an identity that has none.
        public override string ToString() { return AssetIdCodec.Format(this) ?? Origin + ":" + Locator.Path + "/" + Locator.Name; }
    }
}
