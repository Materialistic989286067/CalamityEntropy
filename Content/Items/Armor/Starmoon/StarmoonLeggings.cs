using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Armor.Starmoon
{
    [AutoloadEquip(EquipType.Legs)]
    public class StarmoonLeggings : ModItem
    {
        public override void SetDefaults() {
            Item.width = 28;
            Item.height = 16;
            Item.value = Item.sellPrice(gold: 20);
            Item.rare = ItemRarityID.Cyan;
            Item.defense = 25;
        }

        public override void UpdateEquip(Player player) {
            player.moveSpeed += 0.18f;
        }

        public override void AddRecipes() {
            foreach (int fragment in new int[] { ItemID.FragmentVortex, ItemID.FragmentNebula }) {
                CreateRecipe()
                    .AddIngredient<WraithSoulEssence>(18)
                    .AddIngredient(fragment, 6)
                    .AddTile(TileID.LunarCraftingStation)
                    .Register();
            }
        }
    }
}
