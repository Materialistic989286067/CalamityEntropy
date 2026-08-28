using CalamityEntropy.Content.Items.VoidInvasion;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Tiles
{
    public class VoidPopeRelicTile : CEBaseBossRelic
    {
        //悬浮件直接复用圣物 item 贴图(60x80,§5.3)
        public override string RelicTextureName => "CalamityEntropy/Content/Items/VoidInvasion/VoidPopeRelic";

        public override int AssociatedItem => ModContent.ItemType<VoidPopeRelic>();
    }
}
