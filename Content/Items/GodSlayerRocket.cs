using CalamityEntropy.Content.Items;
using CalamityEntropy.Content.Projectiles;
using CalamityEntropy.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items
{
    public class GodSlayerRocket : ModItem
    {
        public override void SetStaticDefaults()
        {
            AmmoID.Sets.IsSpecialist[Type] = true;
            AmmoID.Sets.SpecificLauncherAmmoProjectileMatches[ItemID.RocketLauncher].Add(Type, ModContent.ProjectileType<GodSlayerRocketProjectile>());
            AmmoID.Sets.SpecificLauncherAmmoProjectileMatches[ItemID.GrenadeLauncher].Add(Type, ModContent.ProjectileType<GodSlayerRocketProjectile>());
            AmmoID.Sets.SpecificLauncherAmmoProjectileMatches[ItemID.ProximityMineLauncher].Add(Type, ModContent.ProjectileType<GodSlayerRocketProjectile>());
            AmmoID.Sets.SpecificLauncherAmmoProjectileMatches[ItemID.SnowmanCannon].Add(Type, ModContent.ProjectileType<GodSlayerRocketProjectile>());
            AmmoID.Sets.SpecificLauncherAmmoProjectileMatches[ItemID.Celeb2].Add(Type, ProjectileID.Celeb2Rocket);
        }

        public override void SetDefaults()
        {
            Item.width = 24;
            Item.height = 24;
            Item.maxStack = 9999;
            Item.value = Item.sellPrice(silver: 1);
            Item.rare = ItemRarityID.Orange;
            Item.ammo = AmmoID.Rocket;
            Item.damage = 125;
        }

        public override void AddRecipes()
        {
            CreateRecipe(250)
                .AddIngredient<VoidBar>()
                .AddIngredient(ItemID.MiniNukeI, 250)
                .AddTile<VoidWellTile>()
                .Register();
        }
    }
}
