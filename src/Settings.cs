using System;
using System.Collections.Generic;
using UnityEngine;

namespace TexturePacks
{
    // The player's pack order, kept in the game's own config file for this mod (one text entry, see PackOrder).
    // Having an entry also gives Texture Packs a settings button in the Mods menu, which opens the manager.
    static class Settings
    {
        const string Key = "packs", Section = "Packs";
        static ModConfig config;
        static ConfigEntry<string> order;

        public static void Define(string modId)
        {
            if (order != null) return;
            try
            {
                config = ModHelper.GetConfig(modId);
                order = config.Define(Key, "", Section, () => TexturePacksMod.L("config_packs"), null,
                    () => TexturePacksMod.L("config_packs_tip"), false);
            }
            catch (Exception e) { Debug.LogWarning(TexturePacksMod.Tag + "settings unavailable, every pack stays on: " + e.Message); }
        }

        static string Saved { get { return order != null ? order.Value : ""; } }

        // The saved order with the packs installed now merged in (new ones on top).
        public static List<PackState> Load(IList<string> installed)
        {
            return PackOrder.Merge(PackOrder.Parse(Saved), installed);
        }

        public static void Save(IEnumerable<PackState> packs)
        {
            if (order == null) return;
            try
            {
                order.Set(PackOrder.Format(packs));
                config.Save();
            }
            catch (Exception e) { Debug.LogWarning(TexturePacksMod.Tag + "could not save the pack order: " + e.Message); }
        }

        public static List<string> ApplyOrder(IList<string> installed)
        {
            return PackOrder.ApplyOrder(Load(installed), new HashSet<string>(installed));
        }

        public static bool IsEnabled(string id)
        {
            foreach (var p in PackOrder.Parse(Saved)) if (p.Id == id) return p.Enabled;
            return true;   // not in the saved order yet: a new pack starts switched on
        }
    }
}
