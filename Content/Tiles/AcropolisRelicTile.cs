using CalamityEntropy.Content.Items;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Tiles
{
    public class AcropolisRelicTile : CEBaseBossRelic
    {
        public override string RelicTextureName => "CalamityEntropy/Content/Tiles/AcropolisRelicTile";

        public override int AssociatedItem => ModContent.ItemType<AcropolisRelic>();
    }
}
