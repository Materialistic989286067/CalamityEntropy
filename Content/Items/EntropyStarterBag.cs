using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items
{
    /// <summary>
    /// 熵之馈赠：替代灾厄新手包注入通道的自有礼包。
    /// 开包内容由 StartBagGItem.ModifyItemLoot 按 IGetFromStarterBag 接口物品统一注入；
    /// MagicStorage/ImproveGame 的开局便利物品由本类 ModifyItemLoot 条件注入；
    /// 首次进入世界的发放与一次性旗标由 EModPlayer.OnEnterWorld 侧落地，受 ServerConfig.ExtraItemsInStarterBag 控制。
    /// </summary>
    public class EntropyStarterBag : ModItem
    {
        // 暂用彩票箱贴图占位，正式贴图画好后换回同名资源
        public override string Texture => "CalamityEntropy/Content/Items/LotteryBox";

        public override void SetDefaults()
        {
            Item.width = 24;
            Item.height = 24;
            Item.maxStack = 1;
            Item.consumable = true;
            Item.rare = ItemRarityID.White;
        }

        public override bool CanRightClick() => true;

        public override void ModifyItemLoot(ItemLoot itemLoot)
        {
            // MagicStorage 与 ImproveGame 的开局便利注入：原挂灾厄新手包（原写法存于 git 历史 EGlobalItem），
            // 脱离灾厄后重挂至此。发放侧已受 ExtraItemsInStarterBag 门控，包内只保留跨模组存在性门控。
            if (ModLoader.TryGetMod("MagicStorage", out Mod magicStorage))
            {
                if (magicStorage.TryFind<ModItem>("CraftingAccess", out ModItem craftingAccess))
                    itemLoot.Add(ItemDropRule.Common(craftingAccess.Type));
                if (magicStorage.TryFind<ModItem>("StorageHeart", out ModItem storageHeart))
                    itemLoot.Add(ItemDropRule.Common(storageHeart.Type));
                if (magicStorage.TryFind<ModItem>("StorageUnit", out ModItem storageUnit))
                    itemLoot.Add(ItemDropRule.Common(storageUnit.Type, 1, 10, 10));
            }
            if (ModLoader.TryGetMod("ImproveGame", out Mod improveGame))
            {
                foreach (string name in new[] { "MagickWand", "SpaceWand", "CreateWand", "PotionBag", "BannerChest" })
                {
                    if (improveGame.TryFind<ModItem>(name, out ModItem tool))
                        itemLoot.Add(ItemDropRule.Common(tool.Type));
                }
            }
        }
    }
}
