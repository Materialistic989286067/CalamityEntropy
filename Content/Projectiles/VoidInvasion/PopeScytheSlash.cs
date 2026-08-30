using CalamityEntropy.Assets.Register;
using CalamityEntropy.Content.NPCs.VoidInvasion;
using CalamityEntropy.Content.Particles.CalamityPorts;
using CalamityEntropy.Core.Graphics;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Projectiles.VoidInvasion
{
    /// <summary>
    /// 魔镰旋斩判定弧(void-invasion.md §4.1 P1-4;演出二迭:刀光按 melee-slash 语法重做):
    /// 短命弧形判定,吸附教皇本体,12t 内以锐利缓出扫过约 250°。判定窗口与旧版逐帧一致
    /// (存活前 14t 刃线活跃,视觉尾巴不带判定)。
    /// 刀光 = PopeSlashArc.fxc 极坐标月牙:前缘白热刃线(结构白)+ 腹部高斯鼓形 + 尾迹急衰
    /// (任意瞬间读作有向月牙而非量角器环)+ 斩后定格 6t(几何冻结)→ 噪声侵蚀消散 10t(生猛出生,温柔死亡)。
    /// 命中反馈:白闪 + 沿切向的方向性震屏 + 冲击粒子。
    /// ai[0] = 教皇 whoAmI;ai[1] = 旋向(±1);基准角借初速度通道原生同步。
    /// </summary>
    public class PopeScytheSlash : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Assets/Extra/Ports/Invisible";

        public const int SweepTime = 12;
        public const float BladeReach = 140f;
        public const float SweepArc = 4.4f; //≈250°
        /// <summary>斩后视觉尾巴:定格 + 消散(无判定,纯演出)</summary>
        public const int FreezeTime = 6;
        public const int DissolveTime = 10;
        /// <summary>旧版判定窗(生成后 14t),视觉尾巴不得延长它</summary>
        public const int HitWindow = SweepTime + 2;

        public int OwnerIndex => (int)Projectile.ai[0];
        public int SweepDir => Projectile.ai[1] >= 0 ? 1 : -1;

        private float Timer => HitWindow + FreezeTime + DissolveTime - Projectile.timeLeft;

        public override void SetStaticDefaults()
        {
            ProjectileID.Sets.DrawScreenCheckFluff[Projectile.type] = 800;
        }

        public override void SetDefaults()
        {
            Projectile.width = 30;
            Projectile.height = 30;
            Projectile.friendly = false;
            Projectile.hostile = true;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.timeLeft = HitWindow + FreezeTime + DissolveTime;
        }

        public override bool ShouldUpdatePosition() => false;

        /// <summary>当前扫掠角:锐利缓出(poly(5)),角向距离几乎都落在前几帧,是一记斩而不是波。</summary>
        public float SweepAngle
        {
            get
            {
                float p = MathHelper.Clamp(Timer / SweepTime, 0f, 1f);
                float ease = 1f - (float)Math.Pow(1f - p, 5);
                //rotation 存基准角(生成侧速度通道转存)
                return Projectile.rotation + SweepDir * (-SweepArc * 0.5f + SweepArc * ease);
            }
        }

        public override void AI()
        {
            NPC owner = OwnerIndex >= 0 && OwnerIndex < Main.maxNPCs ? Main.npc[OwnerIndex] : null;
            if (owner == null || !owner.active || owner.ModNPC is not VoidPope)
            {
                Projectile.Kill();
                return;
            }
            if (Projectile.localAI[0] == 0)
            {
                Projectile.localAI[0] = 1;
                Projectile.rotation = Projectile.velocity.ToRotation();
                Projectile.velocity = Vector2.Zero;
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.Item71 with { Volume = 1.1f, Pitch = 0.2f }, owner.Center);
                }
            }
            Projectile.Center = owner.Center;

            //刃尖火花(扫掠期,速度门控式点缀:只在刀在动的时候撒)
            if (!Main.dedServ && Timer < SweepTime && Main.rand.NextBool(2))
            {
                Vector2 tip = owner.Center + SweepAngle.ToRotationVector2() * BladeReach;
                Vector2 tangent = (SweepAngle + SweepDir * MathHelper.PiOver2).ToRotationVector2();
                var s = PRTLoader.NewParticle<PRT_GlowSparkCal>(tip, tangent * Main.rand.NextFloat(5f, 10f),
                    new Color(220, 160, 255), 0.55f);
                s.Configure(false, 12, new Vector2(2f, 0.5f), quickShrink: true);
            }
        }

        //———判定与旧版逐帧一致:仅生成后 14t 活跃,视觉尾巴无判定———
        public override bool CanHitPlayer(Player target)
        {
            return Timer < HitWindow;
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            if (Timer >= HitWindow)
            {
                return false;
            }
            //刃线判定:本体中心到刃尖的线段,随扫掠角逐帧覆盖弧面(几何不动)
            NPC owner = OwnerIndex >= 0 && OwnerIndex < Main.maxNPCs ? Main.npc[OwnerIndex] : null;
            if (owner == null || !owner.active)
            {
                return false;
            }
            Vector2 tip = owner.Center + SweepAngle.ToRotationVector2() * BladeReach;
            return CEUtils.LineThroughRect(owner.Center + (tip - owner.Center) * 0.3f, tip, targetHitbox, 55);
        }

        public override void OnHitPlayer(Player target, Player.HurtInfo info)
        {
            //命中反馈分层:接触点白闪 + 冲击粒子 + 沿挥砍切向的镜头拳(受击者本机结算,演出只在本机)
            if (Main.dedServ)
            {
                return;
            }
            Vector2 tangent = (SweepAngle + SweepDir * MathHelper.PiOver2).ToRotationVector2();
            var flash = PRTLoader.NewParticle<Particles.PRT_Light>(target.Center, Vector2.Zero, Color.White, 1.3f);
            flash.Configure(0.85f, lifetime: 9);
            PRTLoader.NewParticle<PRT_ImpactCal>(target.Center, tangent * 3f, new Color(220, 160, 255), 0.9f)
                .Configure(0.25f, 14);
            ScreenShaker.AddShake(tangent.ToRotation(), 5f);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            NPC owner = OwnerIndex >= 0 && OwnerIndex < Main.maxNPCs ? Main.npc[OwnerIndex] : null;
            if (owner == null || !owner.active)
            {
                return false;
            }
            float t = Timer;
            float sweepP = MathHelper.Clamp(t / SweepTime, 0f, 1f);
            float ease = 1f - (float)Math.Pow(1f - sweepP, 5);

            //可见拖尾弧长:随扫掠展开,封顶 ~146°(旧段由着色器急衰,画面上始终是有向月牙)
            float span = Math.Min(SweepArc * ease, 2.55f);
            if (span < 0.05f)
            {
                return false;
            }
            //定格→消散包络
            float fade = 0f;
            if (t > HitWindow + FreezeTime)
            {
                fade = MathHelper.Clamp((t - HitWindow - FreezeTime) / DissolveTime, 0f, 1f);
            }
            float hot = t < SweepTime ? 1f : MathHelper.Clamp(1f - (t - SweepTime) * 0.06f, 0.25f, 1f);

            SpriteBatch sb = Main.spriteBatch;
            Effect fx = CEEffectAssets.PopeSlashArc;
            float halfSize = BladeReach + 34f;
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.NonPremultiplied, SamplerState.LinearWrap,
                DepthStencilState.None, RasterizerState.CullNone, fx, Main.GameViewMatrix.TransformationMatrix);
            fx.Parameters["uTime"]?.SetValue(Main.GlobalTimeWrappedHourly + Projectile.identity * 0.83f);
            fx.Parameters["uOpacity"]?.SetValue(1f);
            fx.Parameters["uFront"]?.SetValue(SweepAngle);
            fx.Parameters["uSpan"]?.SetValue(span);
            fx.Parameters["uDir"]?.SetValue((float)SweepDir);
            fx.Parameters["uOuter"]?.SetValue(BladeReach / halfSize);
            fx.Parameters["uWidthMax"]?.SetValue(96f / halfSize);
            fx.Parameters["uFade"]?.SetValue(fade);
            fx.Parameters["uHot"]?.SetValue(hot);
            fx.Parameters["uColorEdge"]?.SetValue(new Color(255, 240, 255).ToVector3());
            fx.Parameters["uColorBody"]?.SetValue(new Color(158, 72, 245).ToVector3());
            fx.Parameters["uColorDeep"]?.SetValue(new Color(48, 16, 92).ToVector3());
            Texture2D noise = CEExtraAssets.TurbulentNoise;
            sb.Draw(noise, owner.Center - Main.screenPosition, null, Color.White, 0f,
                noise.Size() / 2, halfSize * 2f / noise.Width, SpriteEffects.None, 0);
            CEUtils.ReSetToEndShader();

            //刃尖热点(扫掠期,加法小辉光钉在当前刃尖)
            if (t < SweepTime + 2)
            {
                sb.UseAdditive();
                Vector2 tip = owner.Center + SweepAngle.ToRotationVector2() * BladeReach;
                Texture2D glow = CEExtraAssets.Glow;
                sb.Draw(glow, tip - Main.screenPosition, null, Color.White * 0.7f, 0f, glow.Size() / 2, 0.34f, SpriteEffects.None, 0);
                CEUtils.ReSetToEndShader();
            }
            return false;
        }
    }
}
