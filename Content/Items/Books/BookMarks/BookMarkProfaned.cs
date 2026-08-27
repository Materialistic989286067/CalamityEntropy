using CalamityEntropy.Content.Projectiles;
using CalamityEntropy.Content.Rarities;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Books.BookMarks
{
    public class BookMarkProfaned : BookMark
    {
        public override void SetDefaults()
        {
            base.SetDefaults();
            Item.rare = ModContent.RarityType<NihilityBlue>();
            Item.value = Item.buyPrice(platinum: 1, gold: 50);
        }
        public override Texture2D UITexture => BookMark.GetUITexture("Profaned");
        public override EBookProjectileEffect getEffect()
        {
            return new ProfanedBMEffect();
        }
        public override Color tooltipColor => Color.Firebrick;
    }

    public class ProfanedBMEffect : EBookProjectileEffect
    {
        public override void UpdateProjectile(Projectile projectile, bool ownerClient)
        {
            projectile.Entropy().ShootCount += 0.02f;
            if (projectile.Entropy().ShootCount >= 1 && projectile.Entropy().counter % 16 == 0 && ownerClient)
            {
                projectile.Entropy().ShootCount--;
                NPC target = projectile.FindTargetWithinRange(700);
                if (target != null)
                {
                    // 原灾厄 HolyColliderHolyFire 改用自有金色龙焰
                    Projectile.NewProjectile(projectile.GetSource_FromThis(), projectile.Center, (target.Center - projectile.Center).normalize() * 9, ModContent.ProjectileType<DragonGoldenFire>(), (int)(projectile.damage * 0.18f), projectile.knockBack, projectile.owner).ToProj().DamageType = projectile.DamageType;
                }
            }
        }
    }
}
