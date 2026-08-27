using CalamityEntropy.Content.Rarities;
using CalamityEntropy.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Accessories
{
    public class HolyMoonlight : ModItem
    {

        public override void SetDefaults()
        {
            Item.width = 86;
            Item.height = 86;
            Item.value = Item.buyPrice(platinum: 1, gold: 50);
            Item.rare = ModContent.RarityType<VoidPurple>();
            Item.accessory = true;

        }

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            player.statManaMax2 += 50;
            player.GetDamage(DamageClass.Magic) += 0.15f;
            player.Entropy().holyMoonlight = true;
            player.Entropy().visualMagiShield = !hideVisual;
        }

        public override void AddRecipes()
        {
            // 脱离灾厄:三件灾厄饰品原料改为原版同职能饰品(表外裁定,档位由虚空井/虚空锭把关)
            CreateRecipe().
                AddIngredient(ItemID.ObsidianShield, 1).
                AddIngredient(ItemID.ManaFlower, 1).
                AddIngredient(ItemID.FrozenShield, 1).
                AddIngredient(ModContent.ItemType<VoidBar>(), 5).
                AddIngredient(ModContent.ItemType<WraithSoulEssence>(), 4).
                AddTile(ModContent.TileType<VoidWellTile>()).
                Register();
        }
    }
}
