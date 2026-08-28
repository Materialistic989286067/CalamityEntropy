using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Projectiles.VoidInvasion
{
    /// <summary>
    /// 虚空火球(void-invasion.md §2.5):烛灵远程抛射,0.1 重力抛物线,落地生成 2 秒残焰判定。
    /// 演出 = 流星式拉伸弹头(白芯 + 紫晕沿速度方向拉长)+ 焰尾,落地有方向性炸开。
    /// </summary>
    public class VoidFireball : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Content/Particles/PRT_Light";

        public override void SetDefaults()
        {
            Projectile.width = 22;
            Projectile.height = 22;
            Projectile.friendly = false;
            Projectile.hostile = true;
            Projectile.penetrate = -1;
            Projectile.tileCollide = true;
            Projectile.timeLeft = 360;
            Projectile.light = 0.6f;
        }

        public override void AI()
        {
            //§2.5:0.1 重力抛物线
            Projectile.velocity.Y += 0.1f;
            if (Projectile.velocity.Y > 16f)
                Projectile.velocity.Y = 16f;
            Projectile.rotation = Projectile.velocity.ToRotation();

            if (!Main.dedServ)
            {
                Color c = Main.rand.NextBool() ? new Color(190, 90, 255) : new Color(120, 40, 200);
                var p = PRTLoader.NewParticle<PRT_Light>(Projectile.Center + CEUtils.randomPointInCircle(6), -Projectile.velocity * 0.15f, c, 0.55f);
                p.Configure(0.85f, lifetime: 20);
                //焰尾:低频火团回甩(流星尾)
                if (Main.rand.NextBool(3))
                {
                    var f = PRTLoader.NewParticle<PRT_FlameCal>(Projectile.Center - Projectile.velocity * 0.5f,
                        -Projectile.velocity * 0.1f, new Color(190, 90, 255), Main.rand.NextFloat(0.35f, 0.55f));
                    f.Configure(16, 1f, new Color(70, 20, 120));
                }
            }
        }

        public override void OnKill(int timeLeft)
        {
            if (!Main.dedServ)
            {
                //落点炸开:白闪 + 迎速度方向的溅射扇 + 贴地环
                var flash = PRTLoader.NewParticle<PRT_BloomCal>(Projectile.Center, Vector2.Zero, new Color(230, 180, 255), 0.25f);
                flash.Configure(1.3f, 10);
                Vector2 back = (-Projectile.velocity).SafeNormalize(-Vector2.UnitY);
                for (int i = 0; i < 10; i++)
                {
                    Vector2 vel = back.RotatedBy(Main.rand.NextFloat(-0.9f, 0.9f)) * Main.rand.NextFloat(2f, 6.5f);
                    var s = PRTLoader.NewParticle<PRT_GlowSparkCal>(Projectile.Center, vel,
                        new Color(200, 110, 255), Main.rand.NextFloat(0.3f, 0.55f));
                    s.Configure(true, 22, new Vector2(0.5f, 1.6f), quickShrink: true);
                }
                for (int i = 0; i < 10; i++)
                {
                    var p = PRTLoader.NewParticle<PRT_Light>(Projectile.Center, CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(1f, 5f), new Color(190, 90, 255), 0.6f);
                    p.Configure(0.9f, lifetime: 22);
                }
                var ring = PRTLoader.NewParticle<PRT_DirectionalPulseRing>(Projectile.Center, Vector2.Zero, new Color(200, 120, 255), 0.12f);
                ring.Configure(new Vector2(1.4f, 0.55f), 0f, 1.3f, 14);
            }
            //落地残焰(2s 判定),伤害沿用火球档
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                Projectile.NewProjectile(Projectile.GetSource_FromThis(), Projectile.Center, Vector2.Zero,
                    ModContent.ProjectileType<VoidGroundFlame>(), Projectile.damage, 0, -1);
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            SpriteBatch sb = Main.spriteBatch;
            Texture2D tex = Terraria.GameContent.TextureAssets.Projectile[Projectile.type].Value;
            Vector2 drawPos = Projectile.Center - Main.screenPosition;
            float rot = Projectile.rotation;
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            float pulse = 1f + 0.15f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 15f + Projectile.whoAmI);
            //流星弹头:外晕沿速度拉长,中层紫,白芯收尖
            sb.Draw(tex, drawPos, null, new Color(120, 40, 200) * 0.7f, rot, tex.Size() / 2, new Vector2(1.7f, 0.85f) * 0.85f * pulse, SpriteEffects.None, 0);
            sb.Draw(tex, drawPos, null, new Color(210, 130, 255), rot, tex.Size() / 2, new Vector2(1.3f, 0.62f) * 0.7f * pulse, SpriteEffects.None, 0);
            sb.Draw(tex, drawPos, null, new Color(255, 235, 255), rot, tex.Size() / 2, new Vector2(0.9f, 0.4f) * 0.55f * pulse, SpriteEffects.None, 0);
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            return false;
        }
    }

    /// <summary>
    /// 虚空残焰:火球落地留下的地面灼烧判定,存在 2 秒,纯粒子表现(舔地火舌 + 光点)。
    /// </summary>
    public class VoidGroundFlame : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Assets/Extra/Ports/Invisible";

        public override void SetDefaults()
        {
            Projectile.width = 56;
            Projectile.height = 40;
            Projectile.friendly = false;
            Projectile.hostile = true;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.timeLeft = 120;
            Projectile.light = 0.4f;
        }

        public override void AI()
        {
            Projectile.velocity = Vector2.Zero;
            if (Main.dedServ)
                return;
            //残焰:向上舔舐的紫焰(火团为主体,光点为飞溅),尾段渐弱
            float strength = Math.Min(1f, Projectile.timeLeft / 30f);
            var flame = PRTLoader.NewParticle<PRT_FlameCal>(
                Projectile.Center + new Vector2(Main.rand.NextFloat(-24f, 24f), Main.rand.NextFloat(4f, 14f)),
                new Vector2(Main.rand.NextFloat(-0.3f, 0.3f), -Main.rand.NextFloat(1.5f, 3f)),
                new Color(190, 90, 255) * strength, Main.rand.NextFloat(0.4f, 0.7f) * strength);
            flame.Configure(18, 1f, new Color(70, 20, 120));
            if (Main.rand.NextBool(2))
            {
                Vector2 pos = Projectile.Center + new Vector2(Main.rand.NextFloat(-24f, 24f), Main.rand.NextFloat(6f, 16f));
                Color c = Main.rand.NextBool() ? new Color(190, 90, 255) : new Color(255, 150, 230);
                var p = PRTLoader.NewParticle<PRT_Light>(pos, new Vector2(0, -Main.rand.NextFloat(1.2f, 2.6f)), c * strength, Main.rand.NextFloat(0.3f, 0.5f) * strength);
                p.Configure(0.8f * strength, lifetime: 18);
            }
        }
    }
}
