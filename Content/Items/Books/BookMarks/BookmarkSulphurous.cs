using CalamityEntropy.Content.Buffs.PortsDoT;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;

namespace CalamityEntropy.Content.Items.Books.BookMarks
{
    public class BookmarkSulphurous : BookMark
    {
        public override void SetDefaults()
        {
            base.SetDefaults();
            Item.rare = ItemRarityID.Green;
            Item.value = Item.buyPrice(gold: 5);
        }
        public override Texture2D UITexture => BookMark.GetUITexture("Sulphurous");
        public override EBookProjectileEffect getEffect()
        {
            return new BookmarkSulphurousBMEffect();
        }

        public override Color tooltipColor => Color.LimeGreen;
        public override void AddRecipes()
        {
            CreateRecipe().AddIngredient(ItemID.SharkFin, 4)
                .AddIngredient(ItemID.SandBlock, 40)
                .AddTile(TileID.Bookcases)
                .Register();
        }
    }

    public partial class BookmarkSulphurousBMEffect : EBookProjectileEffect
    {
        public override void OnHitNPC(Projectile projectile, NPC target, int damageDone)
        {
            if (projectile.GetOwner().Entropy().SulphurousBubbleRecharge < 3600)
            {
                target.AddBuff<Irradiated>(Time);
            }
        }
    }
}
