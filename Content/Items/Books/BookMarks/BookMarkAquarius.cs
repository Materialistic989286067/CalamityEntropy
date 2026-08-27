using CalamityEntropy.Common;
using CalamityEntropy.Content.Particles;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Books.BookMarks
{
    public class BookMarkAquarius : BookMark
    {
        public override void SetDefaults()
        {
            base.SetDefaults();
            Item.rare = ItemRarityID.Orange;
            Item.Entropy().stroke = true;
            Item.Entropy().NameColor = Color.LightBlue;
            Item.Entropy().strokeColor = Color.DarkBlue;
            Item.Entropy().tooltipStyle = 4;
            Item.value = Item.buyPrice(gold: 1);
        }
        public override Texture2D UITexture => BookMark.GetUITexture("Aquarius");
        public override Color tooltipColor => Color.LightBlue;
        public override EBookProjectileEffect getEffect()
        {
            return new AquariusBMEffect();
        }
    }

    public class AquariusBMEffect : EBookProjectileEffect
    {
        public override void OnHitNPC(Projectile projectile, NPC target, int damageDone)
        {
            if (projectile.ModProjectile is EBookBaseProjectile ebp)
            {
                if (ebp.mainProj || Main.rand.NextBool(projectile.HasEBookEffect<APlusBMEffect>() ? 2 : 3))
                {
                    if (CECooldowns.CheckCD("AquariusBM", 2))
                    {
                        int shootCount = 5;

                        foreach (NPC npc in Main.ActiveNPCs)
                        {
                            float rot = 0;
                            if (npc != target && npc.Distance(target.Center) < 800 && !npc.friendly && !npc.dontTakeDamage)
                            {
                                rot = (target.Center - npc.Center).ToRotation();
                                Projectile.NewProjectile(projectile.GetSource_FromThis(), projectile.Center, rot.ToRotationVector2() * 8, ModContent.ProjectileType<AquariusWaterBolt>(), projectile.damage / 6, projectile.knockBack + 0.5f, projectile.owner);
                                shootCount--;
                            }
                        }
                        for (; shootCount > 0; shootCount--)
                        {
                            float rot = CEUtils.randomRot();
                            Projectile.NewProjectile(projectile.GetSource_FromThis(), projectile.Center, rot.ToRotationVector2() * 8, ModContent.ProjectileType<AquariusWaterBolt>(), projectile.damage / 6, projectile.knockBack + 0.5f, projectile.owner);
                        }
                    }
                }
            }
        }
    }

    // 原灾厄 WaterShot 的自有等效: 直线飞行的小水弹
    public class AquariusWaterBolt : ModProjectile
    {
        public override string Texture => CEUtils.WhiteTexPath;
        public override void SetDefaults()
        {
            Projectile.width = Projectile.height = 12;
            Projectile.friendly = true;
            Projectile.DamageType = DamageClass.Magic;
            Projectile.penetrate = 1;
            Projectile.tileCollide = true;
            Projectile.timeLeft = 240;
            Projectile.extraUpdates = 1;
        }
        public override void AI()
        {
            Projectile.rotation = Projectile.velocity.ToRotation();
            Lighting.AddLight(Projectile.Center, Color.DeepSkyBlue.ToVector3() * 0.2f);
            for (float i = 0; i < 1; i += 0.5f)
            {
                var p = PRTLoader.NewParticle<PRT_GlowLightParticle>(Projectile.Center - Projectile.velocity * i, CEUtils.randomPointInCircle(1), Color.DeepSkyBlue, Main.rand.NextFloat(0.4f, 0.7f));
                p.lightColor = Color.DeepSkyBlue * 0.1f;
                p.Configure(1, true, PRTDrawModeEnum.AdditiveBlend, 0, 12);
            }
        }
        public override void OnKill(int timeLeft)
        {
            for (int i = 0; i < 6; i++)
            {
                Dust d = Dust.NewDustDirect(Projectile.position, Projectile.width, Projectile.height, DustID.Water);
                d.noGravity = true;
                d.velocity = CEUtils.randomPointInCircle(3);
            }
        }
        public override bool PreDraw(ref Color lightColor)
        {
            return false;
        }
    }
}