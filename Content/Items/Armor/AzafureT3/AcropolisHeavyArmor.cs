using CalamityEntropy.Content.Items.Armor.Azafure;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Armor.AzafureT3
{
    [AutoloadEquip(EquipType.Body)]
    public class AcropolisHeavyArmor : ModItem
    {
        public override void SetDefaults()
        {
            Item.width = 42;
            Item.height = 30;
            Item.value = Item.buyPrice(platinum: 1);
            Item.defense = 24;
            Item.rare = ItemRarityID.Red;
        }

        public override void UpdateEquip(Player player)
        {
            player.GetCritChance(DamageClass.Generic) += 8f;
            player.maxMinions += 1;
        }
        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient<AzafureSteamKnightArmor>()
                .AddIngredient(ItemID.LunarBar, 16)
                .AddIngredient<NihilityFragments>(6)
                .AddTile(TileID.LunarCraftingStation)
                .Register();
        }
    }
}
