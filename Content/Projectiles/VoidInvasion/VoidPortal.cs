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
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Projectiles.VoidInvasion
{
    /// <summary>
    /// 虚空传送门(void-invasion.md §0.3 支柱 1,全事件通用演出件):真正的虚空裂隙。
    /// 门面 = VInvPortal 着色器(双层视差涡流深渊 + 中心黑洞 + 噪声撕裂边缘光),
    /// 外层叠法阵环与辉光;开门有汇聚前摇与撕裂冲击拍(速度线 + 轻震屏),关门向内塌缩收白闪。
    /// 无判定,纯演出;所有"从门里出怪"的招式共用它。
    /// 用法(M3 掠食者/噬虚鲨、M4 主教投放、M6+ 教皇门中蠕虫):
    ///   服务端调 <see cref="Open"/>,传入位置、开口朝向(出怪方向)、存活时长与缩放;
    ///   门张开有 <see cref="OpenTime"/> 前摇渐大,关闭前 <see cref="CloseTime"/> 渐小,
    ///   出怪拍应安排在"生成后 OpenTime"之后。
    /// ai[0] = 存活总时长(tick),ai[1] = 最大缩放(0 视为 1)。
    /// </summary>
    public class VoidPortal : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Content/Projectiles/VoidInvasion/VoidGlyph";

        //辉光贴图只在绘制路径读取(服务器恒 null)
        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light")]
        private static Asset<Texture2D> glowTex;

        /// <summary>张开前摇:渐大到满尺寸的时长</summary>
        public const int OpenTime = 40;
        /// <summary>关闭收尾:渐小到消失的时长</summary>
        public const int CloseTime = 20;
        /// <summary>椭圆压扁比(沿开口轴)</summary>
        public const float Squash = 0.35f;
        /// <summary>门面基准半径(px,缩放 1 时;涡流盘、粒子环带与开口冲击共用)</summary>
        public const float BaseRadius = 118f;

        //---- 可调色板(裂隙视觉语言,同族招式引用同源) ----
        private static readonly Vector3 ColorDeep = new Vector3(0.07f, 0.015f, 0.13f);
        private static readonly Vector3 ColorMid = new Vector3(0.47f, 0.18f, 0.82f);
        private static readonly Vector3 ColorRim = new Vector3(0.88f, 0.59f, 1f);

        public float Lifetime => Projectile.ai[0];
        public float MaxScale => Projectile.ai[1] <= 0 ? 1f : Projectile.ai[1];

        /// <summary>
        /// 服务端开门入口。pos = 门心,facing = 开口朝向(出怪方向),lifetime = 存活 tick,scale = 视觉缩放。
        /// 多人客户端上调用无效并返回 null。
        /// </summary>
        public static Projectile Open(Terraria.DataStructures.IEntitySource source, Vector2 pos, Vector2 facing, int lifetime, float scale = 1f)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return null;
            //朝向借初速度通道原生同步,首帧转存 rotation 后清零
            int index = Projectile.NewProjectile(source, pos, facing.SafeNormalize(Vector2.UnitX) * 0.02f,
                ModContent.ProjectileType<VoidPortal>(), 0, 0, -1, lifetime, scale);
            return index >= 0 && index < Main.maxProjectiles ? Main.projectile[index] : null;
        }

        public override void SetDefaults()
        {
            Projectile.width = 10;
            Projectile.height = 10;
            Projectile.friendly = false;
            Projectile.hostile = false;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.timeLeft = 300;
        }

        private float Age => Lifetime - Projectile.timeLeft;

        /// <summary>当前视觉缩放(张开/关闭包络 × MaxScale)</summary>
        public float VisualScale
        {
            get
            {
                float open = MathHelper.Clamp(Age / OpenTime, 0f, 1f);
                float close = MathHelper.Clamp(Projectile.timeLeft / (float)CloseTime, 0f, 1f);
                //开门三次缓出 + 张满前的轻微过冲;关门幂缓(先慢后骤,向内塌缩感)
                float openEase = 1f - (1f - open) * (1f - open) * (1f - open);
                openEase += 0.09f * (float)Math.Sin(open * MathHelper.Pi) * (1f - open);
                float closeEase = (float)Math.Pow(close, 0.6);
                return MaxScale * openEase * closeEase;
            }
        }

        public override void AI()
        {
            if (Projectile.localAI[0] == 0)
            {
                Projectile.localAI[0] = 1;
                Projectile.rotation = Projectile.velocity.ToRotation();
                Projectile.velocity = Vector2.Zero;
                Projectile.timeLeft = Math.Max(2, (int)Lifetime);
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.Item8 with { Pitch = -0.4f }, Projectile.Center);
                }
            }
            //自旋计时(纯视觉)
            Projectile.localAI[1] += 0.045f;

            float vs = VisualScale;
            Lighting.AddLight(Projectile.Center, 0.7f * vs, 0.3f * vs, 1.05f * vs);

            if (Main.dedServ)
                return;

            float age = Age;
            bool closing = Projectile.timeLeft <= CloseTime;

            //开门前摇:空间被撕开前先向门心倒吸(汇聚线,因果拍的"因")
            if (age < OpenTime && !closing)
            {
                for (int i = 0; i < 2; i++)
                {
                    Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(150f, 240f) * MaxScale;
                    var p = PRTLoader.NewParticle<PRT_LineCal>(Projectile.Center + offset, -offset * 0.075f,
                        new Color(190, 110, 255), Main.rand.NextFloat(0.5f, 0.85f));
                    p.Configure(false, 15);
                }
            }

            //撕裂冲击拍(张满瞬间):径向速度线 + 白闪 + 沿开口轴的冲击环 + 轻震屏
            //(60t 短门在 age==40 时恰好踩进关门窗,这里只排除临死门,不吃 closing)
            if ((int)age == OpenTime && Projectile.timeLeft > 4)
            {
                SoundEngine.PlaySound(SoundID.Item72 with { Pitch = -0.35f, Volume = 0.9f }, Projectile.Center);
                CEUtils.SetShake(Projectile.Center, 5f * MaxScale, 1400);
                for (int i = 0; i < 16; i++)
                {
                    Vector2 dir = CEUtils.randomRot().ToRotationVector2();
                    var p = PRTLoader.NewParticle<PRT_LineCal>(Projectile.Center + dir * 40f * MaxScale,
                        dir * Main.rand.NextFloat(13f, 22f), new Color(225, 160, 255), Main.rand.NextFloat(0.7f, 1.2f));
                    p.Configure(false, 13);
                }
                var flash = PRTLoader.NewParticle<PRT_BloomCal>(Projectile.Center, Vector2.Zero, Color.White, 0.5f * MaxScale);
                flash.Configure(2.6f * MaxScale, 12);
                var ring = PRTLoader.NewParticle<PRT_DirectionalPulseRing>(Projectile.Center, Vector2.Zero,
                    new Color(200, 120, 255), 0.25f * MaxScale);
                ring.Configure(new Vector2(Squash, 1f), Projectile.rotation, 3.2f * MaxScale, 20);
            }

            if (vs <= 0.05f)
                return;

            //常态环带:粒子内旋(椭圆缘取点,切向 + 向心);关门期改为全力倒吸
            if (Main.rand.NextBool(2))
            {
                float ang = CEUtils.randomRot();
                Vector2 rim = ang.ToRotationVector2() * BaseRadius * vs;
                //沿开口轴压扁到椭圆
                Vector2 axis = Projectile.rotation.ToRotationVector2();
                Vector2 flat = axis * Vector2.Dot(rim, axis) * Squash + new Vector2(-axis.Y, axis.X) * Vector2.Dot(rim, new Vector2(-axis.Y, axis.X));
                Vector2 pos = Projectile.Center + flat;
                Vector2 tangent = new Vector2(-flat.Y, flat.X).SafeNormalize(Vector2.UnitX);
                Vector2 vel = closing ? -flat * 0.09f : tangent * 2.2f - flat * 0.022f;
                var p = PRTLoader.NewParticle<PRT_Void>(pos, vel, Color.White, 1f);
                p.Opacity = 0.65f;
            }
            //边缘撕裂火花(低频,撑住裂隙"还在撕"的观感)
            if (Main.rand.NextBool(7))
            {
                float ang = CEUtils.randomRot();
                Vector2 rim = ang.ToRotationVector2() * BaseRadius * vs * 0.95f;
                Vector2 tangent = new Vector2(-rim.Y, rim.X).SafeNormalize(Vector2.UnitX) * Main.rand.NextFloat(3f, 6f);
                var s = PRTLoader.NewParticle<PRT_GlowSparkCal>(Projectile.Center + rim, tangent,
                    new Color(230, 170, 255), Main.rand.NextFloat(0.25f, 0.45f));
                s.Configure(false, 18, new Vector2(0.5f, 1.7f), quickShrink: true);
            }
            //关门收白闪(塌缩到点)
            if (Projectile.timeLeft == 2)
            {
                var flash = PRTLoader.NewParticle<PRT_BloomCal>(Projectile.Center, Vector2.Zero, Color.White, 0.3f * MaxScale);
                flash.Configure(1.6f * MaxScale, 10);
                CEUtils.SetShake(Projectile.Center, 2.5f * MaxScale, 1000);
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            float vs = VisualScale;
            if (vs <= 0.01f)
                return false;
            SpriteBatch sb = Main.spriteBatch;
            Texture2D glyph = Terraria.GameContent.TextureAssets.Projectile[Projectile.type].Value;
            Texture2D noise = CEUtils.getExtraTex("TurbulentNoise");
            Vector2 screenPos = Projectile.Center - Main.screenPosition;
            float facing = Projectile.rotation;

            //屏幕空间沿开口轴压扁矩阵:涡流与法阵都画在固定椭圆里
            Matrix squashM =
                Matrix.CreateTranslation(-screenPos.X, -screenPos.Y, 0)
                * Matrix.CreateRotationZ(-facing)
                * Matrix.CreateScale(Squash, 1f, 1f)
                * Matrix.CreateRotationZ(facing)
                * Matrix.CreateTranslation(screenPos.X, screenPos.Y, 0)
                * Main.GameViewMatrix.TransformationMatrix;

            //---- 第一层:裂隙涡流盘(NonPremultiplied,深渊要能压暗背景) ----
            float age = Age;
            //张满后 10t 内的过曝衰减(撕裂冲击的余晖)
            float boost = 1f + 1.1f * MathHelper.Clamp(1f - (age - OpenTime) / 10f, 0f, 1f) * (age >= OpenTime ? 1f : 0f);
            //开门期涡流从暗处显形
            float discAlpha = MathHelper.Clamp(age / (OpenTime * 0.55f), 0f, 1f);

            Effect portalFx = CEEffectAssets.VInvPortal;
            portalFx.Parameters["uTime"].SetValue(Main.GlobalTimeWrappedHourly + Projectile.whoAmI * 2.7f);
            portalFx.Parameters["uOpacity"].SetValue(discAlpha);
            portalFx.Parameters["uBoost"].SetValue(boost);
            portalFx.Parameters["uColorDeep"].SetValue(ColorDeep);
            portalFx.Parameters["uColorMid"].SetValue(ColorMid);
            portalFx.Parameters["uColorRim"].SetValue(ColorRim);

            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.NonPremultiplied, SamplerState.LinearWrap, DepthStencilState.None, RasterizerState.CullNone, null, squashM);
            portalFx.CurrentTechnique.Passes[0].Apply();
            float discScale = BaseRadius * 2f * vs / noise.Width;
            sb.Draw(noise, screenPos, null, Color.White, 0f, noise.Size() / 2, discScale, SpriteEffects.None, 0);

            //---- 第二层:法阵环 + 辉光(Additive,同一压扁矩阵) ----
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, squashM);

            float spin = Projectile.localAI[1];
            Texture2D glow = glowTex.Value;
            //门心辉光(过曝拍一起吃 boost)
            sb.Draw(glow, screenPos, null, new Color(110, 40, 190) * (0.55f * boost), 0, glow.Size() / 2, vs * 2.1f, SpriteEffects.None, 0);
            //双层法阵反向自旋(压暗为涡流让位,保留全事件的法阵语言)
            sb.Draw(glyph, screenPos, null, new Color(190, 110, 255) * 0.6f, spin, glyph.Size() / 2, vs * 1.04f, SpriteEffects.None, 0);
            sb.Draw(glyph, screenPos, null, new Color(110, 40, 210) * 0.42f, -spin * 0.6f, glyph.Size() / 2, vs * 0.7f, SpriteEffects.None, 0);

            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            return false;
        }
    }
}
