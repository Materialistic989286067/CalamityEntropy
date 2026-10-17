using CalamityEntropy.Content.Buffs;
using CalamityEntropy.Content.NPCs.VoidDestroyer;
using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using CalamityEntropy.Content.Particles;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Projectiles.VoidDestroyer
{
    /// <summary>
    /// ai[0]:0 直飞,1 弧形包抄(ai[1]/ai[2] 锁定点),2 延迟转向上(ai[1] 延迟帧),3 沿初速加速,4 形状弹(乘法外扩),
    /// 5 炸弹(重力下落),6 螺旋(绕 ai[1]/ai[2] 外旋)。全部留在平面;命中 + 虚空之火 3 秒
    /// </summary>
    public class VDVoidBolt : VDDepthProjectile
    {
        public const int ModeStraight = 0;
        public const int ModeArcWrap = 1;
        public const int ModeFanRise = 2;
        public const int ModeAccelerate = 3;
        public const int ModeShapeBurst = 4;
        public const int ModeBomb = 5;
        public const int ModeSpiral = 6;
        /// <summary>螺旋弹寿命:半径从吸积盘外沿长到约 530px</summary>
        public const int SpiralLife = 150;
        /// <summary>贴图里的菱形只有 14×30,画大 1.35 倍(约 19×40)才在亮背景和满屏弹幕里读得出来;判定盒同步放到 26</summary>
        public const float BodyScale = 1.35f;

        public static readonly Color GlowColor = new Color(190, 60, 255);
        /// <summary>暗描边色:真透明度画在弹体下,亮天空上也剩一圈轮廓</summary>
        public static readonly Color RimDark = new Color(36, 8, 64);

        public int Mode => (int)Projectile.ai[0];
        /// <summary>螺旋弹的起始半径(首帧由生成位置反推)</summary>
        private float spiralRadius0;
        public override int DebuffType => ModContent.BuffType<VoidFire>();
        public override int DefaultTimeLeft => 240;

        public override void SetStaticDefaults() {
            ProjectileID.Sets.TrailCacheLength[Type] = 8;
            ProjectileID.Sets.TrailingMode[Type] = 2;
        }

        public override void SetExtraDefaults() {
            Projectile.width = 26;
            Projectile.height = 26;
        }

        protected override void DepthAI() {
            //首帧按模式定寿命(生成后改 timeLeft 不走同步,放在全端都会跑的首帧里)
            if (Projectile.localAI[0] == 0f) {
                Projectile.localAI[0] = 1f;
                switch (Mode) {
                    case ModeShapeBurst:
                        Projectile.timeLeft = 120;
                        break;
                    case ModeAccelerate:
                        Projectile.timeLeft = 180;
                        break;
                    case ModeBomb:
                        Projectile.timeLeft = 200;
                        break;
                    case ModeSpiral:
                        Projectile.timeLeft = SpiralLife;
                        //起始角与半径由生成位置相对轨道心反推(各端同一生成包,算得一样)
                        Vector2 rel = Projectile.Center - new Vector2(Projectile.ai[1], Projectile.ai[2]);
                        Projectile.localAI[2] = rel.ToRotation();
                        spiralRadius0 = rel.Length();
                        break;
                }
            }
            Projectile.localAI[1]++;
            float age = Projectile.localAI[1];

            switch (Mode) {
                case ModeArcWrap: {
                    Vector2 target = new Vector2(Projectile.ai[1], Projectile.ai[2]);
                    Vector2 toTarget = target - Projectile.Center;
                    if (age < 120f && toTarget.Length() > 60f) {
                        float cur = Projectile.velocity.ToRotation();
                        float want = toTarget.ToRotation();
                        float diff = MathHelper.WrapAngle(want - cur);
                        float turn = MathHelper.Clamp(diff, -0.038f, 0.038f);
                        float speed = Math.Min(Projectile.velocity.Length() + 0.06f, 17f);
                        Projectile.velocity = (cur + turn).ToRotationVector2() * speed;
                    }
                    break;
                }
                case ModeFanRise: {
                    if (age > Projectile.ai[1]) {
                        float cur = Projectile.velocity.ToRotation();
                        float diff = MathHelper.WrapAngle(-MathHelper.PiOver2 - cur);
                        float turn = MathHelper.Clamp(diff, -0.16f, 0.16f);
                        float speed = Projectile.velocity.Length();
                        if (Math.Abs(diff) < 0.3f) {
                            speed = Math.Min(speed * 1.05f + 0.1f, 24f);
                        }
                        Projectile.velocity = (cur + turn).ToRotationVector2() * speed;
                    }
                    break;
                }
                case ModeAccelerate: {
                    float speed = Math.Min(Projectile.velocity.Length() * 1.045f + 0.05f, 30f);
                    Projectile.velocity = Projectile.velocity.SafeNormalize(Vector2.UnitY) * speed;
                    break;
                }
                case ModeShapeBurst: {
                    //乘法加速:同一轮所有弹按相同比例外扩,形状不变
                    Projectile.velocity *= 1.012f;
                    break;
                }
                case ModeBomb: {
                    Projectile.velocity.X *= 0.99f;
                    Projectile.velocity.Y = Math.Min(Projectile.velocity.Y + VDDirector.BombGravity, VDDirector.BombMaxFall);
                    break;
                }
                case ModeSpiral: {
                    //角按角速度推进、半径按帧数长,速度按差分写回(残影与朝向跟着弧线走)
                    Vector2 c = new Vector2(Projectile.ai[1], Projectile.ai[2]);
                    float angle = Projectile.localAI[2] + VDDirector.SingSpiralAngular * age;
                    float radius = spiralRadius0 + VDDirector.SingSpiralGrowth * age;
                    Projectile.velocity = c + angle.ToRotationVector2() * radius - Projectile.Center;
                    break;
                }
            }

            Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;
            Lighting.AddLight(Projectile.Center, GlowColor.ToVector3() * 0.55f);

            if (Main.dedServ || Mode == ModeShapeBurst) {
                return;
            }
            //火焰拖尾:虚空粒子走像素管线,发光火花保底
            if (age % 2 == 0) {
                Vector2 back = -Projectile.velocity.SafeNormalize(Vector2.Zero);
                var p = PRTLoader.NewParticle<PRT_Void>(Projectile.Center + back * 12f + CEUtils.randomPointInCircle(5f), back * Main.rand.NextFloat(0.5f, 2f) + Projectile.velocity * 0.2f, Color.White, Main.rand.NextFloat(0.8f, 1.2f));
                p.Opacity = 0.7f;
                p.ad = 0.04f;
            }
            if (age % 5 == 0) {
                Vector2 v = -Projectile.velocity * 0.15f + CEUtils.randomPointInCircle(1.5f);
                VDVfx.Spark(Projectile.Center, v, GlowColor, Main.rand.NextFloat(0.5f, 0.9f), 0.9f, 18);
            }
        }

        public override void OnKill(int timeLeft) {
            if (Main.dedServ) {
                return;
            }
            for (int i = 0; i < 6; i++) {
                Vector2 v = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(2f, 6f);
                VDVfx.Spark(Projectile.Center, v, GlowColor, Main.rand.NextFloat(0.5f, 0.9f), 1f, 20, gravity: true);
            }
        }

        /// <summary>
        /// 四层:加法外晕 + 同贴图残影(拖尾与弹体同材质,横向宽度 ≥ 弹体一半)→ 警示描边(暗底 + 暖白亮边)→ 弹体 → 白热芯。
        /// 形状弹太密,不画残影;所有层按 BodyScale 放大
        /// </summary>
        protected override void DrawDepth(SpriteBatch spriteBatch) {
            Texture2D tex = TextureAssets.Projectile[Type].Value;
            Texture2D glowTex = CEUtils.getExtraTex("Glow");
            Vector2 origin = tex.Size() / 2f;
            Vector2 drawPos = Projectile.Center - Main.screenPosition;
            float scale = Projectile.scale * BodyScale;
            float fade = Mode == ModeShapeBurst ? MathHelper.Clamp(Projectile.timeLeft / 30f, 0f, 1f) : 1f;
            fade *= MathHelper.Clamp(Projectile.localAI[1] / 4f, 0f, 1f);

            spriteBatch.UseAdditive();
            spriteBatch.Draw(glowTex, drawPos, null, GlowColor * (0.5f * fade), 0f, glowTex.Size() / 2f, 0.3f * scale, SpriteEffects.None, 0f);
            if (Mode != ModeShapeBurst) {
                for (int i = 1; i < Projectile.oldPos.Length; i++) {
                    if (Projectile.oldPos[i] == Vector2.Zero) {
                        continue;
                    }
                    float k = i / (float)Projectile.oldPos.Length;
                    Vector2 pos = Projectile.oldPos[i] + Projectile.Size / 2f - Main.screenPosition;
                    spriteBatch.Draw(tex, pos, null, GlowColor * ((1f - k) * 0.45f * fade), Projectile.oldRot[i], origin, scale * (1f - k * 0.45f), SpriteEffects.None, 0f);
                }
            }
            CEUtils.ReSetToEndShader();

            VDVfx.DrawHazardRim(tex, drawPos, null, Projectile.rotation, origin, scale, fade);
            spriteBatch.Draw(tex, drawPos, null, Color.White * fade, Projectile.rotation, origin, scale, SpriteEffects.None, 0f);

            spriteBatch.UseAdditive();
            spriteBatch.Draw(glowTex, drawPos, null, Color.White * (0.55f * fade), 0f, glowTex.Size() / 2f, 0.07f * scale, SpriteEffects.None, 0f);
            CEUtils.ReSetToEndShader();
        }
    }
}
