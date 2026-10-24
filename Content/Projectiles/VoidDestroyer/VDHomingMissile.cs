using CalamityEntropy.Content.Items.Weapons.VoidDestroyer;
using CalamityEntropy.Content.NPCs.VoidDestroyer;
using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace CalamityEntropy.Content.Projectiles.VoidDestroyer
{
    /// <summary>
    /// 径向飞出约 8 格后转头追踪,只追 MissileHomingFrames 帧就改直飞(躲过一次就甩掉),全程匀速;命中即炸。
    /// ai[0] 目标玩家,localAI[0] 累计飞行距离,localAI[2] 已追踪帧数;弹幕档伤害
    /// </summary>
    public class VDHomingMissile : VDDepthProjectile
    {
        /// <summary>转头前的直飞距离 8 格:弹在舰体旁就开始弯向玩家,不会一出手就压到脸上</summary>
        public const float RetargetDistance = 8f * 16f;
        /// <summary>贴图 24×44,画大 1.15 倍</summary>
        public const float BodyScale = 1.15f;
        public static readonly Color GlowColor = new Color(210, 90, 255);

        public override int DefaultTimeLeft => 240;

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
                    Projectile.velocity = (cur + turn).ToRotationVector2() * Projectile.velocity.Length();
                }
            }
            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;
            Lighting.AddLight(Projectile.Center, GlowColor.ToVector3() * 0.4f);

            if (Main.dedServ || !Main.rand.NextBool(3)) {
                return;
            }
            Vector2 back = -Projectile.velocity.SafeNormalize(Vector2.UnitY);
            Vector2 v = back * Main.rand.NextFloat(2f, 5f) + CEUtils.randomPointInCircle(1f);
            VDVfx.Spark(Projectile.Center + back * 18f, v, ExhaustHot, Main.rand.NextFloat(0.5f, 0.85f), 0.9f, 14);
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
            VDVfx.Explosion(Projectile.Center, 0.45f, 22, 0.55f);
            for (int i = 0; i < 8; i++) {
                Vector2 v = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(3f, 8f);
                VDVfx.Spark(Projectile.Center, v, i % 2 == 0 ? ExhaustHot : GlowColor, Main.rand.NextFloat(0.5f, 1f), 1f, 22, gravity: true);
            }
        }

        /// <summary>尾焰热色:白里带粉,天幕上读得出的亮度</summary>
        public static readonly Color ExhaustHot = new Color(255, 205, 250);
        private static readonly Vector2[] exhaust = new Vector2[TrailLength];

        /// <summary>尾焰带 → 喷口光 → 警示描边 → 弹体;追踪段尾焰更长更亮</summary>
        protected override void DrawDepth(SpriteBatch spriteBatch) {
            Texture2D tex = TextureAssets.Projectile[Type].Value;
            Texture2D glowTex = CEUtils.getExtraTex("Glow");
            Vector2 origin = tex.Size() / 2f;
            Vector2 drawPos = Projectile.Center - Main.screenPosition;
            float scale = Projectile.scale * BodyScale;
            Vector2 back = -Projectile.velocity.SafeNormalize(Vector2.UnitY);
            float flame = Projectile.localAI[2] > 0f && Projectile.localAI[2] < VDDirector.MissileHomingFrames ? 1f : 0.7f;
            Vector2 nozzle = back * 20f * scale;

            spriteBatch.UseAdditive();
            if (trailCount > 2) {
                for (int i = 0; i < trailCount; i++) {
                    exhaust[i] = (i == 0 ? Projectile.Center : trailPos[i]) + nozzle;
                }
                var pts = new ArraySegment<Vector2>(exhaust, 0, trailCount);
                VDWeaponFx.Ribbon(pts, t => (18f - 12f * t) * flame, t => Color.Lerp(VDVfx.VoidPink, GlowColor, t) * ((1f - t) * 0.55f * flame));
                VDWeaponFx.Ribbon(pts, t => (6f - 4f * t) * flame, t => Color.Lerp(Color.White, ExhaustHot, t) * ((1f - t) * (1f - t) * 0.95f));
            }
            spriteBatch.Draw(glowTex, drawPos + nozzle, null, VDVfx.VoidPink * (0.8f * flame), 0f, glowTex.Size() / 2f, 0.16f * scale, SpriteEffects.None, 0f);
            spriteBatch.Draw(glowTex, drawPos + nozzle, null, Color.White * (0.75f * flame), 0f, glowTex.Size() / 2f, 0.06f * scale, SpriteEffects.None, 0f);
            CEUtils.ReSetToEndShader();

            VDVfx.DrawHazardRim(tex, drawPos, null, Projectile.rotation, origin, scale, 1f);
            spriteBatch.Draw(tex, drawPos, null, Color.White, Projectile.rotation, origin, scale, SpriteEffects.None, 0f);
        }
    }
}
