using CalamityEntropy.Assets.Register;
using CalamityEntropy.Core.Graphics;
using CalamityEntropy.Utilities;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.Audio;
using Terraria.Graphics.Shaders;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Projectiles.VoidInvasion
{
    /// <summary>
    /// 虚空闪电(void-invasion.md §2.3,红衣主教招式 2;M8 起教皇 P3-2 闪电球复用)。
    /// 生成时锁定几何:位置 = 起点,朝向借初速度通道原生同步(首帧转存 rotation,镜像 VoidPortal);
    /// 暗紫警示线 30t 渐亮 → 沿线放电 2 次(间隔 18t,各 6t 判定窗)→ 电弧渐灭。
    /// ai[1] = 1 时为单放模式(§4.3 P3-2):只放第一段电,总时长同步缩短(timeLeft 双端首帧同式改写)。
    /// 判定 = 直线段展宽 24px(与深渊亡魂光束同宽,确定性,双端一致);
    /// 折线抖动纯客户端随机(LightningGenerator),视觉走 CalamityEntropy:HeavenlyGaleLightningArc
    /// 着色器条带,镜像 Lightning.cs/SpiritLaser.cs 的绘制姿势。
    /// </summary>
    public class VoidLightningBolt : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Assets/Extra/white";

        /// <summary>警示线时长(主教前摇节拍引用)</summary>
        public const int WarnTime = 30;
        /// <summary>第二次放电相对第一次的延迟</summary>
        public const int SecondStrikeDelay = 18;
        /// <summary>单次放电的判定窗</summary>
        public const int StrikeWindow = 6;
        /// <summary>放电段总长(主教收招节拍引用)</summary>
        public const int DischargeDuration = SecondStrikeDelay + StrikeWindow;
        /// <summary>线段长度</summary>
        public const float BoltLength = 1700f;
        //放电结束后的电弧渐灭时长
        private const int FadeTime = 26;

        //折线组(纯客户端视觉,放电拍重掷)
        private readonly List<List<Vector2>> arcs = new();

        private float Timer => Projectile.ai[0];
        /// <summary>单放模式(M8 闪电球):只放第一段电</summary>
        private bool SingleStrike => Projectile.ai[1] == 1;
        private Vector2 EndPoint => Projectile.Center + Projectile.rotation.ToRotationVector2() * BoltLength;

        public override void SetStaticDefaults()
        {
            //线段极长,起点出屏也要照画
            ProjectileID.Sets.DrawScreenCheckFluff[Projectile.type] = 2400;
        }

        public override void SetDefaults()
        {
            Projectile.width = 10;
            Projectile.height = 10;
            Projectile.friendly = false;
            Projectile.hostile = true;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.timeLeft = WarnTime + DischargeDuration + FadeTime;
        }

        public override bool ShouldUpdatePosition() => false;

        public override void AI()
        {
            if (Projectile.localAI[0] == 0)
            {
                Projectile.localAI[0] = 1;
                Projectile.rotation = Projectile.velocity.ToRotation();
                Projectile.velocity = Vector2.Zero;
                //单放模式:总寿命缩短(双端同式,timeLeft 不进生成包)
                if (SingleStrike)
                {
                    Projectile.timeLeft = WarnTime + StrikeWindow + FadeTime;
                }
            }
            //放电拍:重掷折线 + 电花音(双端各自演出,判定不吃折线)
            if (Timer == WarnTime || (!SingleStrike && Timer == WarnTime + SecondStrikeDelay))
            {
                if (!Main.dedServ)
                {
                    RollArcs();
                    SoundEngine.PlaySound(new SoundStyle("CalamityEntropy/Assets/Sounds/spark") { Volume = 1.6f, Pitch = -0.2f }, Projectile.Center);
                }
            }
            if (Timer >= WarnTime)
            {
                Lighting.AddLight(Projectile.Center + Projectile.rotation.ToRotationVector2() * BoltLength * 0.5f, 0.5f, 0.2f, 0.9f);
            }
            Projectile.ai[0]++;
        }

        /// <summary>放电折线重掷(纯客户端):3 条中点位移折线叠出电弧密度。</summary>
        private void RollArcs()
        {
            arcs.Clear();
            for (int i = 0; i < 3; i++)
            {
                var points = LightningGenerator.GenerateLightning(Projectile.Center, EndPoint, 30f, 6);
                arcs.Add(points);
            }
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            float t = Timer;
            bool active = (t >= WarnTime && t < WarnTime + StrikeWindow)
                || (!SingleStrike && t >= WarnTime + SecondStrikeDelay && t < WarnTime + SecondStrikeDelay + StrikeWindow);
            if (!active)
            {
                return false;
            }
            //线段命中,展宽 24px(§2.3:玩家中心到线段距离 <24)
            return CEUtils.LineThroughRect(Projectile.Center, EndPoint, targetHitbox, 24);
        }

        public float ArcWidthFunction(float completionRatio, Vector2 vertex)
        {
            float fade = 1f;
            float dischargeEnd = WarnTime + (SingleStrike ? StrikeWindow : DischargeDuration);
            if (Timer > dischargeEnd)
            {
                fade = Math.Max(0f, 1f - (Timer - dischargeEnd) / FadeTime);
            }
            return 13f * fade;
        }

        public Color ArcColorFunction(float completionRatio, Vector2 vertex)
        {
            float lerp = (float)Math.Sin(Projectile.identity / 3f + completionRatio * 16f + Main.GlobalTimeWrappedHourly * 1.1f) * 0.5f + 0.5f;
            return CEUtils.MulticolorLerp(lerp, new Color(205, 135, 255), new Color(120, 50, 220));
        }

        public override bool PreDraw(ref Color lightColor)
        {
            float t = Timer;
            Texture2D warn = CEExtraAssets.vlbw;
            if (t < WarnTime)
            {
                //警示线:暗紫渐亮的细线(§2.3),贴图与深渊亡魂光束警示同款(视觉语言统一)
                float bright = t / WarnTime;
                Color c = Color.Lerp(new Color(70, 25, 120), new Color(200, 120, 255), bright) * (0.4f + 0.6f * bright);
                Main.spriteBatch.UseAdditive();
                Main.spriteBatch.Draw(warn, Projectile.Center - Main.screenPosition, null, c, Projectile.rotation,
                    warn.Size() / 2 * new Vector2(0, 1), new Vector2(BoltLength / warn.Width, 0.25f + 0.35f * bright), SpriteEffects.None, 0);
                CEUtils.ReSetToEndShader();
                return false;
            }
            //放电闪拍:每次放电后 8t 内叠一条亮束(可读性,镜像 VoidLightBall 的发射帧闪烁;单放模式只闪一次)
            float sinceStrike = SingleStrike || t < WarnTime + SecondStrikeDelay ? t - WarnTime : t - WarnTime - SecondStrikeDelay;
            if (sinceStrike < 8f)
            {
                Color flash = ((int)(t / 2) % 2 == 0 ? Color.White : Color.MediumPurple) * (1f - sinceStrike / 8f);
                Main.spriteBatch.UseAdditive();
                Main.spriteBatch.Draw(warn, Projectile.Center - Main.screenPosition, null, flash, Projectile.rotation,
                    warn.Size() / 2 * new Vector2(0, 1), new Vector2(BoltLength / warn.Width, 1.1f), SpriteEffects.None, 0);
                CEUtils.ReSetToEndShader();
            }
            if (arcs.Count == 0)
            {
                return false;
            }
            //电弧条带:折线 + HeavenlyGaleLightningArc(镜像 SpiritLaser 姿势,RenderTrail 自管 GPU 状态)
            GameShaders.Misc["CalamityEntropy:HeavenlyGaleLightningArc"].UseImage1("Images/Misc/Perlin");
            GameShaders.Misc["CalamityEntropy:HeavenlyGaleLightningArc"].Apply();
            foreach (var points in arcs)
            {
                CEPrimitiveRenderer.RenderTrail(points, new CEPrimitiveSettings(ArcWidthFunction, ArcColorFunction,
                    (_, _) => Projectile.Size * 0.2f, false,
                    shader: GameShaders.Misc["CalamityEntropy:HeavenlyGaleLightningArc"]), 10);
            }
            return false;
        }
    }
}
