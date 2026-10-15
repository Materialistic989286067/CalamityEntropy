using CalamityEntropy.Content.Buffs;
using CalamityEntropy.Content.NPCs.VoidDestroyer;
using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Projectiles.VoidDestroyer
{
    /// <summary>
    /// 收缩环门的一环:以 Center 为心、带一个缺口的连续能量环。先在起始半径停 GatePreviewFrames 帧亮出缺口(无判定),
    /// 再匀速收到 GateEndRadius 消散。ai[0] 缺口中心角,ai[1] 起始半径。
    /// 判定与绘制共用同一组几何(半径、环带半宽、缺口半角):画出来的缺口就是安全口
    /// </summary>
    public class VDGateBolt : VDHostileProjectile
    {
        public override string Texture => "CalamityEntropy/Assets/Extra/Empty";
        public override int DebuffType => ModContent.BuffType<VoidFire>();
        public override int DefaultTimeLeft => VDDirector.GatePreviewFrames + VDDirector.GateCloseFrames;

        public static readonly Color RingColor = new Color(170, 110, 255);

        public float GapAngle => Projectile.ai[0];
        public float StartRadius => Projectile.ai[1] > 0f ? Projectile.ai[1] : VDDirector.GateStartRadius;
        public int Age => (int)Projectile.localAI[1];
        public bool Closing => Age > VDDirector.GatePreviewFrames;
        private static float GapHalf => MathHelper.ToRadians(VDDirector.GateGapHalfDeg);

        /// <summary>当前半径:预告期停在起始半径,之后匀速收拢</summary>
        public float Radius {
            get {
                float t = MathHelper.Clamp((Age - VDDirector.GatePreviewFrames) / (float)VDDirector.GateCloseFrames, 0f, 1f);
                return MathHelper.Lerp(StartRadius, VDDirector.GateEndRadius, t);
            }
        }

        public override void SetStaticDefaults() {
            ProjectileID.Sets.DrawScreenCheckFluff[Type] = 1600;
        }

        public override void SetExtraDefaults() {
            Projectile.width = 20;
            Projectile.height = 20;
        }

        public override bool ShouldUpdatePosition() => false;

        public override void AI() {
            if (Projectile.localAI[0] == 0f) {
                Projectile.localAI[0] = 1f;
                Projectile.timeLeft = DefaultTimeLeft;
                if (!Main.dedServ) {
                    CEUtils.PlaySound("vbapear", 1.2f, Projectile.Center, 4, 0.55f);
                }
            }
            Projectile.localAI[1]++;
            if (Age == VDDirector.GatePreviewFrames + 1 && !Main.dedServ) {
                CEUtils.PlaySound("VoidAnticipation", 1.5f, Projectile.Center, 4, 0.5f);
            }
            Lighting.AddLight(Projectile.Center, RingColor.ToVector3() * 0.5f);
        }

        /// <summary>点是否压在环带上(不在缺口里)</summary>
        private bool OnRing(Vector2 p, float radius) {
            Vector2 rel = p - Projectile.Center;
            if (Math.Abs(rel.Length() - radius) > VDDirector.GateThickness) {
                return false;
            }
            return Math.Abs(MathHelper.WrapAngle(rel.ToRotation() - GapAngle)) > GapHalf;
        }

        /// <summary>玩家盒按 3×3 取样:环带全宽 28px,比玩家盒窄边还细,只测中心会漏</summary>
        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox) {
            if (!Closing) {
                return false;
            }
            float radius = Radius;
            for (int ix = 0; ix < 3; ix++) {
                for (int iy = 0; iy < 3; iy++) {
                    Vector2 p = new Vector2(targetHitbox.X + targetHitbox.Width * ix * 0.5f, targetHitbox.Y + targetHitbox.Height * iy * 0.5f);
                    if (OnRing(p, radius)) {
                        return true;
                    }
                }
            }
            return false;
        }

        public override bool PreDraw(ref Color lightColor) {
            float radius = Radius;
            float appear = MathHelper.Clamp(Age / 8f, 0f, 1f);
            float vanish = MathHelper.Clamp(Projectile.timeLeft / 6f, 0f, 1f);
            float a = appear * vanish;
            if (a <= 0.01f) {
                return false;
            }
            float heat = Closing ? 1f : 0.35f + 0.25f * MathF.Sin(Age * 0.8f);
            Vector2 c = Projectile.Center;
            //弧段按约 22px 一段铺,缺口两侧各留到缺口边
            float arc = MathHelper.TwoPi - GapHalf * 2f;
            int segs = Math.Max(16, (int)(radius * arc / 22f));
            float start = GapAngle + GapHalf;
            SpriteBatch sb = Main.spriteBatch;

            //暗底:真透明度,亮背景上也剩一道环
            if (Closing) {
                for (int i = 0; i < segs; i++) {
                    CEUtils.drawLine(ArcPoint(c, start + arc * i / segs, radius), ArcPoint(c, start + arc * (i + 1) / segs, radius), VDVoidBolt.RimDark * (0.7f * a), 12f, 1);
                }
            }
            //发光两遍各一种贴图,延迟批次里各自合成一批(立即模式每段都是一次绘制调用)
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            for (int i = 0; i < segs; i++) {
                CEUtils.drawLineBetter(ArcPoint(c, start + arc * i / segs, radius), ArcPoint(c, start + arc * (i + 1) / segs, radius), RingColor * (0.45f * heat * a), 30f, 1);
            }
            Color core = Color.Lerp(RingColor, Color.White, 0.6f) * (0.85f * heat * a);
            for (int i = 0; i < segs; i++) {
                CEUtils.drawLine(ArcPoint(c, start + arc * i / segs, radius), ArcPoint(c, start + arc * (i + 1) / segs, radius), core, Closing ? 4f : 2f, 1);
            }
            //缺口两端的亮帽:安全口的门框,预告期更亮
            Texture2D glow = CEUtils.getExtraTex("Glow");
            Color cap = Color.Lerp(VDDirector.RimZoneCyan, Color.White, 0.4f) * (Closing ? 0.9f : 1f) * a;
            for (int s = -1; s <= 1; s += 2) {
                Vector2 outward = (GapAngle + s * GapHalf).ToRotationVector2();
                Vector2 edge = c + outward * radius;
                sb.Draw(glow, edge - Main.screenPosition, null, cap, 0f, glow.Size() / 2f, 0.22f, SpriteEffects.None, 0f);
                CEUtils.drawLine(edge - outward * 20f, edge + outward * 20f, cap, 3f);
            }
            CEUtils.ReSetToEndShader();
            return false;
        }

        private static Vector2 ArcPoint(Vector2 center, float angle, float radius) => center + angle.ToRotationVector2() * radius;
    }
}
