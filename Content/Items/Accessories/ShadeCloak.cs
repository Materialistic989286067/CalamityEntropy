using CalamityEntropy.Common;
using CalamityEntropy.Content.Cooldowns;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Core.Cooldowns;
using InnoVault.PRT;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Accessories
{
    public class ShadeCloak : ModItem
    {
        // 自带暗影冲刺,冲刺期间无敌,固定5秒冷却,装备期间排斥其他冲刺来源。
        public static int CooldownTicks = 5 * 60;
        public const int DashDuration = 24;
        public const float DashSpeed = 18f;

        public override void SetDefaults()
        {
            Item.width = 42;
            Item.height = 42;
            Item.value = Item.buyPrice(gold: 20);
            Item.rare = ItemRarityID.Orange;
            Item.accessory = true;
            Item.expert = true;
        }
        public static string ID = "ShadeCloak";

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            player.Entropy().addEquip(ID, !hideVisual);
            player.Entropy().shadeDashExclusive = true;
            // 不挂原版 dashType,避免强化他人冲刺;排他期间关掉原版冲刺
            player.dashType = 0;
        }
        public override void UpdateVanity(Player player)
        {
            player.Entropy().addEquipVisual(ID);
        }
        public override void AddRecipes()
        {
            /*CreateRecipe().AddIngredient(ItemID.SoulofNight, 8)
                .AddIngredient(ItemID.Ectoplasm, 12)
                .AddIngredient(ItemID.SoulofNight, 4)
                .AddIngredient(ItemID.Ectoplasm, 8);*/
        }
    }
    /// <summary>暗影披风自管水平冲刺:输入与盾冲刺共用,冷却走 CECooldown,无敌只在冲刺窗口内。</summary>
    public class SCDashMP : ModPlayer
    {
        public int DashTimer;
        public int DashDir = 1;

        public override void PreUpdateMovement()
        {
            if (Player.whoAmI != Main.myPlayer)
                return;

            var mp = Player.Entropy();
            bool equipped = mp.hasAcc(ShadeCloak.ID);
            bool visual = equipped || mp.hasAccVisual(ShadeCloak.ID);

            if (DashTimer > 0)
            {
                UpdateDash(mp, equipped, visual);
                return;
            }

            if (!equipped || Player.mount.Active)
                return;
            if (Player.HasCooldown(ShadeCloakDashCD.ID))
                return;
            if (!CEShieldDashPlayer.TryGetHorizontalDashDirection(Player, out int direction))
                return;

            StartDash(mp, direction, visual);
        }

        private void StartDash(EModPlayer mp, int direction, bool visual)
        {
            DashDir = direction;
            DashTimer = ShadeCloak.DashDuration;
            Player.velocity.X = direction * ShadeCloak.DashSpeed;
            Player.ChangeDir(direction);
            Player.timeSinceLastDashStarted = 0;
            Player.RemoveAllGrapplingHooks();
            Player.AddCooldown(ShadeCloakDashCD.ID, ShadeCloak.CooldownTicks);

            if (!visual)
                return;

            CEUtils.PlaySound("Dash2", 1, Player.Center);
            mp.avTrail = PRTLoader.NewParticle<PRT_DashBeam>(Player.Center, Vector2.Zero, new Color(0, 0, 0, 210), 1f)
                .Configure(1, true, PRTDrawModeEnum.NonPremultiplied);
            mp.avTrail.maxLength = 30;
            for (int i = 0; i < 12; i++)
            {
                var orb = PRTLoader.NewParticle<PRT_ShadeCloakOrb>(Vector2.Zero, CEUtils.randomPointInCircle(4), Color.Black, 1)
                    .Configure(1, true, PRTDrawModeEnum.NonPremultiplied, -1, ShadeCloak.CooldownTicks);
                orb.PlayerIndex = Player.whoAmI;
            }
        }

        private void UpdateDash(EModPlayer mp, bool equipped, bool visual)
        {
            Player.dashDelay = -1;
            Player.velocity.X = DashDir * ShadeCloak.DashSpeed;
            Player.ChangeDir(DashDir);

            if (equipped)
            {
                Player.RemoveAllGrapplingHooks();
                if (mp.immune < 4)
                    mp.immune = 4;
            }

            Vector2 dashVel = new Vector2(Player.velocity.X, 0f);
            if (visual && dashVel.LengthSquared() > 4f)
            {
                Vector2 dashDir = dashVel.SafeNormalize(Vector2.UnitX * DashDir);
                for (int i = 0; i < 2; i++)
                {
                    PRTLoader.NewParticle<PRT_ShadeDashParticle>(Player.Center + dashVel * 6
                        + CEUtils.randomPointInCircle(26), -dashDir.RotatedByRandom(0.12f) * 40, Color.White, 1)
                        .Configure(1, true, PRTDrawModeEnum.NonPremultiplied, 0, 16);
                }
            }

            DashTimer--;
            if (DashTimer <= 0 && mp.avTrail != null)
            {
                mp.avTrail.Lifetime = mp.avTrail.Time + 30;
            }
        }
    }
}
