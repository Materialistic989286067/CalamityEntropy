using CalamityEntropy.Common;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Rarities;
using CalamityEntropy.Core.Weapons;
using InnoVault;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Weapons.VoidInvasion
{
    /// <summary>
    /// 咒怨咆哮(void-invasion.md §5.4):教皇掉落的远程枪(280x136 源图,持握 scale 0.8,握点取枪身后 1/3)。
    /// 常态:三连点射虚空弹;蓄势(充能条 7s)满 → 下次射击变为"咆哮":张开虚口喷出锥形音波,
    /// 短程高伤 + 大击退,命中附虚空触。
    /// 自由裁量:咆哮走蓄势消耗(就绪后自动作为下次攻击释放),不做右键通道,与全库蓄势武器交互一致。
    /// 伤害定标:HowlingCannon 900@15t(深渊亡魂档,单发拆分 2×450)↔ LightWisper 320@3t(终局档速射),
    /// 三连点射 380/发(名义 ~2850/s)偏下成立。
    /// </summary>
    public class CurseRoar : ModItem, ICEChargeWeapon
    {
        //充能条 7 秒;咆哮伤害由波体显式 ×4,乘数不走框架(避免双重放大)
        public CEChargeProfile ChargeProfile => CEChargeProfile.ChargeBar(7f);

        public bool JustShooted = false;

        public override void SetDefaults()
        {
            Item.width = 100;
            Item.height = 48;
            Item.damage = 380;
            Item.noMelee = true;
            Item.noUseGraphic = true;
            //三连点射:useTime 5 ×3 发一轮,轮间 reuseDelay
            Item.useAnimation = 15;
            Item.useTime = 5;
            Item.reuseDelay = 10;
            Item.useStyle = ItemUseStyleID.Shoot;
            Item.knockBack = 3.5f;
            Item.maxStack = 1;
            Item.value = Item.buyPrice(platinum: 2, gold: 20);
            Item.rare = ModContent.RarityType<VoidPurple>();
            Item.shoot = ModContent.ProjectileType<CurseVoidBullet>();
            Item.shootSpeed = 17f;
            Item.DamageType = DamageClass.Ranged;
            Item.autoReuse = true;
            Item.crit = 8;
        }

        public override bool RangedPrefix()
        {
            return true;
        }

        public override void HoldItem(Player player)
        {
            int heldType = ModContent.ProjectileType<CurseRoarHeld>();
            if (player.ownedProjectileCounts[heldType] < 1)
            {
                Projectile.NewProjectile(player.GetSource_FromThis(), player.MountedCenter, Vector2.Zero, heldType, 0, 0, player.whoAmI);
            }
        }

        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback)
        {
            JustShooted = true;
            if (CEChargeWeapon.TryConsume(player, Item))
            {
                //蓄势满:咆哮(锥形音波,短程高伤 + 击退 + 虚空触)
                int p = Projectile.NewProjectile(source, position + velocity.SafeNormalize(Vector2.UnitX) * 30f, velocity.SafeNormalize(Vector2.UnitX) * 0.02f,
                    ModContent.ProjectileType<CurseRoarWave>(), damage * 4, 10f, player.whoAmI);
                if (p.WithinBounds(Main.maxProjectiles))
                {
                    CEChargeWeapon.Empower(p);
                }
                //咆哮后座:向后一顿
                player.velocity -= velocity.SafeNormalize(Vector2.UnitX) * 5f;
                //本轮点射余下弹头取消
                player.itemTime = player.itemTimeMax;
                player.itemAnimation = Math.Min(player.itemAnimation, 2);
                return false;
            }
            Projectile.NewProjectile(source, position + velocity.SafeNormalize(Vector2.UnitX) * 42f,
                velocity.RotatedByRandom(0.03f), type, damage, knockback, player.whoAmI);
            return false;
        }
    }

    /// <summary>手持枪体(镜像 HowlingCannonHeld):握点取枪身后 1/3,射击后座位移。</summary>
    public class CurseRoarHeld : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Content/Items/Weapons/VoidInvasion/CurseRoar";

        public override bool? CanHitNPC(NPC target) => false;

        public override void SetDefaults()
        {
            Projectile.FriendlySetDefaults(DamageClass.Ranged, false, -1);
            Projectile.width = Projectile.height = 6;
        }

        public float heldOffset = 0;

        public override void AI()
        {
            Player player = Projectile.GetOwner();
            if (player.dead)
            {
                Projectile.Kill();
                return;
            }
            if (player.HeldItem.ModItem is CurseRoar gun)
            {
                Projectile.timeLeft = 2;
                if (gun.JustShooted)
                {
                    gun.JustShooted = false;
                    CEUtils.PlaySound("GunShotSmall", Main.rand.NextFloat(0.7f, 0.9f), Projectile.Center, 5, 0.5f);
                    heldOffset += -10;
                }
                player.Entropy().MouseWorldListener = true;
                Projectile.Center = player.GetDrawCenter();
                Projectile.rotation = (player.Entropy().MouseWorld - Projectile.Center).ToRotation();
                player.heldProj = Projectile.whoAmI;
                player.SetHandRot(Projectile.rotation);
            }
            heldOffset *= 0.86f;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = Projectile.GetTexture();
            int dir = Projectile.rotation.ToRotationVector2().X > 0 ? 1 : -1;
            //握点取枪身后 1/3(§5.4;源图 280 宽,枪口朝右)
            Vector2 origin = new Vector2(tex.Width / 3f, tex.Height / 2f);
            SpriteEffects effect = dir > 0 ? SpriteEffects.None : SpriteEffects.FlipVertically;
            Main.EntitySpriteDraw(tex, Projectile.Center - Main.screenPosition + new Vector2(heldOffset, 0).RotatedBy(Projectile.rotation),
                null, lightColor, Projectile.rotation, origin, Projectile.scale * 0.8f, effect);
            return false;
        }
    }

    /// <summary>虚空弹:三连点射弹头,紫色能量球视觉(白贴图 + 加算叠层)。</summary>
    public class CurseVoidBullet : ModProjectile
    {
        public override string Texture => CEUtils.WhiteTexPath;

        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light")]
        private static Asset<Texture2D> glowTex;

        public List<Vector2> oldPos = new List<Vector2>();

        public override void SetDefaults()
        {
            Projectile.width = 14;
            Projectile.height = 14;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.tileCollide = true;
            Projectile.light = 0.3f;
            Projectile.timeLeft = 480;
            Projectile.penetrate = 1;
            Projectile.MaxUpdates = 4;
            Projectile.DamageType = DamageClass.Ranged;
        }

        public override void AI()
        {
            oldPos.Add(Projectile.Center);
            if (oldPos.Count > 14)
            {
                oldPos.RemoveAt(0);
            }
            Projectile.rotation = Projectile.velocity.ToRotation();
            Lighting.AddLight(Projectile.Center, 0.4f, 0.12f, 0.6f);
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
            for (int i = 0; i < 5; i++)
            {
                var v = PRTLoader.NewParticle<PRT_Void>(Projectile.Center,
                    CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(1f, 4f), Color.White, 0.7f);
                v.Opacity = 0.45f;
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Main.spriteBatch.UseAdditive();
            Texture2D glow = glowTex.Value;
            for (int i = 0; i < oldPos.Count; i++)
            {
                float c = (i + 1f) / oldPos.Count;
                DrawBall(glow, oldPos[i], 0.34f * c, 0.7f * c);
            }
            DrawBall(glow, Projectile.Center, 0.38f, 1f);
            CEUtils.ReSetToEndShader();
            return false;
        }

        private void DrawBall(Texture2D glow, Vector2 pos, float size, float alpha)
        {
            Vector2 stretch = new Vector2(1f + Projectile.velocity.Length() * 0.02f, 1f) * size;
            Main.spriteBatch.Draw(glow, pos - Main.screenPosition, null, new Color(120, 40, 200) * alpha,
                Projectile.rotation, glow.Size() / 2, stretch * 1.5f, SpriteEffects.None, 0);
            Main.spriteBatch.Draw(glow, pos - Main.screenPosition, null, new Color(220, 150, 255) * alpha,
                Projectile.rotation, glow.Size() / 2, stretch, SpriteEffects.None, 0);
        }
    }

    /// <summary>
    /// 咆哮音波:锥形短程判定(半角约 0.5rad,半径 60→360 随 14t 膨胀),高伤 + 大击退 + 虚空触;
    /// 方向借初速度通道原生同步。视觉 = 加算音波环序列 + 虚空粒子喷发。
    /// </summary>
    public class CurseRoarWave : ModProjectile
    {
        public override string Texture => CEUtils.WhiteTexPath;

        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light")]
        private static Asset<Texture2D> glowTex;

        public const int Life = 14;
        public const float MaxReach = 360f;
        public const float HalfAngle = 0.5f;

        private float Reach => 60f + (MaxReach - 60f) * (1f - Projectile.timeLeft / (float)Life);

        public override void SetDefaults()
        {
            Projectile.FriendlySetDefaults(DamageClass.Ranged, false, -1);
            Projectile.width = 24;
            Projectile.height = 24;
            Projectile.timeLeft = Life;
            Projectile.localNPCHitCooldown = -1;
            Projectile.knockBack = 10f;
        }

        public override bool ShouldUpdatePosition() => false;

        public override void AI()
        {
            if (Projectile.localAI[0] == 0)
            {
                Projectile.localAI[0] = 1;
                Projectile.rotation = Projectile.velocity.ToRotation();
                Projectile.velocity = Vector2.Zero;
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.Roar with { Volume = 0.9f, Pitch = 0.35f }, Projectile.Center);
                    SoundEngine.PlaySound(SoundID.Item119 with { Volume = 0.8f, Pitch = -0.4f }, Projectile.Center);
                    CEUtils.SetShake(Projectile.Center, 6);
                    //锥形粒子喷发
                    for (int i = 0; i < 26; i++)
                    {
                        float ang = Projectile.rotation + Main.rand.NextFloat(-HalfAngle, HalfAngle);
                        var v = PRTLoader.NewParticle<PRT_Void>(Projectile.Center,
                            ang.ToRotationVector2() * Main.rand.NextFloat(6f, 22f), Color.White, Main.rand.NextFloat(0.8f, 1.3f));
                        v.Opacity = Main.rand.NextFloat(0.35f, 0.6f);
                    }
                }
            }
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            //锥形判定:圆心距 + 方位角双条件
            Vector2 toTarget = targetHitbox.Center.ToVector2() - Projectile.Center;
            float dist = toTarget.Length();
            if (dist > Reach + Math.Max(targetHitbox.Width, targetHitbox.Height) * 0.5f)
            {
                return false;
            }
            if (dist < 40f)
            {
                return true;
            }
            float delta = Math.Abs(MathHelper.WrapAngle(toTarget.ToRotation() - Projectile.rotation));
            return delta <= HalfAngle + 0.12f;
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            EGlobalNPC.AddVoidTouch(target, 240, 4, 600, 20);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            //音波环序列:沿锥轴排布的压扁弧环,随寿命外推渐隐
            Main.spriteBatch.UseAdditive();
            Texture2D glow = glowTex.Value;
            float p = 1f - Projectile.timeLeft / (float)Life;
            Vector2 dir = Projectile.rotation.ToRotationVector2();
            for (int i = 0; i < 4; i++)
            {
                float ringP = MathHelper.Clamp(p * 1.3f - i * 0.12f, 0f, 1f);
                if (ringP <= 0f)
                {
                    continue;
                }
                float r = 60f + (MaxReach - 70f) * ringP;
                Vector2 pos = Projectile.Center + dir * r;
                float alpha = (1f - ringP) * 0.8f;
                Main.spriteBatch.Draw(glow, pos - Main.screenPosition, null, new Color(150, 60, 230) * alpha,
                    Projectile.rotation, glow.Size() / 2, new Vector2(0.45f, 1.5f + ringP * 2.2f) * 0.8f, SpriteEffects.None, 0);
                Main.spriteBatch.Draw(glow, pos - Main.screenPosition, null, new Color(235, 180, 255) * (alpha * 0.7f),
                    Projectile.rotation, glow.Size() / 2, new Vector2(0.3f, 1.1f + ringP * 1.8f) * 0.8f, SpriteEffects.None, 0);
            }
            CEUtils.ReSetToEndShader();
            return false;
        }
    }
}
