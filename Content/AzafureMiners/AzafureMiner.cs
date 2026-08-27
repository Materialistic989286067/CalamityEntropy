using CalamityEntropy.Content.Items;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.AzafureMiners
{
    public class AzafureMiner : ModItem
    {
        public override void SetDefaults()
        {
            Item.width = 92;
            Item.height = 50;
            Item.maxStack = 9999;
            Item.useTurn = true;
            Item.autoReuse = true;
            Item.useAnimation = 6;
            Item.useTime = 6;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.consumable = true;
            Item.createTile = ModContent.TileType<AzafureMinerTile>();
            Item.rare = ItemRarityID.Orange;
            //脱离灾厄:原灾厄RarityOrangeBuyPrice,按rarity-map实值表Orange=5金
            Item.value = Item.buyPrice(gold: 5);
        }

        public override void AddRecipes()
        {
            //脱离灾厄:灾厄EnergyCore/DubiousPlating按material-map换自有阿扎弗电路/镀层
            CreateRecipe().AddIngredient<AzafureCircuitry>()
                .AddIngredient<HellIndustrialComponents>(6)
                .AddIngredient<AzafurePlating>(6)
                .AddRecipeGroup(CERecipeGroups.IronBar, 2)
                .AddTile(TileID.HeavyWorkBench)
                .Register();
        }
    }
}
