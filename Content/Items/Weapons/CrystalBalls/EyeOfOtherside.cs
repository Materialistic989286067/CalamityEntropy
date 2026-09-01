using CalamityEntropy.Content.Items;
using CalamityEntropy.Content.Projectiles;
using CalamityEntropy.Content.Rarities;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Weapons.CrystalBalls
{
    public class EyeOfOtherside : ModItem
    {
        public override void SetDefaults()
        {
            Item.width = 44;
            Item.height = 44;
            Item.damage = 270;
            Item.noMelee = true;
            Item.noUseGraphic = true;
            Item.useAnimation = Item.useTime = 20;
            Item.useStyle = ItemUseStyleID.Shoot;
            Item.channel = true;
            Item.knockBack = 6f;
            Item.UseSound = CEUtils.GetSound("soulshine");
            Item.maxStack = 1;
            Item.value = Item.buyPrice(1, 75);
            Item.rare = ModContent.RarityType<GlowGreen>();
            Item.shoot = ModContent.ProjectileType<EyeOfOthersideHoldout>();
            Item.shootSpeed = 16f;
            Item.mana = 3;
            Item.DamageType = DamageClass.Magic;
        }
        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(ItemID.CrystalBall)
                .AddIngredient<NihilityFragments>(15)
                .AddTile(TileID.LunarCraftingStation)
                .Register();
        }
        public override bool MagicPrefix()
        {
            return true;
        }
    }
}
