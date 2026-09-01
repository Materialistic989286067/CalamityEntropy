using CalamityEntropy.Common;
using CalamityEntropy.Content.Projectiles.SamsaraCasket;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Weapons
{
    public class NoneTypeDamageClass : DamageClass
    {
        internal static NoneTypeDamageClass Instance;


        public override void Load()
        {
            Instance = this;
        }

        public override void Unload()
        {
            Instance = null;
        }

        public override StatInheritanceData GetModifierInheritance(DamageClass damageClass)
        {
            // 盗贼职业已并入原版投掷,继承比例照旧
            if (damageClass == Throwing)
            {
                return new StatInheritanceData(0.2f, 0.2f, 0.2f, 0.2f, 0.2f);
            }
	    if (damageClass == Summon)
            {
                return new StatInheritanceData(0.3f, 0.3f, 0.3f, 0.3f, 0.3f);
            }
            return StatInheritanceData.Full;
        }
    }
    public class HorizonssKey : ModItem
    {
        // 2026-08-31 平衡案:去除成长性,重做为占用8仆从栏的召唤师武器,固定面板。
        public const int BaseDamage = 50;
        public const float MinionSlotCost = 8f;
        public override bool AltFunctionUse(Player player) => true;
        public override void SetDefaults()
        {
            Item.width = 20;
            Item.height = 20;
            Item.useTime = 30;
            Item.useAnimation = 30;
            Item.useStyle = ItemUseStyleID.RaiseLamp;
            Item.damage = BaseDamage;
            Item.DamageType = DamageClass.Summon;
            Item.noMelee = true;
            Item.value = Item.buyPrice(silver: 1);
            Item.rare = ItemRarityID.Red;
            Item.Entropy().Legend = true;
        }
        public override bool PreDrawInWorld(SpriteBatch spriteBatch, Color lightColor, Color alphaColor, ref float rotation, ref float scale, int whoAmI)
        {
            Item.QuickDrawItemWithBloomToWorld(spriteBatch, Color.HotPink, ref scale, rotation);
            return false;
        }
        public override bool? UseItem(Player player)
        {

            if (player.altFunctionUse == 2)
            {
                if (player.whoAmI == Main.myPlayer)
                {
                    float dist = 400;
                    int npc = -1;
                    foreach (NPC n in Main.npc)
                    {
                        if (n.active && !n.friendly && CEUtils.getDistance(n.Center, Main.MouseWorld) < dist)
                        {
                            npc = n.whoAmI;
                            dist = CEUtils.getDistance(n.Center, Main.MouseWorld);
                        }
                    }
                    if (npc >= 0)
                    {
                        player.MinionAttackTargetNPC = npc;
                    }
                }
            }
            else
            {
                player.Entropy().samsaraCasketOpened = !player.Entropy().samsaraCasketOpened;
                if (Main.myPlayer == player.whoAmI && player.Entropy().samsaraCasketOpened)
                {
                    int p = Projectile.NewProjectile(player.GetSource_FromAI(), player.Center - new Vector2(0, 60), Vector2.Zero, ModContent.ProjectileType<e0>(), 0, 0, -1);
                    SoundEngine.PlaySound(new SoundStyle("CalamityEntropy/Assets/Sounds/AscendantActivate"), player.Center);
                }
            }

            return true;
        }


        public override void HoldItem(Player player)
        {
            // 去成长:棺体能力恒为最高档
            player.Entropy().sCasketLevel = 6;
            if (player.ownedProjectileCounts[ModContent.ProjectileType<SamsaraCasketProj>()] < 1
                && player.maxMinions - player.slotsMinions >= MinionSlotCost)
            {
                int p = Projectile.NewProjectile(player.GetSource_ItemUse(Item), player.Center, Vector2.Zero, ModContent.ProjectileType<SamsaraCasketProj>(), Item.damage, player.GetWeaponKnockback(Item), player.whoAmI);
                if (p >= 0 && p < Main.maxProjectiles)
                {
                    Main.projectile[p].originalDamage = Item.damage;
                }
            }
        }

        public static float getVoidTouchLevel()
        {
            // 2026-08-31 平衡案:不再造成虚空之触
            return 0;
        }

        public static int getArmorPen()
        {
            return 50 + 10 * Main.LocalPlayer.Entropy().WeaponBoost;
        }

        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(ItemID.BreakerBlade)
                .AddIngredient(ItemID.FragmentStardust, 5)
                .AddIngredient(ItemID.LunarBar, 5)
                .AddTile(TileID.LunarCraftingStation).Register();
        }
    }
}
