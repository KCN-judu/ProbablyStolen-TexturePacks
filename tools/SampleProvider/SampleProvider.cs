using UnityEngine;

namespace SampleProvider
{
    // A development mod that is a provider (see template/TexturePacksClient.cs): it makes one sprite in code, a 16x16
    // yellow square, and offers it to texture packs. It shows what a mod that makes its own sprites has to do, and
    // checks the whole path in the game, with and without Texture Packs installed:
    //
    //   1. Enable Sample Provider (development) alone: Player.log says "[SampleProvider] showing the yellow square".
    //      Nothing else happens, and nothing fails: with no Texture Packs, the offer is never read.
    //   2. Enable Texture Packs and a pack that holds a 16x16 picture named
    //        4b6f1f6c-2d6e-4a46-9f7e-5c1d2a7b8e90+Code~square.png
    //      (or sample_provider+Code~square.png). Restart the game. Player.log then says
    //        [SampleProvider] showing the pack's sprite
    //      and the Texture Packs summary line counts one picture of another mod. Switching the pack off in the
    //      manager and applying says "showing the yellow square" again.
    //
    // Not part of the released mod; build it with tools/SampleProvider/build.sh.
    public sealed class SampleProviderMod : IMod
    {
        const string ModId = "sample_provider";
        // Declared so that a pack can name this mod by UUID. Pick one for a real mod and keep it for good.
        const string Uuid = "4b6f1f6c-2d6e-4a46-9f7e-5c1d2a7b8e90";
        const string Tag = "[SampleProvider] ";

        static Sprite original;
        static Sprite shown;    // what the mod would draw: the original, or a pack's sprite

        public void Init(ModManifest manifest) { }

        public void OnEnable()
        {
            original = MakeSquare();
            Show(original);
            TexturePacksClient.DeclareOrigin(ModId, Uuid);
            TexturePacksClient.Offer(ModId, "Code", "square", original, Show);
        }

        public void OnDisable()
        {
            TexturePacksClient.Withdraw(ModId);
            if (original != null) Object.Destroy(original.texture);
            original = shown = null;
        }

        // The mod's own drawing code would use `shown`; here the change is only logged.
        static void Show(Sprite sprite)
        {
            shown = sprite;
            Debug.Log(Tag + (sprite == original ? "showing the yellow square" : "showing the pack's sprite"));
        }

        static Sprite MakeSquare()
        {
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var pixels = new Color32[16 * 16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 200, 0, 255);
            texture.SetPixels32(pixels);
            texture.filterMode = FilterMode.Point;
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
