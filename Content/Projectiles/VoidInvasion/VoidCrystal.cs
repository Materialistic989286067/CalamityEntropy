using CalamityEntropy.Assets.Register;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using CalamityEntropy.Core.Graphics;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Projectiles.VoidInvasion
{
    /// <summary>
    /// 虚空水晶(void-invasion.md §3.2):虚熵魔物的统一弹幕语言。自旋。
    /// ai[0] = 模式:0 = 扇射(直飞 60t 后受 0.08 重力,落地碎裂),1 = 环形/蜕晶(缓速直飞,轻微减速)。
    /// 年龄一律由 timeLeft 反推,双端天然同步,不依赖本地计数。
    /// 绘制:FiendCrystal 棱光着色器(伪折射/内辉光/棱面扫光)+ oldPos 加法拖尾。
    /// </summary>
    public class VoidCrystal : ModProjectile
    {
        /// <summary>扇射模式总寿命(用于反推年龄)</summary>
        public const int FanLifetime = 300;
        /// <summary>环形模式总寿命</summary>
        public const int RingLifetime = 240;
        /// <summary>扇射模式直飞段时长(§3.2:1s 后开始下坠)</summary>
        public const int StraightTime = 60;

        public bool RingMode => Projectile.ai[0] == 1;
        public int Age => (RingMode ? RingLifetime : FanLifetime) - Projectile.timeLeft;

        public override void SetStaticDefaults()
        {
            //拖尾缓存:位置+旋转双缓存,棱光拖尾用
            ProjectileID.Sets.TrailCacheLength[Type] = 8;
            ProjectileID.Sets.TrailingMode[Type] = 2;
        }

        public override void SetDefaults()
        {
            Projectile.width = 24;
            Projectile.height = 24;
            Projectile.friendly = false;
            Projectile.hostile = true;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.timeLeft = FanLifetime;
            Projectile.light = 0.5f;
        }

        public override void AI()
        {
            if (Projectile.localAI[0] == 0)
            {
                Projectile.localAI[0] = 1;
                if (RingMode)
                {
                    Projectile.timeLeft = RingLifetime;
                }
            }
            //自旋(纯视觉,速度按模式区分:扇射快旋,环形慢旋)
            Projectile.rotation += RingMode ? 0.12f : 0.3f;

            if (RingMode)
            {
                //环形/蜕晶:缓速外扩,轻微减速拉开留白
                Projectile.velocity *= 0.995f;
            }
            else if (Age > StraightTime)
            {
                //扇射:直飞 60t 后受重力下坠,落地碎裂
                Projectile.velocity.Y += 0.08f;
                if (Projectile.velocity.Y > 18f)
                    Projectile.velocity.Y = 18f;
                Projectile.tileCollide = true;
            }

            if (!Main.dedServ && Main.rand.NextBool(3))
            {
                var p = PRTLoader.NewParticle<PRT_Light>(Projectile.Center + CEUtils.randomPointInCircle(8), -Projectile.velocity * 0.1f,
                    Main.rand.NextBool() ? new Color(150, 110, 255) : new Color(90, 60, 220), 0.4f);
                p.Configure(0.75f, lifetime: 16);
            }
        }

        public override void OnKill(int timeLeft)
        {
            if (Main.dedServ)
                return;
            Terraria.Audio.SoundEngine.PlaySound(SoundID.Item27 with { Volume = 0.5f, Pitch = 0.2f }, Projectile.Center);
            for (int i = 0; i < 8; i++)
            {
                var p = PRTLoader.NewParticle<PRT_Light>(Projectile.Center, CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(1f, 4f),
                    new Color(160, 120, 255), 0.5f);
                p.Configure(0.85f, lifetime: 18);
            }
            //碎晶四溅 + 一记小脉冲环:落点有"晶体碎裂"的实体感
            for (int i = 0; i < 5; i++)
            {
                PRTLoader.NewParticle<PRT_CrystalGlow>(Projectile.Center, CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(1.5f, 5f),
                    new Color(160, 120, 255), Main.rand.NextFloat(0.3f, 0.6f)).Configure(0.9f, true, PRTDrawModeEnum.AdditiveBlend, CEUtils.randomRot(), 22);
            }
            PRTLoader.NewParticle<PRT_PulseRing>(Projectile.Center, Vector2.Zero, new Color(150, 100, 255), 0.05f).Configure(0.7f, 18);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            SpriteBatch sb = Main.spriteBatch;
            Texture2D tex = Terraria.GameContent.TextureAssets.Projectile[Projectile.type].Value;
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);

            //飞行拖尾:预乘批次下 alpha=0 即纯加法,渐细渐暗(着色器 Apply 前,走默认管线)
            for (int i = Projectile.oldPos.Length - 1; i >= 1; i--)
            {
                if (Projectile.oldPos[i] == Vector2.Zero)
                    continue;
                float k = 1f - i / (float)Projectile.oldPos.Length;
                Vector2 tp = Projectile.oldPos[i] + Projectile.Size / 2;
                sb.Draw(tex, tp - Main.screenPosition, null, new Color(110, 65, 245, 0) * (0.36f * k), Projectile.oldRot[i], tex.Size() / 2, Projectile.scale * (0.55f + 0.45f * k), SpriteEffects.None, 0);
            }
            //辉光底衬
            Texture2D glow = CEExtraAssets.Glow2;
            sb.Draw(glow, Projectile.Center - Main.screenPosition, null, new Color(120, 70, 255, 0) * 0.5f, 0, glow.Size() / 2, 0.5f, SpriteEffects.None, 0);

            //主体:棱光着色器,种子按弹幕编号错相,防整屏同步闪烁
            Effect fx = CEFxcEffects.Get("FiendCrystal");
            fx.CurrentTechnique = fx.Techniques["Technique1"];
            fx.Parameters["time"].SetValue(Main.GlobalTimeWrappedHourly);
            fx.Parameters["seed"].SetValue(Projectile.identity * 0.37f % 10f);
            fx.Parameters["alpha"].SetValue(1f);
            fx.Parameters["glowPulse"].SetValue(0.35f);
            fx.Parameters["glowColor"].SetValue(new Vector4(0.55f, 0.3f, 1.1f, 0f));
            fx.Parameters["noiseTex"].SetValue(CEExtraAssets.Perlin);
            fx.CurrentTechnique.Passes[0].Apply();
            sb.Draw(tex, Projectile.Center - Main.screenPosition, null, Color.White, Projectile.rotation, tex.Size() / 2, Projectile.scale, SpriteEffects.None, 0);

            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            return false;
        }
    }
}
