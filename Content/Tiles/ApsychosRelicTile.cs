using CalamityEntropy.Content.Items;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Tiles
{
    public class ApsychosRelicTile : CEBaseBossRelic
    {
        public override string RelicTextureName => "CalamityEntropy/Content/Tiles/ApsychosRelicTile";

        public override int AssociatedItem => ModContent.ItemType<ApsychosRelic>();
    }
}
