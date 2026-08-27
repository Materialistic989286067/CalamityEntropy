using CalamityEntropy.Content.Buffs.PortsDoT;
using CalamityEntropy.Content.Items.Books.BookMarks;
using CalamityEntropy.Content.Rarities;
using InnoVault;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Books
{
    public class AshTranscript : EntropyBook
    {
        public override void SetDefaults()
        {
            base.SetDefaults();
            Item.damage = 140;
            Item.useAnimation = Item.useTime = 25;
            Item.crit = 10;
            Item.mana = 20;
            Item.rare = ModContent.RarityType<NihilityBlue>();
            Item.value = Item.buyPrice(platinum: 1, gold: 50);
        }
        [VaultLoaden("CalamityEntropy/Content/UI/EntropyBookUI/BookMark5")]
        internal static Asset<Texture2D> BookMarkSlotTex;
        public override Texture2D BookMarkTexture => BookMarkSlotTex.Value;
        public override int HeldProjectileType => ModContent.ProjectileType<AshTranscriptHeld>();
        public override int SlotCount => 4;

        public override void AddRecipes()
        {
            CreateRecipe().AddIngredient<NightEpic>()
                .AddIngredient<NihilityFragments>(6)
                .AddIngredient(ItemID.Ectoplasm, 6)
                .AddTile(TileID.AdamantiteForge)
                .Register();
        }
    }

    public class AshTranscriptHeld : EntropyBookHeldProjectile
    {
        public override string OpenAnimationPath => "CalamityEntropy/Content/Items/Books/Textures/AshTranscript/AshTranscriptOpen";
        public override string PageAnimationPath => "CalamityEntropy/Content/Items/Books/Textures/AshTranscript/AshTranscriptPage";
        public override string UIOpenAnimationPath => "CalamityEntropy/Content/Items/Books/Textures/AshTranscript/AshTranscriptUI";

        public override EBookStatModifer getBaseModifer()
        {
            var m = base.getBaseModifer();
            m.PenetrateAddition += 14;
            return m;
        }
        public override float randomShootRotMax => 0.02f;
        public override int baseProjectileType => ModContent.ProjectileType<ATLaser>();

        public override int frameChange => 3;
        public override EBookProjectileEffect getEffect()
        {
            return new HolyFireDebuffEffect();
        }

    }

    public class HolyFireDebuffEffect : EBookProjectileEffect
    {
        public override void OnHitNPC(Projectile projectile, NPC target, int damageDone)
        {
            target.AddBuff(ModContent.BuffType<HolyFlames>(), 600);
        }
    }
}
