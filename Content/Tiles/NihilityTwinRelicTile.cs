using CalamityEntropy.Content.Items;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Tiles
{
    public class NihilityTwinRelicTile : CEBaseBossRelic
    {
        public override string RelicTextureName => "CalamityEntropy/Content/Tiles/NihilityTwinRelicTile";

        public override int AssociatedItem => ModContent.ItemType<NihilityTwinRelic>();
    }
}
