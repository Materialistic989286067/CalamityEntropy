using CalamityEntropy.Content.Particles.CalamityPorts;
using InnoVault.PRT;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Projectiles
{
    //脱离灾厄:灾厄GhastlySoulLarge同短名移植(幽魂追踪弹,SpiritFractal大招衍生)
    //运动/伤害衰减/死亡爆发逐式对齐(VoidEdge常量已内联:弹速10、散开期20、每挥3发);
    //本体贴图以自有火花拖尾+光照呈现(原灾厄5帧鬼魂贴图不可拷贝),视觉近似待看审
    public class GhastlySoulLarge : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Assets/Extra/Ports/Invisible";

        private const int TimeLeft = 660;
        private const float ShootSpeed = 10f;
        private const int SpreadOutTime = 20;
        private const int SoulsPerSwing = 3;
        private float HomingBuff = 1;

        public override void SetStaticDefaults() => ProjectileID.Sets.CultistIsResistantTo[Type] = true;

        public override void SetDefaults()
        {
            Projectile.width = Projectile.height = 32;
            Projectile.alpha = 100;
            Projectile.friendly = true;
            Projectile.ignoreWater = true;
            Projectile.DamageType = DamageClass.Melee;
            Projectile.penetrate = 1;
            Projectile.extraUpdates = 1;
            Projectile.timeLeft = TimeLeft;
            Projectile.tileCollide = false;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 0;
        }

        public override void AI()
        {
            if (HomingBuff > 0)
                HomingBuff -= 0.01f;

            Lighting.AddLight(Projectile.Center, 0.5f, 0.2f, 0.9f);

            if (Projectile.timeLeft % 2 == 0 && Projectile.timeLeft < TimeLeft - 10)
            {
                PRTLoader.NewParticle<PRT_SparkCal>(Projectile.Center + Main.rand.NextVector2Circular(15, 15) - Projectile.velocity.SafeNormalize(Vector2.UnitX) * 10, -Projectile.velocity * Main.rand.NextFloat(0.5f, 1.5f), Color.Plum, 0.4f).Configure(false, Main.rand.Next(9, 13));
            }

            float inertia = MathHelper.Lerp(20, 90, HomingBuff) * Projectile.ai[1];
            float velocity = ShootSpeed * Projectile.ai[1];
            if (Main.player[Projectile.owner].active && !Main.player[Projectile.owner].dead)
            {
                float homingDistance = 900f;
                NPC target = Projectile.FindTargetWithinRange(homingDistance);
                if (Projectile.timeLeft < TimeLeft - SpreadOutTime && target != null)
                {
                    Projectile.HomingNPCBetter(target, homingDistance, velocity, inertia, giveExtraUpdate: 1, ignoreDist: true);
                }
                else if (Projectile.Distance(Main.player[Projectile.owner].Center) > homingDistance)
                {
                    Vector2 moveDirection = Projectile.SafeDirectionTo(Main.player[Projectile.owner].Center, Vector2.UnitY);
                    Projectile.velocity = (Projectile.velocity * (inertia - 1f) + moveDirection * velocity) / inertia;
                }
            }
            else
            {
                if (Projectile.timeLeft > 30)
                    Projectile.timeLeft = 30;
            }

            Projectile.rotation = Projectile.velocity.ToRotation() - MathHelper.PiOver2;
        }

        public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers)
        {
            if (Projectile.numHits > 0)
                Projectile.damage = (int)(Projectile.damage * 0.88f);
            if (Projectile.damage < 1)
                Projectile.damage = 1;
        }

        public override void OnKill(int timeLeft)
        {
            Projectile.damage = (int)(Projectile.damage / SoulsPerSwing);
            Projectile.penetrate = -1;
            Vector2 center = Projectile.Center;
            Projectile.width += 230;
            Projectile.height += 230;
            Projectile.Center = center;
            Projectile.Damage();
            SoundEngine.PlaySound(SoundID.Item100 with { Volume = 0.5f, Pitch = -0.3f }, Projectile.Center);

            int points = 25;
            float radians = MathHelper.TwoPi / points;
            Vector2 spinningPoint = Vector2.Normalize(new Vector2(-1f, -1f));
            float rotRando = Main.rand.NextFloat(0.1f, 2.5f);
            for (int k = 0; k < points; k++)
            {
                Vector2 velocity = spinningPoint.RotatedBy(radians * k).RotatedBy(-0.45f * rotRando);
                PRTLoader.NewParticle<PRT_LineCal>(Projectile.Center + velocity * 20.5f, velocity * 15, Color.Plum, 0.75f).Configure(false, 30);
            }
            for (int k = 0; k < 17; k++)
            {
                Dust dust2 = Dust.NewDustPerfect(Projectile.Center, DustID.RainbowTorch, new Vector2(14, 14).RotatedByRandom(100) * Main.rand.NextFloat(0.3f, 1.8f));
                dust2.scale = Main.rand.NextFloat(1.15f, 1.45f);
                dust2.noGravity = true;
                dust2.color = Color.Plum;
            }
        }
    }
}
