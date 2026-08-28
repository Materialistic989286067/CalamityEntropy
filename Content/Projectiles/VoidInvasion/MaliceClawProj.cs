using CalamityEntropy.Assets.Register;
using CalamityEntropy.Content.Particles;
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
    /// 恶念之爪(void-invasion.md §4.2 P2-7;MaliceClaw.png 130x80,链头贴图复用):
    /// 自魔盘向上扇形抛出(初速由生成侧)→ 悬滞 20t(速度衰竭,张爪朝玩家缓转 + 微光警示,无判定)
    /// → 点火追踪(11px/t,转向 0.045rad/t,追 90t 后直飞)→ 4s 自毁。爪 160 档。
    /// 波间错拍 15t 由生成时机继承(每爪自身悬滞固定 20t)。
    /// ai[0] = 点火帧覆盖(0 视为 20)。追踪目标 = 最近玩家,双端同式。
    /// </summary>
    public class MaliceClawProj : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Content/Projectiles/VoidInvasion/MaliceClaw";

        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light")]
        private static Asset<Texture2D> glowTex;

        public const int LifeTime = 240;
        public const int HomingTime = 90;
        public const float FlySpeed = 11f;
        public const float HomingTurn = 0.045f;

        public int IgniteAt => Projectile.ai[0] > 0 ? (int)Projectile.ai[0] : 20;

        private float age;

        public override void SetDefaults()
        {
            Projectile.width = 56;
            Projectile.height = 40;
            Projectile.friendly = false;
            Projectile.hostile = true;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.timeLeft = LifeTime;
        }

        public override void AI()
        {
            age++;
            int ignite = IgniteAt;
            if (age < ignite)
            {
                //悬滞:速度衰竭,张爪朝最近玩家缓转(警示指向)
                Projectile.velocity *= 0.86f;
                int idx = Player.FindClosest(Projectile.Center, 1, 1);
                if (idx >= 0)
                {
                    float want = (Main.player[idx].Center - Projectile.Center).ToRotation();
                    Projectile.rotation = Utils.AngleLerp(Projectile.rotation, want, 0.12f);
                }
                //张爪微光脉动(§4.2:悬滞 20t 张爪警示)
                if (!Main.dedServ && Main.rand.NextBool(4))
                {
                    var p = PRTLoader.NewParticle<PRT_Light>(Projectile.Center, Vector2.Zero, new Color(200, 110, 255), 0.4f);
                    p.Configure(0.8f, lifetime: 10);
                }
                return;
            }
            if (age == ignite)
            {
                //点火:一帧设速 + 破空音 + 方向性冲环(悬滞到扑出的爆点)
                int idx = Player.FindClosest(Projectile.Center, 1, 1);
                Vector2 dir = idx >= 0
                    ? (Main.player[idx].Center - Projectile.Center).SafeNormalize(Vector2.UnitY)
                    : Projectile.rotation.ToRotationVector2();
                Projectile.velocity = dir * FlySpeed;
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.Item71 with { Volume = 0.7f, Pitch = 0.1f }, Projectile.Center);
                    var ring = PRTLoader.NewParticle<Particles.CalamityPorts.PRT_DirectionalPulseRing>(
                        Projectile.Center, Vector2.Zero, new Color(210, 130, 255), 0.06f);
                    ring.Configure(new Vector2(2.1f, 0.6f), dir.ToRotation(), 1.2f, 13);
                }
            }
            else if (age < ignite + HomingTime)
            {
                //追踪段:小幅转向
                int idx = Player.FindClosest(Projectile.Center, 1, 1);
                if (idx >= 0)
                {
                    float want = (Main.player[idx].Center - Projectile.Center).ToRotation();
                    float cur = Projectile.velocity.ToRotation();
                    float turn = MathHelper.Clamp(MathHelper.WrapAngle(want - cur), -HomingTurn, HomingTurn);
                    Projectile.velocity = (cur + turn).ToRotationVector2() * FlySpeed;
                }
            }
            Projectile.rotation = Projectile.velocity.ToRotation();
            Lighting.AddLight(Projectile.Center, 0.45f, 0.18f, 0.7f);
            if (!Main.dedServ && Main.rand.NextBool(3))
            {
                var v = PRTLoader.NewParticle<PRT_Void>(Projectile.Center, -Projectile.velocity * 0.1f, Color.White, 0.8f);
                v.Opacity = Main.rand.Next(25, 60) * 0.01f;
            }
        }

        public override bool CanHitPlayer(Player target)
        {
            //抛出与悬滞段无判定(公平阀:点火才是威胁)
            return age >= IgniteAt;
        }

        public override void OnKill(int timeLeft)
        {
            if (Main.dedServ)
            {
                return;
            }
            for (int i = 0; i < 6; i++)
            {
                var v = PRTLoader.NewParticle<PRT_Void>(Projectile.Center,
                    CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(1f, 5f), Color.White, 0.9f);
                v.Opacity = Main.rand.Next(30, 70) * 0.01f;
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = TextureAssets.Projectile[Projectile.type].Value;
            Vector2 pos = Projectile.Center - Main.screenPosition;
            //源图爪尖朝左,水平镜像后沿 rotation 指向(与 PopeChain 链头同姿势)
            Vector2 origin = new Vector2(tex.Width * 0.35f, tex.Height * 0.5f);
            //渐隐出场(寿命尾 20t)
            float alpha = MathHelper.Clamp(Projectile.timeLeft / 20f, 0f, 1f);
            //残影(点火后速度门控)
            if (age >= IgniteAt)
            {
                CEUtils.DrawAfterimage(tex, Projectile.Entropy().odp, Projectile.Entropy().odr);
            }
            //悬滞威胁脉动(演出二迭):张爪缩放呼吸 + 腕部小幅开合;点火头 2 帧沿飞向拉伸
            float clawScale = 0.75f;
            float clawRot = Projectile.rotation;
            Vector2 stretch = Vector2.One;
            if (age < IgniteAt)
            {
                clawScale *= 1f + 0.09f * (float)Math.Sin(age * 0.55f);
                clawRot += 0.1f * (float)Math.Sin(age * 0.45f);
            }
            else if (age - IgniteAt < 2)
            {
                stretch = new Vector2(1.3f, 0.82f);
            }
            Main.spriteBatch.Draw(tex, pos, null, Color.White * alpha, clawRot, origin,
                new Vector2(clawScale * stretch.X, clawScale * stretch.Y), SpriteEffects.FlipHorizontally, 0);
            if (age < IgniteAt)
            {
                Main.spriteBatch.UseAdditive();
                //瞄准线(演出二迭:点火前 8t,细线指向即将扑向的方向,渐亮)
                int toIgnite = IgniteAt - (int)age;
                if (toIgnite <= 8)
                {
                    float aimP = 1f - toIgnite / 8f;
                    Texture2D aim = CEExtraAssets.vlbw;
                    Main.spriteBatch.Draw(aim, pos, null, new Color(230, 150, 255) * (0.55f * aimP), Projectile.rotation,
                        aim.Size() / 2 * new Vector2(0, 1), new Vector2(360f / aim.Width, 0.1f + 0.08f * aimP), SpriteEffects.None, 0);
                }
                //悬滞警示微光
                Texture2D glow = glowTex.Value;
                float pulse = 1f + 0.25f * (float)Math.Sin(age * 0.6f);
                Main.spriteBatch.Draw(glow, pos, null, new Color(200, 110, 255) * 0.5f, 0, glow.Size() / 2, 0.7f * pulse, SpriteEffects.None, 0);
                CEUtils.ReSetToEndShader();
            }
            return false;
        }
    }
}
