using CalamityEntropy.Common;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using CalamityEntropy.Content.Rarities;
using CalamityEntropy.Core.Weapons;
using InnoVault;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Weapons.VoidInvasion
{
    /// <summary>
    /// 超绝虚空铁拳(void-invasion.md §5.4):虚空魔像 4% 掉落的近战拳套。
    /// 快速直拳连打(短程判定);每第 4 拳变重拳(铁拳弹幕飞出)带小震波;
    /// 蓄势(命中计数 14)满 → 玩家朝指针短距冲撞(魔像冲撞玩家版,带无敌帧防撞脸自杀,伤害走近战)。
    /// 伤害定标:贴脸短程风险溢价,480@11t(名义 ~2618/s 贴脸)介于两档之间偏下。
    /// </summary>
    public class SupremeVoidFist : ModItem, ICEChargeWeapon
    {
        //蓄势:拳打命中计数 14;冲撞伤害乘数 1.6 由框架在消耗帧自动套用
        public CEChargeProfile ChargeProfile => CEChargeProfile.HitCount(14, 1.6f);

        private int punchCounter;

        public override void SetDefaults()
        {
            Item.width = 90;
            Item.height = 52;
            Item.damage = 480;
            Item.noMelee = true;
            Item.noUseGraphic = true;
            Item.useAnimation = Item.useTime = 11;
            Item.useStyle = ItemUseStyleID.Shoot;
            Item.knockBack = 7f;
            Item.UseSound = null;
            Item.autoReuse = true;
            Item.maxStack = 1;
            Item.value = Item.buyPrice(platinum: 2);
            Item.rare = ModContent.RarityType<VoidPurple>();
            Item.shoot = ModContent.ProjectileType<VoidFistJab>();
            Item.shootSpeed = 1f;
            Item.DamageType = DamageClass.Melee;
            punchCounter = 0;
        }

        public override void HoldItem(Player player)
        {
            int heldType = ModContent.ProjectileType<SupremeVoidFistHeldProj>();
            if (player.ownedProjectileCounts[heldType] < 1)
            {
                Projectile.NewProjectile(player.GetSource_FromThis(), player.MountedCenter, Vector2.Zero, heldType, 0, 0, player.whoAmI);
            }
        }

        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback)
        {
            Vector2 dir = velocity.SafeNormalize(Vector2.UnitX * player.direction);
            if (CEChargeWeapon.TryConsume(player, Item))
            {
                //蓄势满:短距冲撞(§5.4 魔像冲撞玩家版)
                int p = Projectile.NewProjectile(source, player.MountedCenter, dir,
                    ModContent.ProjectileType<VoidFistDash>(), damage, knockback * 1.4f, player.whoAmI);
                if (p.WithinBounds(Main.maxProjectiles))
                {
                    CEChargeWeapon.Empower(p);
                }
                punchCounter = 0;
                return false;
            }
            punchCounter++;
            if (punchCounter >= 4)
            {
                //第 4 拳重拳:铁拳弹幕飞出,命中带小震波
                punchCounter = 0;
                Projectile.NewProjectile(source, player.MountedCenter + dir * 26f, dir * 15f,
                    ModContent.ProjectileType<VoidFistPunchProj>(), (int)(damage * 1.5f), knockback * 1.3f, player.whoAmI);
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.Item74 with { Volume = 0.9f, Pitch = -0.2f }, player.Center);
                }
                return false;
            }
            //直拳:短程判定,左右拳交替(ai[0] 传交替位)
            Projectile.NewProjectile(source, player.MountedCenter, dir,
                ModContent.ProjectileType<VoidFistJab>(), damage, knockback, player.whoAmI, punchCounter % 2);
            if (!Main.dedServ)
            {
                SoundEngine.PlaySound(SoundID.Item1 with { Volume = 0.7f, Pitch = 0.4f }, player.Center);
            }
            return false;
        }
    }

    /// <summary>手持拳套(镜像 HowlingCannonHeld 姿势):随出拳前顶,朝向指针。</summary>
    public class SupremeVoidFistHeldProj : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Content/Items/Weapons/VoidInvasion/SupremeVoidFistHeld";

        public override bool? CanHitNPC(NPC target) => false;

        public override void SetDefaults()
        {
            Projectile.FriendlySetDefaults(DamageClass.Melee, false, -1);
            Projectile.width = Projectile.height = 6;
        }

        public float punchOffset = 0;

        public override void AI()
        {
            Player player = Projectile.GetOwner();
            if (player.dead)
            {
                Projectile.Kill();
                return;
            }
            if (player.HeldItem.ModItem is SupremeVoidFist)
            {
                Projectile.timeLeft = 2;
                //出拳瞬间前顶(以 itemAnimation 起拍驱动)
                if (player.itemAnimation == player.itemAnimationMax - 1)
                {
                    punchOffset = 24f;
                }
                player.Entropy().MouseWorldListener = true;
                Projectile.Center = player.GetDrawCenter();
                Projectile.rotation = (player.Entropy().MouseWorld - Projectile.Center).ToRotation();
                player.heldProj = Projectile.whoAmI;
                player.SetHandRot(Projectile.rotation);
            }
            punchOffset *= 0.8f;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = Projectile.GetTexture();
            int dir = Projectile.rotation.ToRotationVector2().X > 0 ? 1 : -1;
            Vector2 origin = new Vector2(tex.Width * 0.3f, tex.Height / 2f);
            SpriteEffects effect = dir > 0 ? SpriteEffects.None : SpriteEffects.FlipVertically;
            Main.EntitySpriteDraw(tex, Projectile.Center - Main.screenPosition + new Vector2(16 + punchOffset, 0).RotatedBy(Projectile.rotation),
                null, lightColor, Projectile.rotation, origin, Projectile.scale, effect, 0);
            return false;
        }
    }

    /// <summary>直拳判定:自玩家沿出拳方向 8t 内推出 40→110px 的短程刺线,无贴图(拳影粒子)。</summary>
    public class VoidFistJab : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Assets/Extra/Ports/Invisible";

        public const int Life = 8;

        public override void SetDefaults()
        {
            Projectile.FriendlySetDefaults(DamageClass.Melee, false, -1);
            Projectile.width = 30;
            Projectile.height = 30;
            Projectile.timeLeft = Life;
            Projectile.localNPCHitCooldown = -1;
        }

        public override bool ShouldUpdatePosition() => false;

        private float Reach => 40f + 70f * (1f - Projectile.timeLeft / (float)Life);

        public override void AI()
        {
            if (Projectile.localAI[0] == 0)
            {
                Projectile.localAI[0] = 1;
                Projectile.rotation = Projectile.velocity.ToRotation();
                Projectile.velocity = Vector2.Zero;
            }
            Projectile.Center = Projectile.GetOwner().MountedCenter;
            if (!Main.dedServ && Main.rand.NextBool(2))
            {
                //拳风粒子:沿刺线外推
                Vector2 side = (Projectile.rotation + MathHelper.PiOver2).ToRotationVector2() * Main.rand.NextFloat(-14f, 14f)
                    + (Projectile.ai[0] == 1 ? 10f : -10f) * (Projectile.rotation + MathHelper.PiOver2).ToRotationVector2();
                var v = PRTLoader.NewParticle<PRT_Void>(Projectile.Center + Projectile.rotation.ToRotationVector2() * Reach + side,
                    Projectile.rotation.ToRotationVector2() * Main.rand.NextFloat(3f, 7f), Color.White, 0.6f);
                v.Opacity = 0.4f;
            }
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            Vector2 c = Projectile.GetOwner().MountedCenter;
            return CEUtils.LineThroughRect(c, c + Projectile.rotation.ToRotationVector2() * Reach, targetHitbox, 46);
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            EGlobalNPC.AddVoidTouch(target, 60, 1, 600, 20);
            if (!Main.dedServ)
            {
                SoundEngine.PlaySound(SoundID.NPCHit1 with { Volume = 0.7f, Pitch = 0.5f }, target.Center);
            }
        }
    }

    /// <summary>重拳铁拳:飞行 ~20t,穿透 3,首次命中小震波(§5.4 每第 4 拳)。</summary>
    public class VoidFistPunchProj : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Content/Projectiles/VoidInvasion/VoidFistPunch";

        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light")]
        private static Asset<Texture2D> glowTex;

        private bool shockwaveDone = false;

        public override void SetDefaults()
        {
            Projectile.FriendlySetDefaults(DamageClass.Melee, false, 3);
            Projectile.width = 30;
            Projectile.height = 30;
            Projectile.timeLeft = 20;
            Projectile.localNPCHitCooldown = -1;
        }

        public override void AI()
        {
            Projectile.rotation = Projectile.velocity.ToRotation();
            Lighting.AddLight(Projectile.Center, 0.4f, 0.15f, 0.6f);
            if (!Main.dedServ && Main.rand.NextBool(2))
            {
                var v = PRTLoader.NewParticle<PRT_Void>(Projectile.Center, -Projectile.velocity * 0.15f, Color.White, 0.7f);
                v.Opacity = 0.45f;
            }
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            EGlobalNPC.AddVoidTouch(target, 120, 2, 600, 20);
            if (!shockwaveDone)
            {
                shockwaveDone = true;
                //小震波:所有者端结算的小范围爆
                CEUtils.SpawnExplotionFriendly(Projectile.GetSource_FromAI(), Projectile.GetOwner(),
                    target.Center, (int)(Projectile.damage * 0.5f), 100, Projectile.DamageType);
                CEUtils.SetShake(target.Center, 3);
                if (!Main.dedServ)
                {
                    PRTLoader.NewParticle<PRT_PulseRing>(target.Center, Vector2.Zero, new Color(160, 80, 235), 0.1f).Configure(0.8f, 9);
                    SoundEngine.PlaySound(SoundID.Item62 with { Volume = 0.6f, Pitch = 0.3f }, target.Center);
                }
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = TextureAssets.Projectile[Projectile.type].Value;
            CEUtils.DrawAfterimage(tex, Projectile.Entropy().odp, Projectile.Entropy().odr, 1.2f);
            Main.spriteBatch.Draw(tex, Projectile.Center - Main.screenPosition, null, lightColor, Projectile.rotation,
                tex.Size() / 2, Projectile.scale * 1.2f, SpriteEffects.None, 0);
            Main.spriteBatch.UseAdditive();
            Texture2D glow = glowTex.Value;
            Main.spriteBatch.Draw(glow, Projectile.Center - Main.screenPosition, null,
                new Color(180, 90, 250) * 0.6f, 0, glow.Size() / 2, 0.5f, SpriteEffects.None, 0);
            CEUtils.ReSetToEndShader();
            return false;
        }
    }

    /// <summary>
    /// 蓄势冲撞:玩家朝指针短冲 14t,期间随身携带碰撞判定,并逐帧给无敌帧(防撞脸自杀,§5.4);
    /// 速度施加只在所有者端(玩家坐标本机权威),旁观端吃原生位置同步。
    /// </summary>
    public class VoidFistDash : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Assets/Extra/Ports/Invisible";

        public const int Life = 14;
        public const float DashSpeed = 17f;

        public override void SetDefaults()
        {
            Projectile.FriendlySetDefaults(DamageClass.Melee, false, -1);
            Projectile.width = 92;
            Projectile.height = 92;
            Projectile.timeLeft = Life;
            Projectile.localNPCHitCooldown = 24;
        }

        public override bool ShouldUpdatePosition() => false;

        public override void AI()
        {
            Player player = Projectile.GetOwner();
            if (Projectile.localAI[0] == 0)
            {
                Projectile.localAI[0] = 1;
                Projectile.rotation = Projectile.velocity.ToRotation();
                Projectile.velocity = Vector2.Zero;
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.Item74 with { Volume = 1f, Pitch = 0.1f }, player.Center);
                    CEUtils.SetShake(player.Center, 4);
                }
            }
            Projectile.Center = player.MountedCenter;
            //冲撞推进与无敌帧只动本机玩家
            if (player.whoAmI == Main.myPlayer && Projectile.timeLeft > 3)
            {
                player.velocity = Projectile.rotation.ToRotationVector2() * DashSpeed * (Projectile.timeLeft / (float)Life);
                player.GiveImmuneTimeForCollisionAttack(6);
            }
            if (!Main.dedServ)
            {
                for (int i = 0; i < 2; i++)
                {
                    var v = PRTLoader.NewParticle<PRT_Void>(player.Center + CEUtils.randomPointInCircle(34),
                        -Projectile.rotation.ToRotationVector2() * Main.rand.NextFloat(2f, 6f), Color.White, 0.9f);
                    v.Opacity = 0.5f;
                }
            }
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            EGlobalNPC.AddVoidTouch(target, 180, 3, 600, 20);
            CEUtils.SetShake(target.Center, 5);
            if (!Main.dedServ)
            {
                PRTLoader.NewParticle<PRT_PulseRing>(target.Center, Vector2.Zero, new Color(170, 90, 240), 0.1f).Configure(1f, 10);
                SoundEngine.PlaySound(SoundID.Item62 with { Volume = 0.8f, Pitch = 0.1f }, target.Center);
            }
        }
    }
}
