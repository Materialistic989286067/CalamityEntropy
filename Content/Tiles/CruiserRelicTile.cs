using CalamityEntropy.Content.Items;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Tiles
{
    public class CruiserRelicTile : CEBaseBossRelic
    {
        public override string RelicTextureName => "CalamityEntropy/Content/Tiles/CruiserRelicTile";

        public override int AssociatedItem => ModContent.ItemType<CruiserRelic>();
    }
}
