using CalamityEntropy.Content.Rarities;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.VoidInvasion
{
    /// <summary>
    /// 虚空维度定位仪:虚空入侵开启物品(void-invasion.md §1.1)。
    /// 地表使用,广播开场文本并交给 VoidInvasion 系统跑 150t 倒计时,由服务端置位激活;消耗品。
    /// </summary>
    public class VoidDimensionLocator : ModItem
    {
        public override void SetStaticDefaults()
        {
            Item.ResearchUnlockCount = 3;
            ItemID.Sets.SortingPriorityBossSpawns[Type] = 15;
        }

        public override void SetDefaults()
        {
            Item.width = 54;
            Item.height = 44;
            Item.useAnimation = 24;
            Item.useTime = 24;
            Item.noUseGraphic = true;
            Item.useStyle = ItemUseStyleID.HoldUp;
            Item.consumable = true;
            Item.maxStack = 9999;
            Item.rare = ModContent.RarityType<AbyssalBlue>();
            Item.UseSound = CEUtils.GetSound("bell", 0.7f, 4, 0.9f);
        }

        public override void ModifyResearchSorting(ref ContentSamples.CreativeHelper.ItemGroup itemGroup)
        {
            itemGroup = ContentSamples.CreativeHelper.ItemGroup.BossItem;
        }

        public override bool CanUseItem(Player player)
        {
            //事件未激活且不在开场倒计时中、本模组无 Boss 在场、玩家在地表(§1.1)
            return !Events.VoidInvasion.Active && Events.VoidInvasion.StartCountdown <= 0
                && !AnyEntropyBossAlive()
                && (player.ZoneOverworldHeight || player.ZoneSkyHeight);
        }

        /// <summary>本模组 Boss 是否在场。boss 位由原生同步,客户端可直接判。</summary>
        private static bool AnyEntropyBossAlive()
        {
            foreach (NPC n in Main.npc)
            {
                if (n.active && n.boss && n.ModNPC != null && n.ModNPC.Mod == CalamityEntropy.Instance)
                    return true;
            }
            return false;
        }

        public override bool? UseItem(Player player)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                //服务端/单人权威入口:广播开场文本并启动倒计时,激活后由 SyncVoidInvasion 广播
                Events.VoidInvasion.BeginStartCountdown();
            }
            else if (player.whoAmI == Main.myPlayer)
            {
                //使用者本地镜像倒计时,只用于 CanUseItem 挡重复使用,激活仍以服务端广播为准
                Events.VoidInvasion.StartCountdown = 150;
            }
            return true;
        }

        public override void AddRecipes()
        {
            CreateRecipe().
                AddIngredient(ModContent.ItemType<WraithSoulEssence>(), 8).
                AddIngredient(ModContent.ItemType<NihilityFragments>(), 5).
                AddIngredient(ItemID.SoulofNight, 10).
                AddTile(TileID.LunarCraftingStation).
                Register();
        }
    }
}
