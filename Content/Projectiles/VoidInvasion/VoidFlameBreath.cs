using CalamityEntropy.Content.Particles.CalamityPorts;
using CalamityEntropy.Core.Graphics;
using InnoVault;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using Terraria;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Projectiles.VoidInvasion
{
    /// <summary>
    /// 虚空喷焰(void-invasion.md §2.5,通用件):锥形持续火流,吸附在 ai[0] 指定的 NPC 上,
    /// 喷射方向缓慢转向其目标。烛灵 scale=1 原样用;M3 爬行者按 §2.7 放大 1.5 复用。
    /// 火体 = VInvFlame 着色器(双层噪声外流 + 锥形蒙版侵蚀出火舌 + 三段色温),
    /// 粒子只做余烬点缀;起手 8t 火舌生长,收尾 8t 渐熄。
    /// 放大走 ai[1](原生随生成包同步;直接改 Projectile.scale 不进同步载荷,联机端会回落 1)。
    /// 命中玩家把无敌帧压到 10t(低单跳持续烧灼);时长由生成侧的 timeLeft 决定。
    /// </summary>
    public class VoidFlameBreath : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Assets/Extra/Ports/Invisible";

        //喷口辉光贴图只在绘制路径读取(服务器恒 null)
        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light2")]
        private static Asset<Texture2D> mouthGlowTex;

        /// <summary>喷射源 NPC 的 whoAmI(生成时写入 ai[0])</summary>
        public int OwnerIndex => (int)Projectile.ai[0];
        /// <summary>锥长(锥形判定与火舌绘制共用)</summary>
        public float ConeLength => 260f * Projectile.scale;

        //---- 可调色板(VInvFlame 三段色温) ----
        private static readonly Vector3 ColorCore = new Vector3(0.96f, 0.86f, 1f);
        private static readonly Vector3 ColorMid = new Vector3(0.7f, 0.32f, 1f);
        private static readonly Vector3 ColorEdge = new Vector3(0.3f, 0.09f, 0.52f);

        //喷焰已持续的 tick(localAI 只在本端推进,纯视觉包络)
        private float Age => Projectile.localAI[1];

        public override void SetDefaults()
        {
            Projectile.width = 30;
            Projectile.height = 30;
            Projectile.friendly = false;
            Projectile.hostile = true;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.timeLeft = 50;
        }

        public override void AI()
        {
            NPC owner = OwnerIndex >= 0 && OwnerIndex < Main.maxNPCs ? Main.npc[OwnerIndex] : null;
            if (owner == null || !owner.active)
            {
                Projectile.Kill();
                return;
            }

            //首帧:以生成速度方向为初始喷射角,之后清零速度改为吸附;ai[1] 是净同步安全的放大口
            if (Projectile.localAI[0] == 0)
            {
                Projectile.localAI[0] = 1;
                Projectile.rotation = Projectile.velocity.ToRotation();
                Projectile.velocity = Vector2.Zero;
                if (Projectile.ai[1] > 0)
                {
                    Projectile.scale = Projectile.ai[1];
                }
            }
            Projectile.localAI[1]++;

            //缓慢转向源 NPC 的目标(两端各自确定性推导,目标索引由原生 npc.target 同步)
            if (owner.HasValidTarget)
            {
                float aim = (Main.player[owner.target].Center - owner.Center).ToRotation();
                Projectile.rotation = Projectile.rotation.AngleTowards(aim, 0.03f);
            }
            Vector2 dir = Projectile.rotation.ToRotationVector2();
            Projectile.Center = owner.Center + dir * (owner.width * 0.5f + 8f);

            Lighting.AddLight(Projectile.Center + dir * ConeLength * 0.4f, 0.55f, 0.22f, 0.8f);
            Lighting.AddLight(Projectile.Center + dir * ConeLength * 0.85f, 0.3f, 0.1f, 0.45f);

            if (Main.dedServ)
                return;

            //余烬点缀:沿锥心飘散的光星与偶发火团(主体火舌在着色器里)
            if (Main.rand.NextBool(2))
            {
                float along = Main.rand.NextFloat(0.2f, 0.95f);
                Vector2 pos = Projectile.Center + dir * ConeLength * along
                    + dir.RotatedBy(MathHelper.PiOver2) * Main.rand.NextFloat(-1f, 1f) * 55f * along * Projectile.scale;
                var s = PRTLoader.NewParticle<PRT_GlowSparkCal>(pos, dir * Main.rand.NextFloat(4f, 9f) * Projectile.scale,
                    new Color(215, 140, 255), Main.rand.NextFloat(0.3f, 0.5f) * Projectile.scale);
                s.Configure(false, 16, new Vector2(0.5f, 1.6f), quickShrink: true);
            }
            if (Main.rand.NextBool(6))
            {
                var f = PRTLoader.NewParticle<PRT_FlameCal>(Projectile.Center + dir * 30f * Projectile.scale + CEUtils.randomPointInCircle(8f),
                    dir * Main.rand.NextFloat(7f, 11f) * Projectile.scale, new Color(190, 90, 255), Main.rand.NextFloat(0.4f, 0.7f) * Projectile.scale);
                f.Configure(20, 1f, new Color(70, 20, 120));
            }
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            //锥形判定:沿喷射方向取样圆,半径随距离增大(近窄远宽)
            Vector2 origin = Projectile.Center;
            Vector2 dir = Projectile.rotation.ToRotationVector2();
            float len = ConeLength;
            for (float d = 16f; d <= len; d += 22f)
            {
                float r = (10f + 80f * (d / len)) * Projectile.scale;
                Vector2 p = origin + dir * d;
                if (targetHitbox.Intersects(Utils.CenteredRectangle(p, new Vector2(r * 2f))))
                    return true;
            }
            return false;
        }

        public override void OnHitPlayer(Player target, Player.HurtInfo info)
        {
            //低单跳:把本次命中的无敌帧压到 10t,站在火流里会被持续烧灼(§2.5)
            if (target.immuneTime > 10)
                target.immuneTime = 10;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            //起手生长 / 收尾渐熄的包络
            float reach = MathHelper.Clamp(Age / 8f, 0f, 1f);
            float fade = MathHelper.Clamp(Projectile.timeLeft / 8f, 0f, 1f);
            if (reach <= 0.01f || fade <= 0.01f)
                return false;

            SpriteBatch sb = Main.spriteBatch;
            Texture2D noise = CEUtils.getExtraTex("TurbulentNoise");

            Effect flameFx = CEFxcEffects.Get("VInvFlame");
            flameFx.Parameters["uTime"].SetValue(Main.GlobalTimeWrappedHourly * 1.15f + Projectile.whoAmI * 1.3f);
            flameFx.Parameters["uReach"].SetValue(reach);
            flameFx.Parameters["uOpacity"].SetValue(fade);
            flameFx.Parameters["uColorCore"].SetValue(ColorCore);
            flameFx.Parameters["uColorMid"].SetValue(ColorMid);
            flameFx.Parameters["uColorEdge"].SetValue(ColorEdge);

            //火舌矩形:锥长 × 双侧口径,origin 挂喷口左中,沿喷射角旋转
            float quadLen = ConeLength * 1.08f;
            float quadWidth = 210f * Projectile.scale;
            Vector2 scale = new Vector2(quadLen / noise.Width, quadWidth / noise.Height);

            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.LinearWrap, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            flameFx.CurrentTechnique.Passes[0].Apply();
            sb.Draw(noise, Projectile.Center - Main.screenPosition, null, Color.White, Projectile.rotation,
                new Vector2(0, noise.Height / 2f), scale, SpriteEffects.None, 0);

            //喷口辉光(火根最亮)
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            Texture2D glow = mouthGlowTex.Value;
            float flick = 1f + 0.12f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 26f + Projectile.whoAmI);
            sb.Draw(glow, Projectile.Center - Main.screenPosition, null, new Color(200, 120, 255) * (0.85f * fade),
                0, glow.Size() / 2, 0.75f * Projectile.scale * flick * reach, SpriteEffects.None, 0);

            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            return false;
        }
    }
}
