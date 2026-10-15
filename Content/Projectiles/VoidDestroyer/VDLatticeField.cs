using CalamityEntropy.Content.NPCs.VoidDestroyer;
using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Projectiles.VoidDestroyer
{
    /// <summary>
    /// 井字网:以 Center 为心的横竖各 LatticeLines 道射线,ai[2]=1 整网错半格。预警 ai[0] 帧细线 + 两端无人机,
    /// 到时同帧打下,前 LatticeStrikeFrames 帧判定。判定与绘制共用 LineCoord,画在哪就打在哪;命中 372 + 带电 3 秒
    /// </summary>
    public class VDLatticeField : VDHostileProjectile
    {
        public const int FadeFrames = 14;
        public const int AppearFrames = 8;

        public override string Texture => "CalamityEntropy/Assets/Extra/Empty";
        public override int DebuffType => BuffID.Electrified;
        public override int DefaultTimeLeft => 120;

        public int WarnTime => (int)Math.Max(Projectile.ai[0], 10f);
        public bool HalfShift => Projectile.ai[2] > 0.5f;
        public int Age => (int)Projectile.localAI[1];
        public bool Striking => Age >= WarnTime && Age < WarnTime + VDDirector.LatticeStrikeFrames;

        public override void SetStaticDefaults() {
            ProjectileID.Sets.DrawScreenCheckFluff[Type] = 2200;
        }

        public override void SetExtraDefaults() {
            Projectile.width = 16;
            Projectile.height = 16;
        }

        public override bool ShouldUpdatePosition() => false;

        /// <summary>第 i 道线相对网心的坐标(竖线是 x,横线是 y)</summary>
        public float LineCoord(int i) {
            float shift = HalfShift ? VDDirector.LatticeSpacing * 0.5f : 0f;
            return (i - (VDDirector.LatticeLines - 1) * 0.5f) * VDDirector.LatticeSpacing + shift;
        }

        public override void AI() {
            if (Projectile.localAI[0] == 0f) {
                Projectile.localAI[0] = 1f;
                Projectile.timeLeft = WarnTime + VDDirector.LatticeStrikeFrames + FadeFrames;
                if (!Main.dedServ) {
                    CEUtils.PlaySound("vbapear", 1.25f, Projectile.Center, 4, 0.6f);
                }
            }
            Projectile.localAI[1]++;
            Projectile.velocity = Vector2.Zero;

            if (Age == WarnTime && !Main.dedServ) {
                //整张网同帧打下:一次音效 + 一次震屏,交点各一簇火花
                CEUtils.PlaySound("void_laser", 1.35f, Projectile.Center, 4, 0.8f);
                CEUtils.SetShake(Projectile.Center, 5f, 1600f);
                for (int i = 0; i < VDDirector.LatticeLines; i++) {
                    for (int j = 0; j < VDDirector.LatticeLines; j++) {
                        Vector2 p = Projectile.Center + new Vector2(LineCoord(i), LineCoord(j));
                        Vector2 v = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(2f, 5f);
                        VDVfx.Spark(p, v, VDSporeDrone.WarnColor, Main.rand.NextFloat(0.5f, 0.9f), 1f, 18, gravity: true);
                    }
                }
            }
            if (Striking) {
                Lighting.AddLight(Projectile.Center, VDSporeLaser.BeamColor.ToVector3() * 0.9f);
            }
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox) {
            if (!Striking) {
                return false;
            }
            float half = VDDirector.LatticeBeamWidth * 0.5f;
            float len = VDDirector.LatticeHalfLength;
            Vector2 rel = targetHitbox.Center.ToVector2() - Projectile.Center;
            float hw = targetHitbox.Width * 0.5f + half;
            float hh = targetHitbox.Height * 0.5f + half;
            for (int i = 0; i < VDDirector.LatticeLines; i++) {
                float k = LineCoord(i);
                //竖线 x = k、横线 y = k,只在射线长度内
                if (Math.Abs(rel.X - k) <= hw && Math.Abs(rel.Y) <= len) {
                    return true;
                }
                if (Math.Abs(rel.Y - k) <= hh && Math.Abs(rel.X) <= len) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>出现 8 帧长起;打击后按剩余寿命收缩</summary>
        private float Presence() {
            if (Age <= WarnTime) {
                return MathHelper.Clamp(Age / (float)AppearFrames, 0f, 1f);
            }
            return MathHelper.Clamp(Projectile.timeLeft / (float)FadeFrames, 0f, 1f);
        }

        public override bool PreDraw(ref Color lightColor) {
            float presence = Presence();
            if (presence <= 0.01f) {
                return false;
            }
            Vector2 c = Projectile.Center;
            float len = VDDirector.LatticeHalfLength;
            bool warning = Age < WarnTime;

            if (warning) {
                //预警:细线随蓄力变亮变粗,末 15 帧闪;交点各一枚收紧的小环
                float charge = MathHelper.Clamp(Age / (float)WarnTime, 0f, 1f);
                float alpha = 0.2f + 0.5f * charge;
                if (WarnTime - Age < 15) {
                    alpha += 0.3f * MathF.Sin(Age * 1.2f);
                }
                alpha = MathHelper.Clamp(alpha, 0f, 1f) * presence;
                Main.spriteBatch.UseAdditive();
                for (int i = 0; i < VDDirector.LatticeLines; i++) {
                    float k = LineCoord(i);
                    CEUtils.drawLineBetter(c + new Vector2(k, -len), c + new Vector2(k, len), VDSporeDrone.WarnColor * (alpha * 0.6f), 4f + 8f * charge);
                    CEUtils.drawLineBetter(c + new Vector2(-len, k), c + new Vector2(len, k), VDSporeDrone.WarnColor * (alpha * 0.6f), 4f + 8f * charge);
                    CEUtils.drawLine(c + new Vector2(k, -len), c + new Vector2(k, len), Color.White * (0.3f * charge * presence), 1.5f);
                    CEUtils.drawLine(c + new Vector2(-len, k), c + new Vector2(len, k), Color.White * (0.3f * charge * presence), 1.5f);
                }
                CEUtils.ReSetToEndShader();
            }
            else {
                //打击:每道一根直射线,宽度随剩余寿命收干
                float hot = Striking ? 1f : presence;
                float w = VDDirector.LatticeBeamWidth * (0.6f + 0.4f * hot);
                for (int i = 0; i < VDDirector.LatticeLines; i++) {
                    float k = LineCoord(i);
                    VDBeamDraw.Draw(c + new Vector2(k, -len), Vector2.UnitY, len * 2f, w, VDSporeLaser.BeamColor, Color.White, hot, 1f, i * 0.37f);
                    VDBeamDraw.Draw(c + new Vector2(-len, k), Vector2.UnitX, len * 2f, w, VDSporeLaser.BeamColor, Color.White, hot, 1f, i * 0.53f + 0.2f);
                }
            }
            DrawDrones(c, len, presence);
            return false;
        }

        /// <summary>每道线两端各一架孢子无人机(射线端点):网是它们拉出来的</summary>
        private void DrawDrones(Vector2 c, float len, float presence) {
            Texture2D tex = TextureAssets.Projectile[ModContent.ProjectileType<VDSporeDrone>()].Value;
            Texture2D glow = CEUtils.getExtraTex("Glow");
            float wobble = MathF.Sin(Age * 0.15f) * 0.08f;
            float edge = len;
            Main.spriteBatch.UseAdditive();
            for (int i = 0; i < VDDirector.LatticeLines; i++) {
                float k = LineCoord(i);
                foreach (Vector2 p in DronePoints(c, k, edge)) {
                    Main.spriteBatch.Draw(glow, p - Main.screenPosition, null, VDSporeDrone.WarnColor * (0.5f * presence), 0f, glow.Size() / 2f, 0.2f * presence, SpriteEffects.None, 0f);
                }
            }
            CEUtils.ReSetToEndShader();
            for (int i = 0; i < VDDirector.LatticeLines; i++) {
                float k = LineCoord(i);
                foreach (Vector2 p in DronePoints(c, k, edge)) {
                    Main.spriteBatch.Draw(tex, p - Main.screenPosition, null, Color.White * presence, wobble, tex.Size() / 2f, presence, SpriteEffects.None, 0f);
                }
            }
        }

        private static Vector2[] DronePoints(Vector2 c, float k, float edge)
            => new[] { c + new Vector2(k, -edge), c + new Vector2(k, edge), c + new Vector2(-edge, k), c + new Vector2(edge, k) };
    }
}
