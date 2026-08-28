using CalamityEntropy.Common;
using CalamityEntropy.Content.NPCs.VoidInvasion;
using CalamityEntropy.Content.Rarities;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.VoidInvasion
{
    /// <summary>
    /// 维度圣座请柬(void-invasion.md §5.2 教皇召唤物):
    /// 击败虚空入侵后可合成;地表使用,场上无教皇时召唤虚空教皇;不消耗(对齐 AbyssalSigil 惯例)。
    /// 贴图为定位仪金色调偏移的临时贴图(§7 缺口),待美术。
    /// </summary>
    public class PopeSummonItem : ModItem
    {
        public override void SetStaticDefaults()
        {
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
            Item.consumable = false;
            Item.rare = ModContent.RarityType<VoidPurple>();
            Item.UseSound = CEUtils.GetSound("bell", 0.9f, 4, 0.9f);
        }

        public override void ModifyResearchSorting(ref ContentSamples.CreativeHelper.ItemGroup itemGroup)
        {
            itemGroup = ContentSamples.CreativeHelper.ItemGroup.BossItem;
        }

        public override bool CanUseItem(Player player)
        {
            //§5.2 使用条件:事件已胜利、场上无教皇、玩家在地表
            return EDownedBosses.downedVoidInvasion
                && !NPC.AnyNPCs(ModContent.NPCType<VoidPope>())
                && (player.ZoneOverworldHeight || player.ZoneSkyHeight);
        }

        public override bool? UseItem(Player player)
        {
            //服务端/单人权威生成;入场表现走教皇现有 OnSpawn 链路
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                NPC.SpawnOnPlayer(player.whoAmI, ModContent.NPCType<VoidPope>());
            }
            return true;
        }

        public override void AddRecipes()
        {
            CreateRecipe().
                AddIngredient(ModContent.ItemType<VoidDimensionLocator>(), 1).
                AddIngredient(ModContent.ItemType<WraithSoulEssence>(), 15).
                AddIngredient(ItemID.Obsidian, 30).
                AddTile(TileID.LunarCraftingStation).
                Register();
        }
    }
}
