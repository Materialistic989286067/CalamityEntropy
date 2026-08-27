using CalamityEntropy.Content.Items;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Tiles
{
    public class LuminarisRelicTile : CEBaseBossRelic
    {
        public override string RelicTextureName => "CalamityEntropy/Content/Tiles/LuminarisRelicTile";

        public override int AssociatedItem => ModContent.ItemType<LuminarisRelic>();
    }
}
