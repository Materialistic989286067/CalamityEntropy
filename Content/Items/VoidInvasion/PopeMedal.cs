using CalamityEntropy.Common;
using CalamityEntropy.Content.Rarities;
using Terraria;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.VoidInvasion
{
    /// <summary>
    /// 教皇纪念章(void-invasion.md §5.3):饰品。
    /// 佩戴者在虚空入侵期间的击杀进度 ×1.1(结算在 VoidInvasionGNPC.OnKill,查 lastInteraction 玩家旗标);
    /// 受到虚空触减益时时长减半(EModPlayer.PostUpdateBuffs 侧追踪实现)。
    /// </summary>
    public class PopeMedal : ModItem
    {
        /// <summary>击杀进度乘数(§5.3)</summary>
        public const float ProgressMult = 1.1f;

        public override void SetDefaults()
        {
            Item.width = 30;
            Item.height = 40;
            Item.accessory = true;
            Item.maxStack = 1;
            Item.rare = ModContent.RarityType<VoidPurple>();
            Item.value = Item.buyPrice(platinum: 1, gold: 50);
        }

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            player.Entropy().popeMedal = true;
        }
    }
}
