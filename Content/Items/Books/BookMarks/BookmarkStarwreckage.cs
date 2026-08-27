using CalamityEntropy.Common;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Books.BookMarks
{
    public class BookmarkStarwreckage : BookMark
    {
        public override void SetDefaults()
        {
            base.SetDefaults();
            Item.rare = ItemRarityID.Red;
            Item.value = Item.buyPrice(gold: 5);
        }
        public override Texture2D UITexture => BookMark.GetUITexture("Starwreckage");
        public override EBookProjectileEffect getEffect()
        {
            return new StarwreckageBMEffect();
        }
        public override void AddRecipes()
        {
            // 原联动模组金属块原料, 脱钩后改用自有虚空锭
            CreateRecipe().AddIngredient<VoidBar>()
                .AddTile(TileID.WorkBenches).Register();
        }

        public override Color tooltipColor => Color.DarkRed;
    }

    public class StarwreckageBMEffect : EBookProjectileEffect
    {
        public override void BookUpdate(Projectile projectile, bool ownerClient)
        {
            if (ownerClient && CECooldowns.CheckCD("Starwreckage", 120))
            {
                if (projectile.ModProjectile is EntropyBookHeldProjectile eb)
                    eb.ShootSingleProjectile(ModContent.ProjectileType<MetallicChunkProj>(), projectile.Center, projectile.rotation.ToRotationVector2(), 0.3f, 1, 0.5f);
            }
        }
    }
    public class MetallicChunkProj : EBookBaseProjectile
    {
        public override string Texture => CEUtils.WhiteTexPath;
        public override void SetDefaults()
        {
            base.SetDefaults();
            Projectile.penetrate = -1;
            Projectile.localNPCHitCooldown = -1;
            Projectile.timeLeft = 480;
            Projectile.tileCollide = true;
            Projectile.width = Projectile.height = 26;
        }
        public override void ApplyHoming()
        {

        }

        public override void AI()
        {
            base.AI();
            if (Projectile.localAI[2]++ > 6)
            {
                Projectile.velocity.Y += 0.16f;
                Projectile.velocity *= 0.998f;
            }
            Projectile.rotation += Projectile.velocity.X * 0.01f;
            foreach (NPC npc in Main.ActiveNPCs)
            {
                if (!npc.friendly && !npc.dontTakeDamage && Projectile.Colliding(Projectile.Hitbox, npc.Hitbox))
                {
                    if (npc.life > Projectile.damage)
                    {
                        npc.life -= Projectile.damage;
                        CombatText.NewText(npc.getRect(), Color.DarkRed, Projectile.damage);
                    }
                }
            }
        }
        public override void OnKill(int timeLeft)
        {
            for (int i = 0; i < 16; i++)
                Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, DustID.Stone);
            SoundEngine.PlaySound(SoundID.Dig, Projectile.Center);
        }
        public override bool PreDraw(ref Color lightColor)
        {
            int type = ModContent.ItemType<VoidBar>();
            Main.instance.LoadItem(type);
            Texture2D tex = TextureAssets.Item[type].Value;
            Main.EntitySpriteDraw(Projectile.getDrawData(lightColor, tex));
            return false;
        }
    }
}