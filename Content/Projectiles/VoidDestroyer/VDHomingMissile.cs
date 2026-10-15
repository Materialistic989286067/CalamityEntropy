using CalamityEntropy.Content.NPCs.VoidDestroyer;
using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using CalamityEntropy.Content.Particles;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace CalamityEntropy.Content.Projectiles.VoidDestroyer
{
    /// <summary>
    /// 径向飞出约 15 格后转头追踪,只追 MissileHomingFrames 帧就改直飞(躲过一次就甩掉);命中即炸。
    /// ai[0] 目标玩家,localAI[0] 累计飞行距离,localAI[2] 已追踪帧数;命中 312
    /// </summary>
    public class VDHomingMissile : VDDepthProjectile
    {
        public const float RetargetDistance = 15f * 16f;
        public const float MaxSpeed = 22f;
        /// <summary>贴图 24×44,画大 1.15 倍</summary>
        public const float BodyScale = 1.15f;
        public static readonly Color GlowColor = new Color(210, 90, 255);

        public override int DefaultTimeLeft => 240;

        public override void SetStaticDefaults() {
            ProjectileID.Sets.TrailCacheLength[Type] = 8;
            ProjectileID.Sets.TrailingMode[Type] = 2;
        }

        public override void SetExtraDefaults() {
            Projectile.width = 20;
            Projectile.height = 20;
        }

        protected override void DepthAI() {
            Projectile.localAI[1]++;
            //全端由速度积分得到飞行距离,转头时机一致
            Projectile.localAI[0] += Projectile.velocity.Length();
            if (Projectile.localAI[0] > RetargetDistance && Projectile.localAI[2] < VDDirector.MissileHomingFrames) {
                Projectile.localAI[2]++;
                Player target = TargetPlayer(0);
                if (target != null) {
                    float cur = Projectile.velocity.ToRotation();
                    float want = (target.Center - Projectile.Center).ToRotation();
                    float diff = MathHelper.WrapAngle(want - cur);
                    float turn = MathHelper.Clamp(diff, -0.105f, 0.105f);
                    float speed = Math.Min(Projectile.velocity.Length() * 1.03f + 0.1f, MaxSpeed);
                    Projectile.velocity = (cur + turn).ToRotationVector2() * speed;
                }
            }
            else if (Projectile.localAI[2] >= VDDirector.MissileHomingFrames) {
                //追踪段结束:保持速度直飞
                Projectile.velocity = Projectile.velocity.SafeNormalize(Vector2.UnitY) * Math.Min(Projectile.velocity.Length() * 1.01f, MaxSpeed);
            }
            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;
            Lighting.AddLight(Projectile.Center, GlowColor.ToVector3() * 0.4f);

            if (Main.dedServ) {
                return;
            }
            Vector2 back = -Projectile.velocity.SafeNormalize(Vector2.UnitY);
            Dust d = Dust.NewDustPerfect(Projectile.Center + back * 18f, DustID.Smoke, back * Main.rand.NextFloat(1f, 3f), 120, default, Main.rand.NextFloat(1f, 1.6f));
            d.noGravity = true;
            if (Main.rand.NextBool(2)) {
                Vector2 v = back * Main.rand.NextFloat(2f, 5f) + CEUtils.randomPointInCircle(1f);
                VDVfx.Spark(Projectile.Center + back * 16f, v, GlowColor, Main.rand.NextFloat(0.5f, 0.9f), 0.9f, 16);
            }
        }

        public override void OnHitPlayer(Player target, Player.HurtInfo info) {
            base.OnHitPlayer(target, info);
            Projectile.Kill();
        }

        public override void OnKill(int timeLeft) {
            if (Main.dedServ) {
                return;
            }
            CEUtils.PlaySound("VoidBomb", Main.rand.NextFloat(1.1f, 1.3f), Projectile.Center, 6, 0.45f);
            PRTLoader.NewParticle<PRT_EXPLOSION>(Projectile.Center, Vector2.Zero, Color.White, 0.4f)
                .Configure(1f, true, PRTDrawModeEnum.AdditiveBlend, 0f, 24);
            for (int i = 0; i < 10; i++) {
                Vector2 v = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(3f, 8f);
                VDVfx.Spark(Projectile.Center, v, GlowColor, Main.rand.NextFloat(0.5f, 1f), 1f, 22, gravity: true);
            }
        }

        protected override void DrawDepth(SpriteBatch spriteBatch) {
            Texture2D tex = TextureAssets.Projectile[Type].Value;
            Texture2D glowTex = CEUtils.getExtraTex("Glow");
            Vector2 origin = tex.Size() / 2f;
            Vector2 drawPos = Projectile.Center - Main.screenPosition;
            float scale = Projectile.scale * BodyScale;
            Vector2 back = -Projectile.velocity.SafeNormalize(Vector2.UnitY);

            spriteBatch.UseAdditive();
            for (int i = 1; i < Projectile.oldPos.Length; i++) {
                if (Projectile.oldPos[i] == Vector2.Zero) {
                    continue;
                }
                float k = i / (float)Projectile.oldPos.Length;
                Vector2 pos = Projectile.oldPos[i] + Projectile.Size / 2f - Main.screenPosition;
                spriteBatch.Draw(tex, pos, null, GlowColor * ((1f - k) * 0.35f), Projectile.oldRot[i], origin, scale * (1f - k * 0.3f), SpriteEffects.None, 0f);
            }
            //尾焰:追踪段里更亮,告诉玩家它还在拐
            float flame = Projectile.localAI[2] > 0f && Projectile.localAI[2] < VDDirector.MissileHomingFrames ? 1f : 0.7f;
            spriteBatch.Draw(glowTex, drawPos + back * 20f * scale, null, new Color(255, 140, 255) * (0.85f * flame), 0f, glowTex.Size() / 2f, 0.18f * scale, SpriteEffects.None, 0f);
            spriteBatch.Draw(glowTex, drawPos, null, GlowColor * 0.35f, 0f, glowTex.Size() / 2f, 0.3f * scale, SpriteEffects.None, 0f);
            CEUtils.ReSetToEndShader();

            spriteBatch.Draw(tex, drawPos, null, VDVoidBolt.RimDark * 0.8f, Projectile.rotation, origin, scale * 1.18f, SpriteEffects.None, 0f);
            spriteBatch.Draw(tex, drawPos, null, Color.White, Projectile.rotation, origin, scale, SpriteEffects.None, 0f);
        }
    }
}
