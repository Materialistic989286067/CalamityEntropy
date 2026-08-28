using CalamityEntropy.Content.Particles;
using InnoVault;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Projectiles.VoidInvasion
{
    /// <summary>
    /// 魔焰弹(void-invasion.md §4.2,教皇 P2 通用小弹幕;魔眼弹.png 32x32):
    /// ai[0] = 模式:0 = 直飞(P2-4s 环爆收尾 / P2-6 眼吐);1 = 小幅度追踪(P2-4s 法球吐弹,
    /// 12px/t,转向 0.015rad/t,追 60t 后直飞);2 = 缓速追踪(§4.3 P3-5 终曲,M8:初速 8px/t 由生成侧给,
    /// 转向上限 0.03rad/t,寿命 6s,尾段 40t 直飞;timeLeft 双端首帧同式改写,不进生成包)。
    /// 方向与初速由生成侧给定,模式 0/1 4s 自毁。
    /// 双端确定性:追踪目标取距离最近的活玩家,双端同式推导,无自定义同步。
    /// </summary>
    public class MagicEyeBolt : ModProjectile
    {
        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light")]
        private static Asset<Texture2D> glowTex;

        public const int LifeTime = 240;
        public const int HomingTime = 60;
        public const float HomingTurn = 0.015f;
        /// <summary>模式 2 寿命(§4.3 P3-5:6s 命)</summary>
        public const int SlowLife = 360;
        /// <summary>模式 2 转向上限(§4.3 P3-5:0.03rad/t)</summary>
        public const float SlowTurn = 0.03f;

        public bool Homing => Projectile.ai[0] == 1;
        /// <summary>缓速追踪模式(P3-5 终曲)</summary>
        public bool SlowHoming => Projectile.ai[0] == 2;
        private float Age => LifeTime - Projectile.timeLeft;

        public override void SetDefaults()
        {
            Projectile.width = 22;
            Projectile.height = 22;
            Projectile.friendly = false;
            Projectile.hostile = true;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.timeLeft = LifeTime;
        }

        public override void AI()
        {
            //模式 2 首帧:寿命双端同式补长(timeLeft 不进生成包)
            if (SlowHoming && Projectile.localAI[0] == 0)
            {
                Projectile.localAI[0] = 1;
                Projectile.timeLeft = SlowLife;
            }
            //追踪段:朝最近玩家小幅转向,速度模保持(模式 2 全程追踪至尾段 40t 直飞)
            bool homingActive = (Homing && Age < HomingTime) || (SlowHoming && Projectile.timeLeft > 40);
            if (homingActive)
            {
                int idx = Player.FindClosest(Projectile.Center, 1, 1);
                if (idx >= 0)
                {
                    Player target = Main.player[idx];
                    float speed = Projectile.velocity.Length();
                    float want = (target.Center - Projectile.Center).ToRotation();
                    float cur = Projectile.velocity.ToRotation();
                    float turnCap = SlowHoming ? SlowTurn : HomingTurn;
                    float turn = MathHelper.Clamp(MathHelper.WrapAngle(want - cur), -turnCap, turnCap);
                    Projectile.velocity = (cur + turn).ToRotationVector2() * speed;
                }
            }
            Projectile.rotation = Projectile.velocity.ToRotation();
            Lighting.AddLight(Projectile.Center, 0.5f, 0.2f, 0.75f);

            if (!Main.dedServ && Main.rand.NextBool(3))
            {
                var p = PRTLoader.NewParticle<PRT_Light>(Projectile.Center - Projectile.velocity * 0.5f,
                    -Projectile.velocity * 0.1f, new Color(190, 90, 255), 0.35f);
                p.Configure(0.85f, lifetime: 12);
            }
        }

        public override void OnKill(int timeLeft)
        {
            if (Main.dedServ)
            {
                return;
            }
            for (int i = 0; i < 5; i++)
            {
                var v = PRTLoader.NewParticle<PRT_Void>(Projectile.Center,
                    CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(1f, 4f), Color.White, 0.8f);
                v.Opacity = Main.rand.Next(30, 70) * 0.01f;
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = TextureAssets.Projectile[Projectile.type].Value;
            //速度门控残影
            CEUtils.DrawAfterimage(tex, Projectile.Entropy().odp, Projectile.Entropy().odr);
            Main.spriteBatch.Draw(tex, Projectile.Center - Main.screenPosition, null, Color.White,
                Projectile.rotation, tex.Size() / 2, Projectile.scale, SpriteEffects.None, 0);
            Main.spriteBatch.UseAdditive();
            Texture2D glow = glowTex.Value;
            float pulse = 1f + 0.15f * (float)Math.Sin(Age * 0.4f);
            //终曲缓速弹(模式 2)换深红紫辉光并放大——弹幕海里一眼分清"会拐弯的那批"
            Color glowColor = SlowHoming ? new Color(255, 95, 175) : new Color(190, 90, 255);
            float glowScale = SlowHoming ? 0.62f : 0.5f;
            Main.spriteBatch.Draw(glow, Projectile.Center - Main.screenPosition, null,
                glowColor * 0.6f, 0, glow.Size() / 2, glowScale * pulse, SpriteEffects.None, 0);
            CEUtils.ReSetToEndShader();
            return false;
        }
    }
}
