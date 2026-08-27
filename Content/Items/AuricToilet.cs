using CalamityEntropy.Content.Rarities;
using CalamityEntropy.Content.Tiles;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items
{
    public class AuricToilet : ModItem
    {
        public override void SetDefaults()
        {
            Item.width = 12;
            Item.height = 30;
            Item.maxStack = 9999;
            Item.useTurn = true;
            Item.autoReuse = true;
            Item.useAnimation = 15;
            Item.useTime = 10;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.consumable = true;
            Item.createTile = ModContent.TileType<AToilet>();
            Item.rare = ModContent.RarityType<Golden>();
        }

        public override void AddRecipes()
        {
            // 三把灾厄主题椅换为原版奇珍椅，保持“三椅合一”的配方趣味；门槛由虚空锭把关
            CreateRecipe().
                AddIngredient(ItemID.GoldenChair).
                AddIngredient(ItemID.LihzahrdChair).
                AddIngredient(ItemID.MartianHoverChair).
                AddIngredient<VoidBar>(5).
                AddTile(TileID.LunarCraftingStation).
                Register();
        }
    }
}
