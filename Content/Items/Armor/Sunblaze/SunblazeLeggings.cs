using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Armor.Sunblaze
{
    [AutoloadEquip(EquipType.Legs)]
    public class SunblazeLeggings : ModItem
    {
        public override void SetDefaults() {
            Item.width = 26;
            Item.height = 14;
            Item.value = Item.sellPrice(gold: 20);
            Item.rare = ItemRarityID.Cyan;
            Item.defense = 28;
        }

        public override void UpdateEquip(Player player) {
            player.moveSpeed += 0.18f;
            player.GetDamage(DamageClass.Generic) += 0.06f;
        }

        public override void AddRecipes() {
            foreach (int fragment in new int[] { ItemID.FragmentSolar, ItemID.FragmentStardust }) {
                CreateRecipe()
                    .AddIngredient<BlazingSunlight>(18)
                    .AddIngredient(fragment, 6)
                    .AddTile(TileID.LunarCraftingStation)
                    .Register();
            }
        }
    }
}
