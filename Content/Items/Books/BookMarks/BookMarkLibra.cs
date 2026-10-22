using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;

namespace CalamityEntropy.Content.Items.Books.BookMarks
{
    public class BookMarkLibra : BookMark
    {
        public override void SetDefaults() {
            base.SetDefaults();
            Item.rare = ItemRarityID.Orange;
            Item.Entropy().stroke = true;
            Item.Entropy().NameColor = Color.LightBlue;
            Item.Entropy().strokeColor = Color.DarkBlue;
            Item.Entropy().tooltipStyle = 4;
            Item.value = Item.buyPrice(gold: 1);
        }
        public override Texture2D UITexture => BookMark.GetUITexture("Libra");
        public override Color tooltipColor => Color.LightBlue;
        public override EBookProjectileEffect getEffect() {
            return new LibraBMEffect();
        }
        public override void ModifyStat(EBookStatModifer modifer) {
            modifer.Damage += 0.08f;
            modifer.Crit += 8;
        }
    }

    public class LibraBMEffect : EBookProjectileEffect
    {
        public override void BookUpdate(Projectile projectile, bool ownerClient) {
            projectile.GetOwner().Entropy().bmLibraDefTime = 2;
        }
    }

}
