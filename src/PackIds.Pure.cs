using System.Text;

namespace TexturePacks
{
    // Mod ids as the game accepts them (ModLoader.IsValidModId): 1 to 64 characters, ASCII letters, digits, '_', '-'
    // and '.', not starting with '.'. Shared by the mod and the pack builder, so a builder never writes an id the
    // game refuses.
    public static class PackIds
    {
        public const int MaxLength = 64;
        public const string Fallback = "my_texture_pack";

        public static bool IsAllowedChar(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.';
        }

        public static bool IsValid(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > MaxLength || id[0] == '.') return false;
            foreach (char c in id) if (!IsAllowedChar(c)) return false;
            return true;
        }

        // A valid id made from any name: lower case, every other character turned into '_', leading dots dropped,
        // cut to the maximum length. A name with nothing usable gives the fallback.
        public static string FromName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in (name ?? "").ToLowerInvariant())
            {
                if (sb.Length == 0 && c == '.') continue;
                sb.Append(IsAllowedChar(c) ? c : '_');
                if (sb.Length == MaxLength) break;
            }
            string id = sb.ToString();
            return IsValid(id) && id.Trim('_').Length > 0 ? id : Fallback;
        }
    }
}
