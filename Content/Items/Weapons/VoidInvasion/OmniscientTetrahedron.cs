using CalamityEntropy.Common;
using CalamityEntropy.Content.Buffs;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Rarities;
using InnoVault;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Weapons.VoidInvasion
{
    /// <summary>
    /// 全知金四面体(void-invasion.md §5.4):教皇掉落的召唤武器。
    /// 召唤一只全知之眼(教皇 P2-6 玩家版,全知之眼贴图 ×0.6)绕玩家公转,
    /// 自动对 900px 内敌人螺旋吐魔焰弹。
    /// 伤害定标:召唤锚点机制差异过大(Nyxolithraken 1750 慢重击 ↔ PhantomPlanetKillerEngine 100 速射),
    /// 按弹速换算定标:每 9t 一发 ×240 ≈ 名义 1600/s·槽,低于 VoidOde 名义 1714,偏下成立。
    /// </summary>
    public class OmniscientTetrahedron : ModItem
    {
        public override void SetStaticDefaults()
        {
            ItemID.Sets.GamepadWholeScreenUseRange[Item.type] = true;
            ItemID.Sets.LockOnIgnoresCollision[Item.type] = true;
        }

        public override void ModifyTooltips(List<TooltipLine> tooltips)
        {
            tooltips.IntegrateHotkey(CEKeybinds.CommandMinions);
        }

        public override void SetDefaults()
        {
            Item.damage = 240;
            Item.DamageType = DamageClass.Summon;
            Item.width = 100;
            Item.height = 100;
            Item.useTime = 30;
            Item.useAnimation = 30;
            Item.knockBack = 2f;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.shoot = ModContent.ProjectileType<OmniscientEyeMinion>();
            Item.shootSpeed = 2f;
            Item.value = Item.buyPrice(platinum: 2, gold: 20);
            Item.autoReuse = true;
            Item.UseSound = SoundID.Item44;
            Item.noMelee = true;
            Item.mana = 10;
            Item.buffType = ModContent.BuffType<OmniscientEyeBuff>();
            Item.rare = ModContent.RarityType<VoidPurple>();
        }

        public override bool Shoot(Player player, Terraria.DataStructures.EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback)
        {
            player.AddBuff(Item.buffType, 3);
            int projectile = Projectile.NewProjectile(source, Main.MouseWorld, Vector2.Zero, type, Item.damage, knockback, player.whoAmI);
            Main.projectile[projectile].originalDamage = Item.damage;
            return false;
        }
    }

    /// <summary>
    /// 全知之眼随从:绕玩家公转;900px 内有敌人时朝向目标,以螺旋摆角每 9t 吐一枚魔焰弹。
    /// 公转相位由 minionPos 错开,多眼不重叠。
    /// </summary>
    public class OmniscientEyeMinion : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Content/Projectiles/VoidInvasion/OmniscientEye";

        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light")]
        private static Asset<Texture2D> glowTex;

        public const float SeekRange = 900f;
        public const float OrbitRadius = 116f;
        public const int FireInterval = 9;

        private float firePhase = 0;
        private int fireCd = 0;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Projectile.type] = 1;
            ProjectileID.Sets.MinionSacrificable[Projectile.type] = true;
            ProjectileID.Sets.MinionTargettingFeature[Projectile.type] = true;
        }

        public override void SetDefaults()
        {
            Projectile.DamageType = DamageClass.Summon;
            Projectile.width = 54;
            Projectile.height = 42;
            Projectile.friendly = false;
            Projectile.aiStyle = -1;
            Projectile.penetrate = -1;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = 120;
            Projectile.tileCollide = false;
            Projectile.minion = true;
            Projectile.minionSlots = 1;
            Projectile.scale = 0.6f;
            Projectile.netImportant = true;
        }

        public override bool? CanCutTiles() => false;
        public override bool? CanHitNPC(NPC target) => false;

        public override void AI()
        {
            Player player = Projectile.GetOwner();
            if (player.dead || !player.active)
            {
                return;
            }
            if (player.HasBuff(ModContent.BuffType<OmniscientEyeBuff>()))
            {
                Projectile.timeLeft = 3;
            }

            //公转:相位按随从序号错开
            float orbitAng = (float)Main.timeForVisualEffects * 0.024f + Projectile.minionPos * MathHelper.TwoPi / Math.Max(1, player.slotsMinions);
            Vector2 want = player.MountedCenter + orbitAng.ToRotationVector2() * OrbitRadius;
            Projectile.Center = Vector2.Lerp(Projectile.Center, want, 0.16f);
            if (CEUtils.getDistance(Projectile.Center, player.Center) > 2000)
            {
                Projectile.Center = want;
            }

            //索敌
            NPC target = null;
            if (player.MinionAttackTargetNPC >= 0 && Main.npc[player.MinionAttackTargetNPC].active
                && Main.npc[player.MinionAttackTargetNPC].CanBeChasedBy(Projectile))
            {
                target = Main.npc[player.MinionAttackTargetNPC];
            }
            target ??= CEUtils.FindTarget_HomingProj(Projectile, Projectile.Center, SeekRange);

            if (target != null)
            {
                //螺旋吐弹(§5.4 P2-6 玩家版):朝向目标 + 正弦摆角连发
                firePhase += 0.55f;
                float baseRot = (target.Center - Projectile.Center).ToRotation();
                Projectile.rotation = baseRot;
                fireCd--;
                if (fireCd <= 0 && Main.myPlayer == Projectile.owner)
                {
                    fireCd = FireInterval;
                    float rot = baseRot + (float)Math.Sin(firePhase) * 0.32f;
                    Projectile.NewProjectile(Projectile.GetSource_FromAI(), Projectile.Center,
                        rot.ToRotationVector2() * 13f, ModContent.ProjectileType<MinionEyeBolt>(),
                        Projectile.damage, Projectile.knockBack, Projectile.owner);
                }
            }
            else
            {
                Projectile.rotation = orbitAng + MathHelper.PiOver2;
                fireCd = Math.Max(fireCd - 1, 0);
            }

            Lighting.AddLight(Projectile.Center, 0.4f, 0.25f, 0.6f);
            if (!Main.dedServ && Main.rand.NextBool(9))
            {
                var p = PRTLoader.NewParticle<PRT_Light>(Projectile.Center + CEUtils.randomPointInCircle(24),
                    Vector2.Zero, new Color(230, 190, 110), 0.28f);
                p.Configure(0.85f, lifetime: 10);
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = TextureAssets.Projectile[Projectile.type].Value;
            Vector2 pos = Projectile.Center - Main.screenPosition;
            Main.spriteBatch.Draw(tex, pos, null, Color.White, Projectile.rotation, tex.Size() / 2, Projectile.scale, SpriteEffects.None, 0);
            Main.spriteBatch.UseAdditive();
            Texture2D glow = glowTex.Value;
            float pulse = 1f + 0.12f * (float)Math.Sin(Main.timeForVisualEffects * 0.06f);
            Main.spriteBatch.Draw(glow, pos, null, new Color(235, 190, 120) * 0.5f, 0, glow.Size() / 2, 0.6f * pulse, SpriteEffects.None, 0);
            CEUtils.ReSetToEndShader();
            return false;
        }
    }

    /// <summary>随从魔焰弹:魔眼弹贴图友方召唤版,直飞穿透 1。</summary>
    public class MinionEyeBolt : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Content/Projectiles/VoidInvasion/MagicEyeBolt";

        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light")]
        private static Asset<Texture2D> glowTex;

        public override void SetDefaults()
        {
            Projectile.FriendlySetDefaults(DamageClass.Summon, false, 1);
            Projectile.width = 22;
            Projectile.height = 22;
            Projectile.timeLeft = 180;
            Projectile.localNPCHitCooldown = -1;
        }

        public override void AI()
        {
            Projectile.rotation = Projectile.velocity.ToRotation();
            Lighting.AddLight(Projectile.Center, 0.45f, 0.2f, 0.65f);
            if (!Main.dedServ && Main.rand.NextBool(3))
            {
                var p = PRTLoader.NewParticle<PRT_Light>(Projectile.Center - Projectile.velocity * 0.5f,
                    -Projectile.velocity * 0.1f, new Color(230, 170, 255), 0.32f);
                p.Configure(0.85f, lifetime: 10);
            }
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            EGlobalNPC.AddVoidTouch(target, 90, 1, 600, 20);
        }

        public override void OnKill(int timeLeft)
        {
            if (Main.dedServ)
            {
                return;
            }
            for (int i = 0; i < 4; i++)
            {
                var v = PRTLoader.NewParticle<PRT_Void>(Projectile.Center,
                    CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(1f, 3.5f), Color.White, 0.7f);
                v.Opacity = Main.rand.Next(30, 60) * 0.01f;
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = TextureAssets.Projectile[Projectile.type].Value;
            CEUtils.DrawAfterimage(tex, Projectile.Entropy().odp, Projectile.Entropy().odr, 0.9f);
            Main.spriteBatch.Draw(tex, Projectile.Center - Main.screenPosition, null, Color.White,
                Projectile.rotation, tex.Size() / 2, Projectile.scale * 0.9f, SpriteEffects.None, 0);
            Main.spriteBatch.UseAdditive();
            Texture2D glow = glowTex.Value;
            Main.spriteBatch.Draw(glow, Projectile.Center - Main.screenPosition, null,
                new Color(220, 150, 255) * 0.55f, 0, glow.Size() / 2, 0.45f, SpriteEffects.None, 0);
            CEUtils.ReSetToEndShader();
            return false;
        }
    }
}
