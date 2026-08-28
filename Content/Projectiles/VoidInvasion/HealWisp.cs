using CalamityEntropy.Common;
using CalamityEntropy.Content.NPCs.VoidInvasion;
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
    /// 治疗光灵(void-invasion.md §2.2):虚空术士治疗咒术放出,飞向 ai[0] 指定的事件怪,
    /// 命中回复其 20% 最大生命。敌对阵营弹幕但不判伤、不可被打掉;回血仅服务端结算,
    /// 同目标 8 秒内不重复(锁在 EGlobalNPC.voidHealCd)。
    /// </summary>
    public class HealWisp : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Content/Particles/PRT_Light";

        /// <summary>目标 NPC 的 whoAmI(生成时写入 ai[0],服务端选取)</summary>
        public int TargetIndex => (int)Projectile.ai[0];

        public override void SetDefaults()
        {
            Projectile.width = 18;
            Projectile.height = 18;
            Projectile.friendly = false;
            Projectile.hostile = false;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.timeLeft = 360;
            Projectile.light = 0.5f;
        }

        public override void AI()
        {
            NPC target = TargetIndex >= 0 && TargetIndex < Main.maxNPCs ? Main.npc[TargetIndex] : null;
            bool valid = target != null && target.active && (target.ModNPC is VoidCultist || target.ModNPC is IVoidInvasionNPC);
            if (valid)
            {
                //缓转追踪:先阻尼再朝目标加速,轨迹带一点弧度
                Vector2 dir = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitY);
                Vector2 v = Projectile.velocity * 0.93f + dir * 1.6f;
                if (v.Length() > 14f)
                    v = v.SafeNormalize(Vector2.UnitY) * 14f;
                Projectile.velocity = v;

                if (Projectile.Center.Distance(target.Center) < 26f)
                {
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        var g = target.Entropy();
                        if (g.voidHealCd <= 0 && target.life < target.lifeMax)
                        {
                            int heal = Math.Min((int)(target.lifeMax * 0.2f), target.lifeMax - target.life);
                            target.life += heal;
                            g.voidHealCd = 480;
                            target.HealEffect(heal, true);
                            target.netUpdate = true;
                        }
                        Projectile.Kill();
                    }
                    return;
                }
            }
            else
            {
                //目标失效:漂浮减速等超时消失
                Projectile.velocity *= 0.95f;
                if (Projectile.timeLeft > 40)
                    Projectile.timeLeft = 40;
            }

            if (!Main.dedServ)
            {
                //绿紫双色光点拖尾(§2.2 配色)+ 双星环绕(治疗灵的"精灵感")
                if (Main.rand.NextBool(2))
                {
                    Color c = Main.rand.NextBool() ? new Color(110, 255, 150) : new Color(190, 110, 255);
                    var p = PRTLoader.NewParticle<PRT_Light>(Projectile.Center + CEUtils.randomPointInCircle(8), -Projectile.velocity * 0.1f, c, 0.5f);
                    p.Configure(0.8f, lifetime: 24);
                }
                if (Main.rand.NextBool(5))
                {
                    var sp = PRTLoader.NewParticle<PRT_SparkleCal>(Projectile.Center + CEUtils.randomPointInCircle(12f),
                        -Projectile.velocity * 0.05f, new Color(180, 255, 200), 0.45f);
                    sp.Configure(new Color(110, 255, 150), 20, 0.1f, 1.1f);
                }
                float orbit = Main.GlobalTimeWrappedHourly * 6f + Projectile.whoAmI;
                for (int i = 0; i < 2; i++)
                {
                    Vector2 orbitPos = Projectile.Center + (orbit + i * MathHelper.Pi).ToRotationVector2() * 16f;
                    var mote = PRTLoader.NewParticle<PRT_Light>(orbitPos, Projectile.velocity * 0.55f,
                        i == 0 ? new Color(110, 255, 150) : new Color(190, 110, 255), 0.3f);
                    mote.Configure(0.7f, lifetime: 8);
                }
            }
        }

        public override void OnKill(int timeLeft)
        {
            if (Main.dedServ)
                return;
            for (int i = 0; i < 14; i++)
            {
                Color c = i % 2 == 0 ? new Color(110, 255, 150) : new Color(190, 110, 255);
                var p = PRTLoader.NewParticle<PRT_Light>(Projectile.Center, CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(1f, 4f), c, 0.6f);
                p.Configure(0.9f, lifetime: 26);
            }
            //触达拍:目标身上收一圈绿环 + 上升治愈星(回血的视觉落点在被治者)
            NPC target = TargetIndex >= 0 && TargetIndex < Main.maxNPCs ? Main.npc[TargetIndex] : null;
            if (target != null && target.active)
            {
                var ring = PRTLoader.NewParticle<PRT_PulseRing>(target.Center, Vector2.Zero, new Color(120, 255, 160), 0.12f);
                ring.Configure(Math.Max(target.width, target.height) / 60f + 0.5f, 18);
                for (int i = 0; i < 5; i++)
                {
                    var sp = PRTLoader.NewParticle<PRT_SparkleCal>(target.Center + CEUtils.randomPointInCircle(target.width * 0.45f),
                        new Vector2(Main.rand.NextFloat(-0.5f, 0.5f), -Main.rand.NextFloat(1.5f, 3f)), new Color(160, 255, 190), 0.55f);
                    sp.Configure(new Color(110, 255, 150), 24, 0.08f, 1.2f);
                }
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            SpriteBatch sb = Main.spriteBatch;
            Texture2D tex = Terraria.GameContent.TextureAssets.Projectile[Projectile.type].Value;
            Vector2 drawPos = Projectile.Center - Main.screenPosition;
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            float pulse = 1f + 0.15f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 12f);
            //十字星芒(纵横两道细光)+ 双层光核
            sb.Draw(tex, drawPos, null, new Color(150, 255, 180) * 0.55f, 0, tex.Size() / 2, new Vector2(1.6f, 0.18f) * pulse, SpriteEffects.None, 0);
            sb.Draw(tex, drawPos, null, new Color(150, 255, 180) * 0.55f, 0, tex.Size() / 2, new Vector2(0.18f, 1.6f) * pulse, SpriteEffects.None, 0);
            sb.Draw(tex, drawPos, null, new Color(190, 110, 255) * 0.7f, 0, tex.Size() / 2, 0.8f * pulse, SpriteEffects.None, 0);
            sb.Draw(tex, drawPos, null, new Color(120, 255, 160), 0, tex.Size() / 2, 0.5f * pulse, SpriteEffects.None, 0);
            sb.Draw(tex, drawPos, null, Color.White * 0.85f, 0, tex.Size() / 2, 0.26f * pulse, SpriteEffects.None, 0);
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            return false;
        }
    }
}
