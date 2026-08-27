using CalamityEntropy.Common;
using CalamityEntropy.Content.Items.Armor;
using CalamityEntropy.Content.Rarities;
using System.Collections.Generic;
using Terraria;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Accessories
{
    public class VetrasylsEye : ModItem
    {
        // 脱离灾厄:灾厄 IntegrateHotkey 扩展改自有键名提示
        public override void ModifyTooltips(List<TooltipLine> list) => list.Replace("[KEY]", CEKeybinds.VetrasylsEyeBlockHotKey.TooltipKeyHint());

        public override void SetDefaults()
        {
            Item.width = 52;
            Item.height = 52;
            Item.value = Item.buyPrice(platinum: 1);
            Item.rare = ModContent.RarityType<SkyBlue>();
            Item.accessory = true;
        }

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            player.Entropy().vetrasylsEye = true;
        }

        public override void AddRecipes()
        {
        }
    }
}
