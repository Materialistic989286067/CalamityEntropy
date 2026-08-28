using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using InnoVault;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Projectiles.VoidInvasion
{
    /// <summary>
    /// 嵌合体爪击(void-invasion.md §2.10 双爪连击):挥出瞬间的弧形判定,140px 前方短存 8t,
    /// 吸附 ai[0] 指定的嵌合体,ai[1] = 朝向(±1)。视觉主体是嵌合体本体的爪挥动画,
    /// 这里补刀光:斩痕粒子一记 + 沿弧线的风压线与爪风火花。
    /// </summary>
    public class ChimeraClawSlash : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Assets/Extra/Ports/Invisible";

        public int OwnerIndex => (int)Projectile.ai[0];
        public int Dir => Projectile.ai[1] >= 0 ? 1 : -1;

        public override void SetDefaults()
        {
            Projectile.width = 140;
            Projectile.height = 130;
            Projectile.friendly = false;
            Projectile.hostile = true;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.timeLeft = 8;
        }

        public override void AI()
        {
            NPC owner = OwnerIndex >= 0 && OwnerIndex < Main.maxNPCs ? Main.npc[OwnerIndex] : null;
            if (owner == null || !owner.active)
            {
                Projectile.Kill();
                return;
            }
            Projectile.Center = owner.Center + new Vector2(Dir * 110f, -6f);
            Projectile.velocity = Vector2.Zero;

            if (Main.dedServ)
                return;

            //出爪拍:一记斩痕(半透明大弧光)+ 白闪(刀光的"帧")
            if (Projectile.localAI[0] == 0)
            {
                Projectile.localAI[0] = 1;
                float slashRot = Dir > 0 ? -0.5f : MathHelper.Pi + 0.5f;
                var slash = PRTLoader.NewParticle<PRT_SlashDarkRed>(Projectile.Center, Vector2.Zero,
                    new Color(220, 130, 255), 1.05f);
                slash.Configure(0.85f, true, PRTDrawModeEnum.AdditiveBlend, slashRot, 10);
                var flash = PRTLoader.NewParticle<PRT_BloomCal>(Projectile.Center, Vector2.Zero, new Color(235, 190, 255), 0.3f);
                flash.Configure(1.3f, 8);
            }

            //刀光弧:沿挥砍弧线扫过的爪风火花(本体爪动画是主视觉)
            float swipeP = 1f - Projectile.timeLeft / 8f;
            float arcAng = MathHelper.Lerp(-1.2f, 0.9f, swipeP);
            Vector2 arcPos = owner.Center + new Vector2(Dir * 90f, -20f) + new Vector2(Dir * (float)Math.Cos(arcAng), (float)Math.Sin(arcAng)) * 70f;
            //弧线切向(挥砍瞬时方向):对参数化位置求导,镜像由 Dir 带入 x 分量
            Vector2 sweepVel = new Vector2(-Dir * (float)Math.Sin(arcAng), (float)Math.Cos(arcAng)) * 7f;
            for (int i = 0; i < 2; i++)
            {
                var s = PRTLoader.NewParticle<PRT_GlowSparkCal>(arcPos + CEUtils.randomPointInCircle(12f),
                    sweepVel.RotatedBy(Main.rand.NextFloat(-0.25f, 0.25f)) * Main.rand.NextFloat(0.7f, 1.15f),
                    new Color(220, 130, 255), Main.rand.NextFloat(0.35f, 0.55f));
                s.Configure(false, 13, new Vector2(0.5f, 1.7f), quickShrink: true);
            }
            //弧线外缘的风压线(挥砍方向可读)
            if (Projectile.timeLeft % 2 == 0)
            {
                var line = PRTLoader.NewParticle<PRT_LineCal>(arcPos, sweepVel * 1.4f,
                    new Color(190, 110, 255), Main.rand.NextFloat(0.45f, 0.7f));
                line.Configure(false, 10);
            }
        }
    }

    /// <summary>
    /// 混沌地刺(void-invasion.md §2.10 插地爆发):Center 为地表基点(生成侧已贴地)。
    /// ai[0] = 爆发倒计时(依次错拍由生成侧赋值),最后 15t 裂纹预警(发光裂纹自中心蔓延
    /// + 渗光 + 末 3t 静默);归零后地刺爆出(碎屑 + 冲击环 + 尖端亮星),存在 20t。
    /// 无美术素材,MagicPixel 叠条拼刺加辉光描边(美术到位后一换即可)。
    /// </summary>
    public class ChimeraGroundSpike : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Assets/Extra/Ports/Invisible";

        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light2")]
        private static Asset<Texture2D> glowTex;

        public const int EruptTime = 20;
        public const int WarnTime = 15;
        private const float SpikeWidth = 40f;
        private const float SpikeHeight = 130f;
        /// <summary>预警末尾静默拍(爆发前的吸气)</summary>
        private const int QuietTime = 3;

        private ref float Countdown => ref Projectile.ai[0];
        public bool Erupting => Countdown <= 0;

        public override void SetDefaults()
        {
            Projectile.width = 24;
            Projectile.height = 24;
            Projectile.friendly = false;
            Projectile.hostile = true;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.timeLeft = 300;
        }

        public override bool CanHitPlayer(Player target)
        {
            return Erupting;
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            if (!Erupting)
                return false;
            //爆发首 5t 刺体从地里长出,判定随视觉抬升
            float rise = MathHelper.Clamp((EruptTime - Projectile.timeLeft) / 5f, 0f, 1f);
            Rectangle spike = new Rectangle((int)(Projectile.Center.X - SpikeWidth / 2), (int)(Projectile.Center.Y - SpikeHeight * rise), (int)SpikeWidth, (int)(SpikeHeight * rise));
            return spike.Intersects(targetHitbox);
        }

        public override void AI()
        {
            Projectile.velocity = Vector2.Zero;
            if (Countdown > 0)
            {
                Countdown--;
                //裂纹预警(§2.10):地表碎裂尘 + 裂隙渗光粒(末 3t 静默,给爆发让因果拍)
                if (Countdown <= WarnTime && Countdown > QuietTime && !Main.dedServ)
                {
                    if (Main.rand.NextBool(2))
                    {
                        Dust d = Dust.NewDustDirect(Projectile.Center - new Vector2(SpikeWidth / 2, 6), (int)SpikeWidth, 8, DustID.Smoke, 0, -1.6f, 120, default, 1f);
                        d.noGravity = true;
                    }
                    if (Main.rand.NextBool(3))
                    {
                        var seep = PRTLoader.NewParticle<PRT_Light>(Projectile.Center + new Vector2(Main.rand.NextFloat(-SpikeWidth * 0.7f, SpikeWidth * 0.7f), Main.rand.NextFloat(-4f, 2f)),
                            new Vector2(0, -Main.rand.NextFloat(0.6f, 1.4f)), new Color(200, 110, 255), 0.35f);
                        seep.Configure(0.8f, lifetime: 14);
                    }
                }
                if (Countdown <= 0)
                {
                    Projectile.timeLeft = EruptTime;
                    if (!Main.dedServ)
                    {
                        SoundEngine.PlaySound(SoundID.DeerclopsIceAttack with { Volume = 0.7f, Pitch = 0.3f }, Projectile.Center);
                        //爆出拍:碎屑弹射 + 贴地冲击环 + 尖端亮星
                        for (int i = 0; i < 12; i++)
                        {
                            Dust.NewDust(Projectile.Center - new Vector2(SpikeWidth / 2, 8), (int)SpikeWidth, 10, DustID.Smoke,
                                Main.rand.NextFloat(-2f, 2f), Main.rand.NextFloat(-4f, -1f));
                        }
                        for (int i = 0; i < 6; i++)
                        {
                            var rock = PRTLoader.NewParticle<PRT_LineCal>(Projectile.Center + new Vector2(Main.rand.NextFloat(-14f, 14f), -4f),
                                new Vector2(Main.rand.NextFloat(-3.5f, 3.5f), -Main.rand.NextFloat(3f, 7.5f)),
                                Color.Lerp(new Color(150, 100, 200), new Color(90, 60, 130), Main.rand.NextFloat()), Main.rand.NextFloat(0.5f, 0.85f));
                            rock.Configure(true, 24);
                        }
                        var ring = PRTLoader.NewParticle<PRT_DirectionalPulseRing>(Projectile.Center + new Vector2(0, -6), Vector2.Zero,
                            new Color(200, 120, 255), 0.16f);
                        ring.Configure(new Vector2(1.5f, 0.5f), 0f, 1.6f, 14);
                        var tip = PRTLoader.NewParticle<PRT_SparkleCal>(Projectile.Center + new Vector2(0, -SpikeHeight * 0.9f), Vector2.Zero,
                            new Color(235, 200, 255), 0.8f);
                        tip.Configure(new Color(190, 110, 255), 14, 0.1f, 1.3f);
                    }
                }
                return;
            }
            Lighting.AddLight(Projectile.Center + new Vector2(0, -SpikeHeight / 2), 0.5f, 0.2f, 0.7f);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            SpriteBatch sb = Main.spriteBatch;
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            Vector2 basePos = Projectile.Center - Main.screenPosition;

            if (!Erupting)
            {
                //裂纹预警:发光裂纹贴图自中心蔓延(横向展开 + 渐亮),静默拍锁最亮
                if (Countdown <= WarnTime)
                {
                    Texture2D crack = CEUtils.getExtraTex("Cracks");
                    float warnP = 1f - MathHelper.Clamp((Countdown - QuietTime) / (float)(WarnTime - QuietTime), 0f, 1f);
                    float spread = 0.35f + 0.65f * warnP;
                    Color cc = new Color(200, 110, 255) * (0.25f + 0.6f * warnP);
                    //横拍地表:裂纹压成扁片,宽随预警进度蔓延
                    Vector2 crackScale = new Vector2(110f * spread / crack.Width, 34f / crack.Height);
                    sb.Draw(crack, basePos + new Vector2(0, -2), null, cc, 0f, crack.Size() / 2, crackScale, SpriteEffects.None, 0);
                    //裂缝心渗光
                    Texture2D glow = glowTex.Value;
                    sb.Draw(glow, basePos + new Vector2(0, -4), null, new Color(180, 90, 255) * (0.4f * warnP), 0, glow.Size() / 2,
                        new Vector2(0.55f * spread, 0.16f), SpriteEffects.None, 0);
                }
            }
            else
            {
                //MagicPixel 叠条拼刺:自底向上收窄,首 5t 生长;辉光描边一层 + 亮芯一条
                float rise = MathHelper.Clamp((EruptTime - Projectile.timeLeft) / 5f, 0f, 1f);
                rise = 1f - (1f - rise) * (1f - rise);
                float fade = MathHelper.Clamp(Projectile.timeLeft / 6f, 0f, 1f);
                Texture2D pixel = TextureAssets.MagicPixel.Value;
                (float w, float h)[] strips = { (36f, 34f), (28f, 66f), (20f, 94f), (12f, 116f), (5f, 130f) };
                //辉光描边(宽一档、暗一档,画在主条之下)
                foreach (var (w, h) in strips)
                {
                    float hh = h * rise;
                    sb.Draw(pixel, new Rectangle((int)(basePos.X - w / 2 - 3), (int)(basePos.Y - hh - 2), (int)w + 6, (int)hh + 2), new Color(120, 50, 200) * (0.35f * fade));
                }
                foreach (var (w, h) in strips)
                {
                    float hh = h * rise;
                    sb.Draw(pixel, new Rectangle((int)(basePos.X - w / 2), (int)(basePos.Y - hh), (int)w, (int)hh), new Color(140, 60, 230) * (0.75f * fade));
                }
                sb.Draw(pixel, new Rectangle((int)(basePos.X - 2), (int)(basePos.Y - 126f * rise), 4, (int)(126 * rise)), new Color(240, 200, 255) * (0.9f * fade));
                Texture2D glow2 = glowTex.Value;
                sb.Draw(glow2, basePos, null, new Color(190, 100, 255) * (0.7f * fade), 0, glow2.Size() / 2, 0.55f, SpriteEffects.None, 0);
                sb.Draw(glow2, basePos + new Vector2(0, -SpikeHeight * rise), null, new Color(230, 180, 255) * (0.5f * fade), 0, glow2.Size() / 2, 0.3f, SpriteEffects.None, 0);
            }

            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            return false;
        }
    }
}
