using CalamityEntropy.Assets.Register;
using CalamityEntropy.Content.NPCs.VoidInvasion;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using InnoVault;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Projectiles.VoidInvasion
{
    /// <summary>
    /// 反射激光(void-invasion.md §4.3 P3-4,M8):教皇胸口射出的贯穿激光,触领域边界反射共 4 段。
    /// 几何在首帧一次算完存局部数组(圆内反射:入射向量对边界法线镜像;每端各自从同步量推导,
    /// 自身绘制与自身判定必然一致):初始方向服务端定夺,借初速度通道原生同步;
    /// 领域圆心/半径从教皇实例读(ai[0] = 教皇 whoAmI),教皇缺位时退化为 900px 假想圆。
    /// 节拍:细警示线 30t(第 1 段)→ 各段依次点火(段间错拍 20t,"每段落点前 20t 先画淡预告线")
    /// → 各段留存 50t → 渐灭 12t。判定 = 各段活跃窗内线段展宽;激光 230 档。
    /// </summary>
    public class ReflectLaser : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Assets/Extra/Ports/Invisible";

        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light")]
        private static Asset<Texture2D> glowTex;

        /// <summary>第 1 段警示时长(§4.3:细警示线 30t)</summary>
        public const int WarnTime = 30;
        /// <summary>段间错拍(后一段的预告窗恰是它)</summary>
        public const int SegStagger = 20;
        /// <summary>每段留存(§4.3:50t)</summary>
        public const int SegHold = 50;
        /// <summary>渐灭时长</summary>
        public const int FadeTime = 12;
        /// <summary>反射段数(§4.3:共 4 段)</summary>
        public const int SegCount = 4;
        /// <summary>判定展宽(半宽 px)</summary>
        public const int HitWidth = 44;
        public const int TotalLife = WarnTime + SegStagger * (SegCount - 1) + SegHold + FadeTime; //152

        private float Timer => TotalLife - Projectile.timeLeft;
        //反射路径顶点(首帧算定:points[0..4],段 k = points[k] → points[k+1])
        private readonly Vector2[] points = new Vector2[SegCount + 1];
        private bool pathInit = false;

        public override void SetStaticDefaults()
        {
            ProjectileID.Sets.DrawScreenCheckFluff[Projectile.type] = 2600;
        }

        public override void SetDefaults()
        {
            Projectile.width = 30;
            Projectile.height = 30;
            Projectile.friendly = false;
            Projectile.hostile = true;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.timeLeft = TotalLife;
        }

        public override bool ShouldUpdatePosition() => false;

        /// <summary>段 k 的点火拍。</summary>
        private static int SegStart(int k)
        {
            return WarnTime + k * SegStagger;
        }

        /// <summary>
        /// 首帧路径计算:射线-圆求交 + 法线镜像,4 段一次算完。
        /// 起点在圆心附近且方向近径向时会退化为来回直线,加 0.06rad 偏置破坏对称。
        /// </summary>
        private void InitPath()
        {
            pathInit = true;
            Vector2 center;
            float radius;
            int popeIdx = (int)Projectile.ai[0];
            if (popeIdx >= 0 && popeIdx < Main.maxNPCs && Main.npc[popeIdx].active
                && Main.npc[popeIdx].ModNPC is VoidPope pope)
            {
                center = pope.DomainAnchor;
                radius = Math.Max(pope.DomainRadius, 300f);
            }
            else
            {
                center = Projectile.Center;
                radius = 900f;
            }

            Vector2 pos = Projectile.Center;
            Vector2 dir = Projectile.rotation.ToRotationVector2();
            points[0] = pos;
            for (int k = 0; k < SegCount; k++)
            {
                //射线 pos + t*dir 与圆 |x-center|=radius 的正根
                Vector2 rel = pos - center;
                float b = Vector2.Dot(dir, rel);
                float c = rel.LengthSquared() - radius * radius;
                float disc = b * b - c;
                float t = disc > 0f ? -b + (float)Math.Sqrt(disc) : radius;
                Vector2 hit = pos + dir * Math.Max(t, 40f);
                points[k + 1] = hit;
                //镜像反射;近径向入射加偏置(防来回重叠成一条线)
                Vector2 normal = (hit - center).SafeNormalize(Vector2.UnitY);
                float dot = Vector2.Dot(dir, normal);
                dir = (dir - 2f * dot * normal).SafeNormalize(Vector2.UnitX);
                if (Math.Abs(Vector2.Dot(dir, normal)) > 0.985f)
                {
                    dir = dir.RotatedBy(0.06f);
                }
                pos = hit;
            }
        }

        public override void AI()
        {
            if (Projectile.localAI[0] == 0)
            {
                Projectile.localAI[0] = 1;
                Projectile.rotation = Projectile.velocity.ToRotation();
                Projectile.velocity = Vector2.Zero;
            }
            if (!pathInit)
            {
                InitPath();
            }
            float t = Timer;
            for (int k = 0; k < SegCount; k++)
            {
                int start = SegStart(k);
                //点火拍:各段一次性演出(双端各自凭拍播)
                if ((int)t == start && !Main.dedServ)
                {
                    SoundEngine.PlaySound(new SoundStyle("CalamityEntropy/Assets/Sounds/light_bolt") { Volume = 1f - k * 0.12f, Pitch = -0.4f + k * 0.08f }, points[k]);
                    if (k == 0)
                    {
                        SoundEngine.PlaySound(SoundID.Zombie104 with { Volume = 1f, Pitch = -0.3f }, points[0]);
                        ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.ScreenShake(Vector2.Zero, 8), Main.LocalPlayer.Distance(points[0]), 2200);
                    }
                    //反射点撞击闪光(段序色,和预告线同一色阶)
                    PRTLoader.NewParticle<PRT_PulseRing>(points[k + 1], Vector2.Zero, SegHalo[k], 0.08f).Configure(3.2f, 26);
                    var flash = PRTLoader.NewParticle<PRT_Light>(points[k + 1], Vector2.Zero, Color.White, 1.6f);
                    flash.Configure(0.82f, lifetime: 12);
                }
                if (t >= start && t < start + SegHold)
                {
                    Lighting.AddLight(Vector2.Lerp(points[k], points[k + 1], 0.5f), 0.7f, 0.3f, 1.1f);
                }
            }
        }

        /// <summary>段 k 当前活跃(判定窗)。</summary>
        private bool SegActive(int k)
        {
            float t = Timer;
            int start = SegStart(k);
            return t >= start && t < start + SegHold;
        }

        public override bool CanHitPlayer(Player target)
        {
            for (int k = 0; k < SegCount; k++)
            {
                if (SegActive(k))
                {
                    return true;
                }
            }
            return false;
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            if (!pathInit)
            {
                return false;
            }
            for (int k = 0; k < SegCount; k++)
            {
                if (SegActive(k) && CEUtils.LineThroughRect(points[k], points[k + 1], targetHitbox, HitWidth))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>段序色阶(§任务 5:第 N 段预告随段序变色,玩家能"数"到第几段):紫 → 品红 → 热玫 → 金白。</summary>
        private static readonly Color[] SegHalo =
        {
            new Color(170, 90, 255),
            new Color(225, 95, 235),
            new Color(255, 105, 185),
            new Color(255, 195, 125),
        };
        private static readonly Color[] SegEdge =
        {
            new Color(70, 26, 140),
            new Color(105, 30, 130),
            new Color(130, 36, 95),
            new Color(140, 90, 45),
        };

        public override bool PreDraw(ref Color lightColor)
        {
            if (!pathInit)
            {
                return false;
            }
            float t = Timer;
            Texture2D beam = CEExtraAssets.vlbw;
            //———预告层(加法):各段落点前 20t 的变色淡线 + 落点预燃辉光———
            Main.spriteBatch.UseAdditive();
            for (int k = 0; k < SegCount; k++)
            {
                int start = SegStart(k);
                Vector2 a = points[k];
                float segLen = Vector2.Distance(a, points[k + 1]);
                float rot = (points[k + 1] - a).ToRotation();
                Vector2 pos = a - Main.screenPosition;
                Vector2 origin = beam.Size() / 2 * new Vector2(0, 1);
                float lenScale = segLen / beam.Width;

                int warnLen = k == 0 ? WarnTime : SegStagger;
                int warnFrom = start - warnLen;
                if (t >= warnFrom && t < start)
                {
                    float bright = (t - warnFrom) / warnLen;
                    float baseA = k == 0 ? 0.45f : 0.32f;
                    Color c = Color.Lerp(new Color(70, 25, 120), SegHalo[k], bright) * (baseA + 0.55f * bright);
                    Main.spriteBatch.Draw(beam, pos, null, c, rot, origin,
                        new Vector2(lenScale, 0.2f + 0.3f * bright), SpriteEffects.None, 0);
                    //下一反射点预燃(落点比激光先亮,玩家永远早一步知道路径)
                    Texture2D preGlow = glowTex.Value;
                    Main.spriteBatch.Draw(preGlow, points[k + 1] - Main.screenPosition, null,
                        SegHalo[k] * (0.55f * bright), 0, preGlow.Size() / 2, 0.5f + 0.5f * bright, SpriteEffects.None, 0);
                }
            }
            CEUtils.ReSetToEndShader();

            //———活跃束(PopeBeam 分层束体,段序变色) + 反射点辉光———
            for (int k = 0; k < SegCount; k++)
            {
                int start = SegStart(k);
                if (t < start || t >= start + SegHold + FadeTime)
                {
                    continue;
                }
                Vector2 a = points[k];
                float segLen = Vector2.Distance(a, points[k + 1]);
                float rot = (points[k + 1] - a).ToRotation();
                float fade = t >= start + SegHold ? 1f - (t - start - SegHold) / FadeTime : 1f;
                float grow = MathHelper.Clamp((t - start) / 5f, 0f, 1f); //出膛端 5t 展宽(公平阀)
                PopeVfx.DrawBeam(a, rot, segLen, HitWidth * 2.3f, fade, grow, SegHalo[k], SegEdge[k],
                    coreFrac: 0.28f, fringe: 0.5f, flicker: 0.32f, flickerSeed: k * 2.9f + Projectile.identity);
                Main.spriteBatch.UseAdditive();
                //出膛冲击帧(每段头 2t 白闪)
                if (t - start < 2f)
                {
                    Texture2D flashTex = CEExtraAssets.vlbw;
                    Main.spriteBatch.Draw(flashTex, a - Main.screenPosition, null, Color.White * (0.85f * (1f - (t - start) / 2f)),
                        rot, flashTex.Size() / 2 * new Vector2(0, 1),
                        new Vector2(segLen / flashTex.Width, HitWidth * 2.8f / flashTex.Height), SpriteEffects.None, 0);
                }
                //反射点辉光(段色)
                Texture2D glow = glowTex.Value;
                Main.spriteBatch.Draw(glow, points[k + 1] - Main.screenPosition, null,
                    SegHalo[k] * (0.85f * fade), 0, glow.Size() / 2, 1.1f * fade + 0.2f, SpriteEffects.None, 0);
                CEUtils.ReSetToEndShader();
            }
            return false;
        }
    }
}
