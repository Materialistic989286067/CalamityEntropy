using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Armor.Sunblaze
{
    [AutoloadEquip(EquipType.Body)]
    public class SunblazeBreastplate : ModItem
    {
        public override void SetDefaults() {
            Item.width = 34;
            Item.height = 24;
            Item.value = Item.sellPrice(gold: 20);
            Item.rare = ItemRarityID.Cyan;
            Item.defense = 36;
        }

        public override void UpdateEquip(Player player) {
            player.statLifeMax2 += 40;
            Lighting.AddLight(player.Center, 0.95f, 0.7f, 0.35f);
        }

        public override void AddRecipes() {
            foreach (int fragment in new int[] { ItemID.FragmentSolar, ItemID.FragmentStardust }) {
                CreateRecipe()
                    .AddIngredient<BlazingSunlight>(26)
                    .AddIngredient(fragment, 10)
                    .AddTile(TileID.LunarCraftingStation)
                    .Register();
            }
        }
    }
}
