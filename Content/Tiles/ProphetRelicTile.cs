using CalamityEntropy.Content.Items;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Tiles
{
    public class ProphetRelicTile : CEBaseBossRelic
    {
        public override string RelicTextureName => "CalamityEntropy/Content/Tiles/ProphetRelicTile";

        public override int AssociatedItem => ModContent.ItemType<ProphetRelic>();
    }
}
