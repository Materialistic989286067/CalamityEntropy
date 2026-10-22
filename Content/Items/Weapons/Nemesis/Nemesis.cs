using CalamityEntropy.Common;
using CalamityEntropy.Content.Tiles;
using CalamityEntropy.Core.CalamityRef;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Weapons.Nemesis
{
    public class Nemesis : ModItem, IDevItem
    {
        public string DevName => "锯角";
        private int fireIndex;
        public override void SetDefaults() {
            Item.height = 154;
            Item.width = 154;
            Item.damage = 360;
            Item.DamageType = DamageClass.Melee;
            Item.useAnimation = Item.useTime = 18;
            Item.scale = 1;
            Item.useTurn = true;
            Item.useStyle = ItemUseStyleID.Shoot;
            Item.noMelee = true;
            Item.noUseGraphic = true;
            Item.knockBack = 5.5f;
            Item.UseSound = null;
            Item.autoReuse = true;
            Item.value = Item.buyPrice(platinum: 3, gold: 20);
            Item.rare = ItemRarityID.Red;
            Item.shoot = ModContent.ProjectileType<NemesisHeld>();
            Item.shootSpeed = 18f;
            fireIndex = 0;
            ItemID.Sets.ItemsThatAllowRepeatedRightClick[Type] = true;
        }

        public override bool AltFunctionUse(Player player) => true;

        //手持弹幕存活期间不许再次使用,蓄力时长可变,靠它而不是 useTime 收口
        public override bool CanUseItem(Player player) => player.ownedProjectileCounts[Item.shoot] <= 0;

        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position
            , Vector2 velocity, int type, int damage, float knockback) {
            int newLevel = 0;
            if (++fireIndex > 6) {
                newLevel = 1;
                fireIndex = 0;
            }
            if (player.altFunctionUse == 2) {
                newLevel = 2;
            }
            //ai[1] 恒为 +1:每次都从上往下劈,不再往返
            Projectile.NewProjectile(source, position, velocity, type, damage, knockback, player.whoAmI, newLevel, 1f);
            return false;
        }

        public override void AddRecipes() {
            if (CECal.CalChainReady(CEID.Item_GalactusBlade, CEID.Item_TheBurningSky)) {
                CreateRecipe().AddIngredient(CEID.Item_GalactusBlade)
                    .AddIngredient(CEID.Item_TheBurningSky)
                    .AddIngredient<FadingRunestone>()
                    .AddTile<VoidWellTile>()
                    .Register();
                return;
            }
            CreateRecipe().AddIngredient<BlazingSunlight>(5)
                .AddIngredient(ItemID.StarWrath)
                .AddIngredient<FadingRunestone>()
                .AddTile<VoidWellTile>()
                .Register();
        }
    }
}
