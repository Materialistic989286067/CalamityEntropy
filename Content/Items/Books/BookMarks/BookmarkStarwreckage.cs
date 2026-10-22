using CalamityEntropy.Assets.Register;
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
        public override void SetDefaults() {
            base.SetDefaults();
            Item.rare = ItemRarityID.Red;
            Item.value = Item.buyPrice(gold: 5);
        }
        public override Texture2D UITexture => BookMark.GetUITexture("Starwreckage");
        public override EBookProjectileEffect getEffect() {
            return new StarwreckageBMEffect();
        }
        public override void AddRecipes() {
            CreateRecipe()
                .AddIngredient(ItemID.FragmentNebula, 3)
                .AddTile(TileID.LunarCraftingStation)
                .Register();
        }

        public override Color tooltipColor => Color.DarkRed;
    }

    /// <summary>星骸书签(2026-08-31 平衡案重做):随机投射无视无敌帧的四柱碎片(固定基伤100)。</summary>
    public class StarwreckageBMEffect : EBookProjectileEffect
    {
        public override void BookUpdate(Projectile projectile, bool ownerClient) {
            if (ownerClient && CECooldowns.CheckCD("Starwreckage", 60)) {
                Player owner = projectile.GetOwner();
                Vector2 dir = (projectile.rotation + Main.rand.NextFloat(-0.5f, 0.5f)).ToRotationVector2();
                Projectile.NewProjectile(projectile.GetSource_FromAI(), projectile.Center, dir * Main.rand.NextFloat(11f, 15f),
                    ModContent.ProjectileType<PillarShardProj>(), FixedDamage(owner, 100, projectile.DamageType), 2f, projectile.owner,
                    Main.rand.Next(4));
            }
        }
    }
    /// <summary>四柱碎片:ai[0]=0..3 选日耀/星旋/星云/星尘外观,采用本地无敌帧(无视全局无敌帧)。</summary>
    public class PillarShardProj : ModProjectile
    {
        private static readonly int[] FragmentItems = new int[] { ItemID.FragmentSolar, ItemID.FragmentVortex, ItemID.FragmentNebula, ItemID.FragmentStardust };
        public override string Texture => CEUtils.WhiteTexPath;
        public override void SetStaticDefaults() {
            ProjectileID.Sets.TrailingMode[Type] = 2;
            ProjectileID.Sets.TrailCacheLength[Type] = 10;
        }
        private Color GlowColor => ((int)Projectile.ai[0] % 4) switch {
            0 => new Color(255, 160, 40),
            1 => new Color(60, 220, 180),
            2 => new Color(220, 80, 220),
            _ => new Color(80, 160, 255)
        };
        public override void SetDefaults() {
            Projectile.width = Projectile.height = 26;
            Projectile.friendly = true;
            Projectile.hostile = false;
            Projectile.DamageType = Terraria.ModLoader.DamageClass.Magic;
            Projectile.penetrate = 3;
            Projectile.tileCollide = true;
            Projectile.timeLeft = 360;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 12;
        }
        public override void AI() {
            if (Projectile.localAI[0]++ > 10) {
                Projectile.velocity.Y += 0.14f;
            }
            Projectile.rotation += Projectile.velocity.X * 0.02f;
            Color glow = GlowColor;
            Lighting.AddLight(Projectile.Center, glow.ToVector3() * 0.4f);
            if (Main.rand.NextBool(3)) {
                Dust d = Dust.NewDustPerfect(Projectile.Center, DustID.RainbowMk2, -Projectile.velocity * 0.12f, 0, glow, Main.rand.NextFloat(0.8f, 1.2f));
                d.noGravity = true;
            }
        }
        public override void OnKill(int timeLeft) {
            for (int i = 0; i < 16; i++)
                Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, DustID.Stone);
            for (int i = 0; i < 8; i++) {
                Dust d = Dust.NewDustPerfect(Projectile.Center, DustID.RainbowMk2, CEUtils.randomVec(4), 0, GlowColor, Main.rand.NextFloat(1f, 1.4f));
                d.noGravity = true;
            }
            SoundEngine.PlaySound(SoundID.Dig, Projectile.Center);
        }
        public override bool PreDraw(ref Color lightColor) {
            int type = FragmentItems[(int)Projectile.ai[0] % 4];
            Main.instance.LoadItem(type);
            Texture2D tex = TextureAssets.Item[type].Value;
            Rectangle frame = Main.itemAnimations[type] == null ? tex.Frame() : Main.itemAnimations[type].GetFrame(tex);
            Color glow = GlowColor;
            Texture2D orb = CEExtraAssets.lightball;
            Main.spriteBatch.UseAdditive();
            for (int i = Projectile.oldPos.Length - 1; i >= 1; i--) {
                if (Projectile.oldPos[i] == Vector2.Zero)
                    continue;
                float fade = 1f - i / (float)Projectile.oldPos.Length;
                Main.spriteBatch.Draw(orb, Projectile.oldPos[i] + Projectile.Size / 2f - Main.screenPosition, null, glow * 0.5f * fade, 0, orb.Size() / 2f, 0.16f * fade * Projectile.scale, SpriteEffects.None, 0);
            }
            Main.spriteBatch.Draw(orb, Projectile.Center - Main.screenPosition, null, glow * 0.7f, 0, orb.Size() / 2f, 0.2f * Projectile.scale, SpriteEffects.None, 0);
            CEUtils.ReSetToEndShader();
            Main.EntitySpriteDraw(tex, Projectile.Center - Main.screenPosition, frame, Color.Lerp(lightColor, Color.White, 0.5f), Projectile.rotation, frame.Size() / 2f, Projectile.scale, SpriteEffects.None);
            return false;
        }
    }
}