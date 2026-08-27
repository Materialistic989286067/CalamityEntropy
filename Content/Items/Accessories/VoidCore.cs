using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using CalamityEntropy.Content.Rarities;
using InnoVault.PRT;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.Graphics.Shaders;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Accessories
{
    public class VoidCore : ModItem
    {
        public const int ShieldSlamDamage = 800;
        public const float ShieldSlamKnockback = 8f;
        public const int ShieldSlamIFrames = 18;
        public static int DashDelay = 20;
        public float charge = 0;
        public static int MaxShield = 60;
        public static int ShieldRecharge = 20 * 60;
        public static float CritDamage = 0.05f;
        public override void SetDefaults()
        {
            Item.width = 60;
            Item.height = 60;
            Item.value = Item.buyPrice(platinum: 2, gold: 40);
            Item.defense = 8;
            Item.accessory = true;
            Item.rare = ModContent.RarityType<VoidPurple>();
        }

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            player.Entropy().VoidShieldVisual = !hideVisual;
            player.Entropy().VoidCoreItem = Item;
            player.GetModPlayer<CEShieldDashPlayer>().ActiveDash = VoidCoreDash.Instance;
            player.dashType = 0;
            player.AddCritDamage(DamageClass.Generic, CritDamage);
        }
        public override void UpdateVanity(Player player)
        {
            player.Entropy().VoidShieldVisual = true;
        }
        public override void ModifyTooltips(List<TooltipLine> tooltips)
        {
            tooltips.Replace("[S]", MaxShield.ToString());
            tooltips.Replace("[C]", CritDamage.ToPercent().ToString());

        }
        public override void AddRecipes()
        {
            // 脱离灾厄:原 RuinousSoul×6 按 material-map 换虚无碎片并与原有 10 枚合并
            CreateRecipe()
                .AddIngredient<AzafureDriverCore>()
                .AddIngredient<NihilityFragments>(16)
                .AddTile(TileID.LunarCraftingStation)
                .Register();
        }
    }
    public class VoidCoreDash : CEShieldDashEffect
    {
        public static readonly VoidCoreDash Instance = new();

        public int Time;

        public bool PostHit;

        public static string ID => "VoidCoreDash";
        public override string DashID => ID;

        public override float CalculateDashSpeed(Player player)
        {
            return 26f;
        }

        public override void OnDashEffects(Player player)
        {
            Time = 0;
            PostHit = false;
        }

        public override void MidDashEffects(Player player, ref float dashSpeed, ref float dashSpeedDecelerationFactor, ref float runSpeedDecelerationFactor)
        {
            Time += 2;
            if (Time > 44)
            {
                player.velocity.X *= 0.96f;
            }
            else
            {

            }
            if (Time < 60)
            {
                float num = MathHelper.Lerp(0f, 1f, Utils.GetLerpValue(2f, 2.5f, Time, clamped: true));
                for (float i = 0; i < 1; i += 0.1f)
                {
                    PRTLoader.NewParticle<PRT_GlowSpark>(CEUtils.randomPointInCircle(18) + player.Center - player.velocity * i, -player.velocity.RotatedByRandom(0.32f) * Main.rand.NextFloat(0.4f, 0.6f), Color.Lerp(new Color(100, 100, 255), Color.LightBlue, Main.rand.NextFloat()), Main.rand.NextFloat(0.1f, 0.14f)).Configure(1, true, PRTDrawModeEnum.AdditiveBlend, -player.velocity.ToRotation(), 16);
                }
                for (int i = 0; i < 6; i++)
                {
                    float f = player.velocity.ToRotation() + (float)Time / 5f;
                    float num2 = (15f + (float)Math.Cos((float)Time / 3f) * 12f) * num;
                    Dust dust = Dust.NewDustPerfect(player.Center - player.velocity * 2f + f.ToRotationVector2().RotatedBy((float)i / 5f * (MathF.PI * 2f)) * num2, Main.rand.NextBool(5) ? DustID.BlueTorch : DustID.CosmicCarKeys);
                    dust.alpha = 140;
                    dust.noGravity = true;
                    dust.velocity = player.velocity * -0.8f;
                    dust.scale = Main.rand.NextFloat(1.7f, 2f);
                    dust.shader = GameShaders.Armor.GetSecondaryShader(player.cShield, player);
                    Dust dust2 = Dust.NewDustPerfect(player.Center + new Vector2(Main.rand.NextFloat(-6f, 6f), Main.rand.NextFloat(-15f, 15f)) + player.velocity * 1.5f, Main.rand.NextBool(6) ? DustID.SparksMech : DustID.MinecartSpark, -player.velocity.RotatedByRandom(MathHelper.ToRadians(30f)) * Main.rand.NextFloat(0.1f, 0.8f), 0, default(Color), Main.rand.NextFloat(1.7f, 1.9f));
                    dust2.alpha = 140;
                    dust.scale = dust2.scale = 0.7f;
                    dust2.noGravity = true;
                    dust2.shader = GameShaders.Armor.GetSecondaryShader(player.cShield, player);
                }
                for (int i = 0; i < 3; i++)
                {
                    PRTLoader.NewParticle<PRT_LineCal>(CEUtils.randomPointInCircle(18) + player.Center - player.velocity * Main.rand.NextFloat(), -player.velocity * Main.rand.NextFloat(0.4f, 0.6f), Color.LightBlue, Main.rand.NextFloat(0.6f, 1)).Configure(false, 8);
                }
                //AbyssalLine被EffectLoader捞起走RT合成,xadd/lx得spawn后赋
                var dashLine = PRTLoader.NewParticle<PRT_AbyssalLine>(player.Center - player.velocity, Vector2.Zero, Color.LightBlue, 1);
                dashLine.xadd = 0.84f;
                dashLine.lx = 0.84f;
                dashLine.Configure(1, true, PRTDrawModeEnum.AdditiveBlend, player.velocity.ToRotation(), 26);
                dashSpeed = 20f;
            }
        }

        public override void OnHitEffects(Player player, NPC npc, ref CEDashHitContext hitContext)
        {
            if (!PostHit)
            {
                // 脱离灾厄:原灾厄 GeneralScreenShakePower=6,改用自有屏震
                ScreenShaker.AddShake(new ScreenShaker.ScreenShake(Vector2.Zero, 6));
                PostHit = true;
            }
            NPC target = npc;

            PRTLoader.NewParticle<PRT_ShineParticle>(player.Center, Vector2.Zero, Color.Blue, 1.4f).Configure(1, true, PRTDrawModeEnum.AdditiveBlend, 0, 12);
            PRTLoader.NewParticle<PRT_ShineParticle>(player.Center, Vector2.Zero, Color.White, 0.8f).Configure(1, true, PRTDrawModeEnum.AdditiveBlend, 0, 12);
            float r2 = player.velocity.ToRotation();
            float r = player.velocity.ToRotation();

            var hitLine1 = PRTLoader.NewParticle<PRT_AbyssalLine>(player.Center, Vector2.Zero, new Color(30, 10, 50), 1);
            hitLine1.xadd = 1.4f;
            hitLine1.lx = 3.2f;
            hitLine1.Configure(1, true, PRTDrawModeEnum.NonPremultiplied, r, 30);
            var hitLine2 = PRTLoader.NewParticle<PRT_AbyssalLine>(player.Center, Vector2.Zero, new Color(30, 10, 50), 1);
            hitLine2.xadd = 1.4f;
            hitLine2.lx = 3.2f;
            hitLine2.Configure(1, true, PRTDrawModeEnum.NonPremultiplied, r2, 30);

            var hitLine3 = PRTLoader.NewParticle<PRT_AbyssalLine>(player.Center, Vector2.Zero, new Color(80, 40, 120), 1);
            hitLine3.xadd = 1.36f;
            hitLine3.lx = 3.2f;
            hitLine3.Configure(1, true, PRTDrawModeEnum.NonPremultiplied, r, 30);
            var hitLine4 = PRTLoader.NewParticle<PRT_AbyssalLine>(player.Center, Vector2.Zero, new Color(80, 40, 120), 1);
            hitLine4.xadd = 1.36f;
            hitLine4.lx = 3.2f;
            hitLine4.Configure(1, true, PRTDrawModeEnum.NonPremultiplied, r2, 30);

            var hitLine5 = PRTLoader.NewParticle<PRT_AbyssalLine>(player.Center, Vector2.Zero, Color.LightBlue, 1);
            hitLine5.xadd = 1.34f;
            hitLine5.lx = 3f;
            hitLine5.Configure(1, true, PRTDrawModeEnum.AdditiveBlend, r, 36);
            var hitLine6 = PRTLoader.NewParticle<PRT_AbyssalLine>(player.Center, Vector2.Zero, Color.LightBlue, 1);
            hitLine6.xadd = 1.34f;
            hitLine6.lx = 3f;
            hitLine6.Configure(1, true, PRTDrawModeEnum.AdditiveBlend, r2, 36);

            CEUtils.PlaySound("amethyst_break", 1, npc.Center, 6, 0.6f);
            CEUtils.PlaySound("AntivoidDash", 1, npc.Center, 6, 0.6f);
            CEUtils.PlaySound("ExoHit" + Main.rand.Next(1, 5), Main.rand.NextFloat(1.6f, 1.9f), target.Center, 6, 0.3f);
            hitContext.HitDirection = Math.Sign(player.velocity.X);
            hitContext.PlayerImmunityFrames = 16;
            int num = VoidCore.ShieldSlamDamage;
            hitContext.damageClass = DamageClass.Melee;
            hitContext.BaseDamage = num.ApplyAccArmorDamageBonus(player);
            hitContext.BaseKnockback = 6f;
        }
    }
}
