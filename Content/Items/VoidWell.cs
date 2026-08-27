using CalamityEntropy.Content.Rarities;
using CalamityEntropy.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items
{
    public class VoidWell : ModItem
    {
        public override void SetDefaults()
        {
            Item.width = 62;
            Item.height = 48;
            Item.maxStack = 9999;
            Item.useTurn = true;
            Item.autoReuse = true;
            Item.useAnimation = 12;
            Item.useTime = 12;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.consumable = true;
            Item.createTile = ModContent.TileType<VoidWellTile>();
            Item.rare = ModContent.RarityType<VoidPurple>();
        }

        public override void AddRecipes()
        {
            // 解除对灾厄 VoidCondenser 的循环依赖：虚空之鳞出自巡游者袋，保持虚空井为终局站台
            CreateRecipe()
                .AddIngredient(ModContent.ItemType<VoidScales>(), 10)
                .AddIngredient(ItemID.LunarBar, 8)
                .AddIngredient(ItemID.FragmentVortex, 6)
                .AddTile(TileID.LunarCraftingStation)
                .Register();
        }
    }
}
