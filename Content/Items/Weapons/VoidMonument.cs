using CalamityEntropy.Content.Projectiles.monument;
using CalamityEntropy.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Weapons
{
    public class VoidMonument : ModItem
    {
        public override void SetDefaults()
        {
            Item.damage = 580;
            Item.DamageType = DamageClass.Melee;
            Item.width = 100;
            Item.noUseGraphic = true;
            Item.height = 100;
            Item.useTime = 16;
            Item.useAnimation = 2;
            Item.useStyle = ItemUseStyleID.Shoot;
            Item.knockBack = 6;
            Item.value = 12000;
            Item.rare = ItemRarityID.Gray;
            Item.Entropy().stroke = true;
            Item.Entropy().strokeColor = new Color(20, 26, 92);
            Item.Entropy().tooltipStyle = 4;
            Item.UseSound = null;
            Item.channel = true;
            Item.noMelee = true;
            Item.shoot = ModContent.ProjectileType<VoidMonumentProj>();
            Item.shootSpeed = 1f;
            Item.Entropy().NameColor = new Color(60, 80, 140);
            Item.Entropy().HasCustomStrokeColor = true;
            Item.Entropy().HasCustomNameColor = true;
        }
        public override bool CanUseItem(Player player)
        {
            return player.ownedProjectileCounts[ModContent.ProjectileType<VoidMonumentProj>()] < 1;
        }
        public override void AddRecipes()
        {
            CreateRecipe().
                AddIngredient(ItemID.SoulofNight, 10).
                AddIngredient(ItemID.CrystalShard, 15).
                AddIngredient(ItemID.Obsidian, 45).
                AddTile(ModContent.TileType<VoidWellTile>()).
                Register();
        }

        public override bool MeleePrefix()
        {
            return true;
        }
    }
}