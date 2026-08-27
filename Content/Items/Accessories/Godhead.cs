using CalamityEntropy.Common;
using CalamityEntropy.Content.Rarities;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Accessories
{
    public class Godhead : ModItem
    {

        public override void SetDefaults()
        {
            Item.width = 22;
            Item.height = 22;
            Item.value = Item.buyPrice(platinum: 1, gold: 50);
            Item.rare = ModContent.RarityType<NihilityBlue>();
            Item.accessory = true;

        }

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            player.Entropy().GodHeadVisual = !hideVisual;
            player.GetModPlayer<EModPlayer>().Godhead = true;
        }

        public override void AddRecipes()
        {
            // 脱离灾厄:原 Bloodstone×5 换虚无碎片并与原有 3 枚合并
            CreateRecipe().
                AddIngredient(ModContent.ItemType<NihilityFragments>(), 8).
                AddIngredient(ItemID.Ectoplasm, 3).
                AddTile(TileID.LunarCraftingStation).
                Register();
        }
    }
}
