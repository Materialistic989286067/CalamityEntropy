using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Armor.Starmoon
{
    [AutoloadEquip(EquipType.Body)]
    public class StarmoonBreastplate : ModItem
    {
        public override void SetDefaults() {
            Item.width = 34;
            Item.height = 22;
            Item.value = Item.sellPrice(gold: 20);
            Item.rare = ItemRarityID.Cyan;
            Item.defense = 34;
        }

        public override void UpdateEquip(Player player) {
            player.statLifeMax2 += 40;
            player.GetDamage(DamageClass.Generic) += 0.12f;
        }

        public override void AddRecipes() {
            foreach (int fragment in new int[] { ItemID.FragmentVortex, ItemID.FragmentNebula }) {
                CreateRecipe()
                    .AddIngredient<WraithSoulEssence>(26)
                    .AddIngredient(fragment, 10)
                    .AddTile(TileID.LunarCraftingStation)
                    .Register();
            }
        }
    }
}
