// A texture pack has no code of its own. The game only loads a mod that contains an IMod class, so every pack
// ships this empty one; the pictures in the pack's Textures folder are applied by the Texture Packs mod.
// Every pack can carry the same TexturePack.dll: the game loads it once and reuses it for the others.
// Built by build.sh into out/texture_pack_template/TexturePack.dll, copied into the creator kit by tools/kit.sh,
// and into each pack by PackBuilder. IMod and ModManifest are the game's types.
public sealed class TexturePack : IMod
{
    public void Init(ModManifest manifest) { }
    public void OnEnable() { }
    public void OnDisable() { }
}
