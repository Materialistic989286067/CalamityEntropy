using CalamityEntropy.Assets.Register;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using CalamityEntropy.Core.Graphics;
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
    /// 魔像冲击波(void-invasion.md §2.6):跳锤落地向两侧甩出的贴地行波,18px/t 起步衰减消失。
    /// 生成时速度决定方向,每 tick 吸附地表(可爬 2 格内起伏)。
    /// 演出 = 贴地滚动的尘浪(烟粒)+ 抛物碎石 + 波前辉光,首帧一道贴地冲击环。
    /// </summary>
    public class GolemShockwave : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Assets/Extra/Ports/Invisible";

        public override void SetDefaults()
        {
            Projectile.width = 50;
            Projectile.height = 44;
            Projectile.friendly = false;
            Projectile.hostile = true;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.timeLeft = 45;
        }

        public override void AI()
        {
            //衰减消失(§2.6)
            Projectile.velocity.X *= 0.965f;
            Projectile.velocity.Y = 0;
            SnapToGround();

            if (Main.dedServ)
                return;
            float strength = Projectile.timeLeft / 45f;
            int dir = Math.Sign(Projectile.velocity.X);

            //首帧:贴地压扁的冲击环(波的"出膛拍")
            if (Projectile.localAI[0] == 0)
            {
                Projectile.localAI[0] = 1;
                var ring = PRTLoader.NewParticle<PRT_DirectionalPulseRing>(Projectile.Bottom + new Vector2(0, -10),
                    Vector2.Zero, new Color(200, 120, 255), 0.2f);
                ring.Configure(new Vector2(1.5f, 0.45f), 0f, 2.4f, 16);
            }

            //贴地尘浪:暗色滚动烟(体量层)
            var smoke = PRTLoader.NewParticle<PRT_HeavySmokeCal>(
                Projectile.Bottom + new Vector2(Main.rand.NextFloat(-22f, 22f), -Main.rand.NextFloat(2f, 16f)),
                new Vector2(Projectile.velocity.X * 0.45f, -Main.rand.NextFloat(0.4f, 1.4f)),
                Color.Lerp(new Color(70, 50, 95), new Color(110, 75, 150), Main.rand.NextFloat()),
                Main.rand.NextFloat(0.75f, 1.2f) * strength);
            smoke.Configure(0.65f * strength, 26, Main.rand.NextFloat(-0.04f, 0.04f));

            //抛物碎石:向前上方甩出,受重力回落(±30° 扇)
            if (Main.rand.NextBool(2))
            {
                Vector2 vel = new Vector2(dir * Main.rand.NextFloat(2f, 6f), -Main.rand.NextFloat(3.5f, 7f));
                var rock = PRTLoader.NewParticle<PRT_LineCal>(Projectile.Bottom + new Vector2(Main.rand.NextFloat(-16f, 16f), -8f),
                    vel, Color.Lerp(new Color(150, 110, 200), new Color(90, 60, 130), Main.rand.NextFloat()),
                    Main.rand.NextFloat(0.5f, 0.9f) * strength);
                rock.Configure(true, 26);
            }

            //波前辉光(可读性:波头在哪一眼可见)
            var p = PRTLoader.NewParticle<PRT_Light>(Projectile.Bottom + new Vector2(dir * 20f, -Main.rand.NextFloat(4f, 14f)),
                new Vector2(Projectile.velocity.X * 0.3f, -Main.rand.NextFloat(1f, 2.5f)), new Color(200, 110, 255) * strength, 0.55f * strength);
            p.Configure(0.85f, lifetime: 14);
            Lighting.AddLight(Projectile.Center, 0.35f * strength, 0.15f * strength, 0.5f * strength);
        }

        /// <summary>底边吸附最近地表,行进中跟随 2 格内的起伏。</summary>
        private void SnapToGround()
        {
            for (int step = -2; step <= 4; step++)
            {
                Vector2 probe = new Vector2(Projectile.Center.X, Projectile.position.Y + Projectile.height + step * 16);
                Point tile = probe.ToTileCoordinates();
                if (!WorldGen.InWorld(tile.X, tile.Y, 8))
                    continue;
                if (WorldGen.SolidOrSlopedTile(tile.X, tile.Y))
                {
                    Projectile.position.Y = tile.Y * 16 - Projectile.height;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// 魔像光柱(void-invasion.md §2.6 光柱锤击):落点两侧 120/240px 四处地面。
    /// ai[0] = 爆发倒计时(生成侧填 30t 告警),期间地面法阵收束告警(末 4t 静默拍);
    /// 归零后光柱爆发:VInvPillar 着色器柱体自地面上冲生长 + 顶端光冠 + 基座碎屑,
    /// 60x400 判定存在 20t。Center 即光柱底心(生成侧已贴地)。
    /// </summary>
    public class GolemLightPillar : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Assets/Extra/Ports/Invisible";

        [VaultLoaden("CalamityEntropy/Content/Projectiles/VoidInvasion/VoidGlyph")]
        private static Asset<Texture2D> glyphTex;
        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light2")]
        private static Asset<Texture2D> glowTex;

        public const int EruptTime = 20;
        public const float PillarWidth = 60f;
        public const float PillarHeight = 400f;
        /// <summary>告警末尾的静默拍(爆发前的"吸气")</summary>
        private const int QuietTime = 4;
        /// <summary>柱体生长时长(爆发首段,判定抬升与视觉同步)</summary>
        private const int RiseTime = 5;

        //---- 可调色板(VInvPillar) ----
        private static readonly Vector3 ColorCore = new Vector3(0.95f, 0.82f, 1f);
        private static readonly Vector3 ColorEdge = new Vector3(0.5f, 0.2f, 0.88f);

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
            //判定随柱体生长抬升(视觉与判定同步,公平阀)
            float rise = RiseEase();
            Rectangle pillar = new Rectangle((int)(Projectile.Center.X - PillarWidth / 2), (int)(Projectile.Center.Y - PillarHeight * rise), (int)PillarWidth, (int)(PillarHeight * rise));
            return pillar.Intersects(targetHitbox);
        }

        /// <summary>柱体生长包络:首 RiseTime tick 三次缓出上冲。</summary>
        private float RiseEase()
        {
            float t = MathHelper.Clamp((EruptTime - Projectile.timeLeft) / (float)RiseTime, 0f, 1f);
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }

        public override void AI()
        {
            Projectile.velocity = Vector2.Zero;
            if (Countdown > 0)
            {
                Countdown--;
                //告警收束(末 4t 静默:粒子全停,给爆发让出因果拍)
                if (!Main.dedServ && Countdown > QuietTime)
                {
                    //地面碎裂尘 + 上升光点(能量自地底聚来)
                    if (Main.rand.NextBool(3))
                    {
                        Dust d = Dust.NewDustDirect(Projectile.Center - new Vector2(PillarWidth / 2, 4), (int)PillarWidth, 8, DustID.Smoke, 0, -1.4f, 130, default, 0.9f);
                        d.noGravity = true;
                    }
                    if (Main.rand.NextBool(2))
                    {
                        var p = PRTLoader.NewParticle<PRT_Light>(Projectile.Center + new Vector2(Main.rand.NextFloat(-PillarWidth, PillarWidth), Main.rand.NextFloat(-4f, 6f)),
                            new Vector2(0, -Main.rand.NextFloat(0.8f, 1.8f)), new Color(190, 100, 255), 0.4f);
                        p.Configure(0.8f, lifetime: 16);
                    }
                    //收束环:告警中段两拍,环向法阵收拢(§2.6 告警法阵收束)
                    if ((int)Countdown % 12 == 0)
                    {
                        var ring = PRTLoader.NewParticle<PRT_PulseRing>(Projectile.Center + new Vector2(0, -8), Vector2.Zero,
                            new Color(190, 110, 255), 1.6f);
                        ring.Configure(0.15f, 14);
                    }
                }
                if (Countdown <= 0)
                {
                    //爆发拍:定住存在时长;表现在双端各自触发
                    Projectile.timeLeft = EruptTime;
                    if (!Main.dedServ)
                    {
                        SoundEngine.PlaySound(SoundID.Item72 with { Pitch = -0.2f }, Projectile.Center);
                        SoundEngine.PlaySound(SoundID.Item14 with { Volume = 0.55f, Pitch = 0.25f }, Projectile.Center);
                        CEUtils.SetShake(Projectile.Center, 5.5f, 1300);
                        //基座:地面碎屑弹射 + 贴地冲击环 + 白闪
                        for (int i = 0; i < 10; i++)
                        {
                            Vector2 vel = new Vector2(Main.rand.NextFloat(-5f, 5f), -Main.rand.NextFloat(3f, 9f));
                            var rock = PRTLoader.NewParticle<PRT_LineCal>(Projectile.Center + new Vector2(Main.rand.NextFloat(-24f, 24f), -6f),
                                vel, Color.Lerp(new Color(160, 120, 210), new Color(90, 60, 130), Main.rand.NextFloat()), Main.rand.NextFloat(0.6f, 1f));
                            rock.Configure(true, 30);
                        }
                        var ring = PRTLoader.NewParticle<PRT_DirectionalPulseRing>(Projectile.Center + new Vector2(0, -8),
                            Vector2.Zero, new Color(210, 130, 255), 0.25f);
                        ring.Configure(new Vector2(1.6f, 0.5f), 0f, 2.8f, 18);
                        var flash = PRTLoader.NewParticle<PRT_BloomCal>(Projectile.Center + new Vector2(0, -20), Vector2.Zero, Color.White, 0.4f);
                        flash.Configure(1.8f, 10);
                    }
                }
                return;
            }
            Lighting.AddLight(Projectile.Center + new Vector2(0, -PillarHeight / 2), 0.9f, 0.45f, 1.2f);
            Lighting.AddLight(Projectile.Center + new Vector2(0, -PillarHeight * RiseEase()), 0.6f, 0.3f, 0.85f);
            if (Main.dedServ)
                return;
            //柱内上升速度线 + 溢光粒子
            var line = PRTLoader.NewParticle<PRT_LineCal>(Projectile.Center + new Vector2(Main.rand.NextFloat(-PillarWidth * 0.4f, PillarWidth * 0.4f), -Main.rand.NextFloat(0, PillarHeight * RiseEase() * 0.9f)),
                new Vector2(0, -Main.rand.NextFloat(9f, 16f)), new Color(230, 180, 255), Main.rand.NextFloat(0.5f, 0.9f));
            line.Configure(false, 12);
            var mote = PRTLoader.NewParticle<PRT_Light>(Projectile.Center + new Vector2(Main.rand.NextFloat(-PillarWidth / 2, PillarWidth / 2), -Main.rand.NextFloat(0, PillarHeight)),
                new Vector2(0, -Main.rand.NextFloat(2f, 5f)), new Color(210, 140, 255), 0.6f);
            mote.Configure(0.9f, lifetime: 14);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            SpriteBatch sb = Main.spriteBatch;
            Vector2 basePos = Projectile.Center - Main.screenPosition;

            if (!Erupting)
            {
                //告警:地面法阵,随倒计时收拢并增亮(收束 = 蓄力语义);静默拍锁最亮但停一切动效
                sb.End();
                sb.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
                Texture2D glyph = glyphTex.Value;
                float warnP = 1f - MathHelper.Clamp(Countdown / 30f, 0f, 1f);
                bool quiet = Countdown <= QuietTime;
                float pulse = quiet ? 1f : 1f + 0.08f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 25f);
                //法阵横径 0.55→0.34 收束
                float gw = MathHelper.Lerp(0.55f, 0.34f, warnP);
                Color c = new Color(200, 120, 255) * (0.35f + 0.65f * warnP);
                sb.Draw(glyph, basePos, null, c, quiet ? 0f : Main.GlobalTimeWrappedHourly * 2f, glyph.Size() / 2, new Vector2(gw * pulse, gw * 0.35f * pulse), SpriteEffects.None, 0);
                //柱位预示:一根极淡的细光柱(读招指向,亮度远低于爆发)
                Texture2D pixel = TextureAssets.MagicPixel.Value;
                sb.Draw(pixel, new Rectangle((int)(basePos.X - 3), (int)(basePos.Y - PillarHeight), 6, (int)PillarHeight),
                    new Color(170, 90, 255) * (0.1f + 0.18f * warnP));
                sb.End();
                sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
                return false;
            }

            //爆发:VInvPillar 柱体(生长包络 + 首尾亮度包络)
            float life = Projectile.timeLeft / (float)EruptTime;
            float fade = MathHelper.Clamp(life * 3.4f, 0f, 1f);
            float rise = RiseEase();
            Texture2D noise = CEUtils.getExtraTex("TurbulentNoise");

            Effect pillarFx = CEEffectAssets.VInvPillar;
            pillarFx.Parameters["uTime"].SetValue(Main.GlobalTimeWrappedHourly * 1.2f + Projectile.whoAmI * 0.83f);
            pillarFx.Parameters["uGrow"].SetValue(rise);
            pillarFx.Parameters["uOpacity"].SetValue(fade);
            pillarFx.Parameters["uColorCore"].SetValue(ColorCore);
            pillarFx.Parameters["uColorEdge"].SetValue(ColorEdge);

            float quadW = PillarWidth * 2.2f;
            Vector2 scale = new Vector2(quadW / noise.Width, PillarHeight / noise.Height);

            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.LinearWrap, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            pillarFx.CurrentTechnique.Passes[0].Apply();
            //origin 挂柱底中心(uv.y=1 是柱底)
            sb.Draw(noise, basePos, null, Color.White, 0f, new Vector2(noise.Width / 2f, noise.Height), scale, SpriteEffects.None, 0);

            //基座辉光 + 顶端光冠
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            Texture2D glow = glowTex.Value;
            sb.Draw(glow, basePos, null, new Color(200, 120, 255) * fade, 0, glow.Size() / 2, 1.5f, SpriteEffects.None, 0);
            Vector2 crownPos = basePos + new Vector2(0, -PillarHeight * rise);
            sb.Draw(glow, crownPos, null, new Color(235, 200, 255) * (0.9f * fade), 0, glow.Size() / 2, 1.1f, SpriteEffects.None, 0);
            sb.Draw(glow, crownPos, null, Color.White * (0.5f * fade), 0, glow.Size() / 2, 0.55f, SpriteEffects.None, 0);

            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            return false;
        }
    }
}
