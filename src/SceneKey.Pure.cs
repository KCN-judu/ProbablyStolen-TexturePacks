using System;

namespace TexturePacks
{
    // How a scene texture is found among the loaded ones: by its name and its size, because several share a name
    // ("wall" is 960x540 and 640x360). The same identity as the file name AssetIdCodec.SceneName spells
    // ("wall.960x540"), without building that string: SceneTextures compares every loaded texture against the packs'
    // files after each scene load, and a struct key costs no allocation per texture. Two keys are equal exactly when
    // their SceneName strings are (tests/DiffTest.cs checks this).
    //
    // A pure file (no Unity types), tested by tests/PureTest.cs and tests/DiffTest.cs.
    public struct SceneKey : IEquatable<SceneKey>
    {
        public readonly string Name;
        public readonly int Width, Height;

        public SceneKey(string name, int width, int height) { Name = name ?? ""; Width = width; Height = height; }

        // The key of a scene locator.
        public static SceneKey Of(Locator scene) { return new SceneKey(scene.Path, scene.Width, scene.Height); }

        // Width and height in one number, to filter loaded textures by size before their name is read (a texture's
        // name is a call into the engine that allocates a string; its size is not).
        public static long Size(int width, int height) { return ((long)width << 32) | (uint)height; }

        public long SizeKey { get { return Size(Width, Height); } }

        public bool Equals(SceneKey o) { return Width == o.Width && Height == o.Height && string.Equals(Name, o.Name, StringComparison.Ordinal); }
        public override bool Equals(object obj) { return obj is SceneKey && Equals((SceneKey)obj); }
        public override int GetHashCode() { return ((Name ?? "").GetHashCode() * 397 ^ Width) * 397 ^ Height; }

        public override string ToString() { return AssetIdCodec.SceneName(Name, Width, Height); }
    }
}
