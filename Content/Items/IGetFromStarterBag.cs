using System.Collections.Generic;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items
{
    public interface IGetFromStarterBag
    {
        public bool OwnAble(Player player, ref int count);
    }
    public class StartBagGItem : GlobalItem
    {
        public static bool NameContains(Player player, string str)
        {
            return player.name.ToLower().Contains(str);
        }
        public static List<int> items;
        public override void ModifyItemLoot(Item item, ItemLoot itemLoot)
        {
            // 注入目标由灾厄新手包改为自有礼包「熵之馈赠」
            if (item.ModItem != null && item.ModItem is EntropyStarterBag)
            {
                foreach (int id in items)
                {
                    Item loot = ContentSamples.ItemsByType[id];
                    if (loot.ModItem is IGetFromStarterBag gfsb)
                    {
                        int ItemCount = 1;
                        gfsb.OwnAble(Main.LocalPlayer, ref ItemCount);
                        itemLoot.Add(ItemDropRule.ByCondition(new OwnableCondition(gfsb), id, 1, ItemCount, ItemCount));
                    }
                }
            }
        }
        // 把 OwnAble 判定包装为原生掉落条件（按掉落时的实际玩家判定）
        private class OwnableCondition : IItemDropRuleCondition, IProvideItemConditionDescription
        {
            private readonly IGetFromStarterBag gfsb;
            public OwnableCondition(IGetFromStarterBag gfsb)
            {
                this.gfsb = gfsb;
            }
            public bool CanDrop(DropAttemptInfo info)
            {
                int count = 1;
                return gfsb.OwnAble(info.player, ref count);
            }
            public bool CanShowItemDropInUI() => false;
            public string GetConditionDescription() => null;
        }
    }
}
