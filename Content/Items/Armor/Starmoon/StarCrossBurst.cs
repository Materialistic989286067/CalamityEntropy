using CalamityEntropy.Content.Particles.CalamityPorts;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Armor.Starmoon
{
    /// <summary>
    /// 十字星爆,星月法师套魔法暴击时生成在目标中心
    /// 横竖两道光臂在 HitStart~HitEnd 帧内对每个敌人判定一次,消失时治疗拥有者
    /// ai[1] 十字的倾角(弧度),由生成方随机后随弹幕包同步
    /// </summary>
    public class StarCrossBurst : ModProjectile
    {
        public const int Lifetime = 40;
        public const int HitStart = 3;
        public const int HitEnd = 12;
        public const float ArmLength = 240f;
        public const float ArmWidth = 30f;
        public const int HealAmount = 8;

        private static readonly Color Outer = new(110, 140, 255);
        private static readonly Color Violet = new(180, 130, 255);

        public override string Texture => CEUtils.WhiteTexPath;

        private float Age => Lifetime - Projectile.timeLeft;

        public override void SetDefaults() {
            Projectile.width = 48;
            Projectile.height = 48;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Magic;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.ignoreWater = true;
            Projectile.timeLeft = Lifetime;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = -1;
        }

        public override void AI() {
            if (Projectile.localAI[0] == 0f) {
                Projectile.localAI[0] = 1f;
                if (!Main.dedServ) {
                    SoundEngine.PlaySound(SoundID.Item105 with { Volume = 0.6f, Pitch = 0.2f, MaxInstances = 3 }, Projectile.Center);
                    PRTLoader.NewParticle<PRT_PulseRing>(Projectile.Center, Vector2.Zero, Outer, 0.1f).Configure(2.4f, 18);
                    for (int i = 0; i < 6; i++) {
                        Vector2 offset = Main.rand.NextVector2Circular(60f, 60f);
                        PRTLoader.NewParticle<PRT_SparkleCal>(Projectile.Center + offset, offset * 0.03f, Color.White, Main.rand.NextFloat(1f, 1.6f))
                            .Configure(Main.rand.NextBool() ? Outer : Violet, Main.rand.Next(14, 22), Main.rand.NextFloat(-0.1f, 0.1f), 2f);
                    }
                }
            }
            Lighting.AddLight(Projectile.Center, new Vector3(0.45f, 0.55f, 1f) * Intensity() * 1.5f);
        }

        /// <summary>0-1,前 5 帧冲到顶,10 帧后渐隐</summary>
        private float Intensity() {
            float rise = Utils.GetLerpValue(0f, 5f, Age, true);
            float fade = 1f - Utils.GetLerpValue(10f, Lifetime, Age, true);
            return rise * fade * fade;
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox) {
            if (Age < HitStart || Age > HitEnd) {
                return false;
            }
            float point = 0f;
            for (int i = 0; i < 2; i++) {
                Vector2 arm = (Projectile.ai[1] + i * MathHelper.PiOver2).ToRotationVector2() * ArmLength;
                if (Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), Projectile.Center - arm, Projectile.Center + arm, ArmWidth, ref point)) {
                    return true;
                }
            }
            return false;
        }

        public override void OnKill(int timeLeft) {
            if (Projectile.owner == Main.myPlayer) {
                Main.player[Projectile.owner].Heal(HealAmount);
            }
        }

        public override bool PreDraw(ref Color lightColor) {
            float power = Intensity();
            if (power <= 0.01f) {
                return false;
            }
            Vector2 center = Projectile.Center - Main.screenPosition;
            Texture2D ray = CEUtils.getExtraTex("Ray");
            Texture2D star = CEUtils.getExtraTex("StarTexture");
            Texture2D soft = CEUtils.getExtraTex("Glow");
            float rot = Projectile.ai[1];
            //光臂先伸到全长再收细
            float reach = MathHelper.Lerp(0.35f, 1f, Utils.GetLerpValue(0f, 6f, Age, true));
            float thin = MathHelper.Lerp(1f, 0.45f, Utils.GetLerpValue(6f, Lifetime, Age, true));
            float length = ArmLength * 2f / ray.Width * reach;

            Main.EntitySpriteDraw(soft, center, null, (Outer * (0.55f * power)) with { A = 0 }, 0f, soft.Size() / 2f, 0.7f, SpriteEffects.None, 0);
            for (int i = 0; i < 2; i++) {
                float r = rot + i * MathHelper.PiOver2;
                Main.EntitySpriteDraw(ray, center, null, (Outer * power) with { A = 0 }, r, ray.Size() / 2f, new Vector2(length, 0.9f * thin), SpriteEffects.None, 0);
                Main.EntitySpriteDraw(ray, center, null, (Color.White * power) with { A = 0 }, r, ray.Size() / 2f, new Vector2(length * 0.85f, 0.35f * thin), SpriteEffects.None, 0);
                float d = r + MathHelper.PiOver4;
                Main.EntitySpriteDraw(ray, center, null, (Violet * (0.6f * power)) with { A = 0 }, d, ray.Size() / 2f, new Vector2(length * 0.45f, 0.5f * thin), SpriteEffects.None, 0);
            }
            float spin = Main.GlobalTimeWrappedHourly * 2f;
            Main.EntitySpriteDraw(star, center, null, (Color.White * power) with { A = 0 }, rot + spin * 0.2f, star.Size() / 2f, 0.38f * (0.8f + 0.2f * power), SpriteEffects.None, 0);
            Main.EntitySpriteDraw(star, center, null, (Violet * (0.7f * power)) with { A = 0 }, rot + MathHelper.PiOver4, star.Size() / 2f, 0.24f, SpriteEffects.None, 0);
            return false;
        }
    }
}
