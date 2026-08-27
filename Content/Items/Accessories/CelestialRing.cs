using CalamityEntropy.Content.Rarities;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Accessories
{
    public class CelestialRing : ModItem
    {

        public override void SetDefaults()
        {
            Item.width = 26;
            Item.defense = 15;
            Item.height = 26;
            Item.value = Item.buyPrice(platinum: 1, gold: 50);
            Item.rare = ModContent.RarityType<NihilityBlue>();
            Item.accessory = true;
        }

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            player.GetDamage(DamageClass.Generic) += 0.15f;
            player.GetKnockback(DamageClass.Summon) += 0.75f;
            player.GetCritChance(DamageClass.Generic) += 5;
            player.pickSpeed *= 1.2f;
            player.GetAttackSpeed(DamageClass.Melee) += 0.05f;
            player.Entropy().CRing = true;
            player.lifeRegen += 5;
            player.maxMinions += 2;
        }

        public override void AddRecipes()
        {
            // 脱离灾厄:灾厄暗日戒指改为原版天界石(表外裁定,拜月后同档)
            CreateRecipe().
                AddIngredient(ItemID.CelestialShell).
                AddIngredient(ItemID.CelestialStone).
                AddIngredient(ModContent.ItemType<WraithSoulEssence>(), 4).
                AddIngredient(ModContent.ItemType<VoidBar>(), 5)
                .AddTile(TileID.LunarCraftingStation).
                Register();
        }
    }
}
