using CalamityEntropy.Content.Items.Weapons.VoidInvasion;
using CalamityEntropy.Content.NPCs.VoidInvasion;
using CalamityEntropy.Content.Rarities;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.VoidInvasion
{
    /// <summary>
    /// 虚空教皇宝藏箱(void-invasion.md §5.3):专家模式 Boss 袋。
    /// 内容 = 五武器之一(必出)+ 幽渊魂髓 15~20 + 金币。
    /// 书签注入:bookmark-rehang.md 无教皇袋裁定(该文档定稿于本 Boss 之前),按 M9 交付约定不注入。
    /// </summary>
    public class VoidPopeBag : ModItem
    {
        public override void SetStaticDefaults()
        {
            Item.ResearchUnlockCount = 3;
            ItemID.Sets.BossBag[Item.type] = true;
        }

        public override void SetDefaults()
        {
            Item.maxStack = 9999;
            Item.consumable = true;
            Item.width = 30;
            Item.height = 40;
            Item.expert = true;
            Item.rare = ModContent.RarityType<VoidPurple>();
        }

        public override void ModifyResearchSorting(ref ContentSamples.CreativeHelper.ItemGroup itemGroup)
        {
            itemGroup = ContentSamples.CreativeHelper.ItemGroup.BossBags;
        }

        public override bool CanRightClick() => true;

        public override Color? GetAlpha(Color lightColor) => Color.Lerp(lightColor, Color.White, 0.4f);

        public override void PostUpdate()
        {
            CEUtils.ForceItemIntoWorld(Item);
            Item.TreasureBagLightAndDust();
        }

        public override bool PreDrawInWorld(SpriteBatch spriteBatch, Color lightColor, Color alphaColor, ref float rotation, ref float scale, int whoAmI)
        {
            return CEUtils.DrawTreasureBagInWorld(Item, spriteBatch, ref rotation, ref scale, whoAmI);
        }

        public override void ModifyItemLoot(ItemLoot itemLoot)
        {
            itemLoot.Add(ItemDropRule.CoinsBasedOnNPCValue(ModContent.NPCType<VoidPope>()));

            //五武器必出其一(§5.3)
            itemLoot.Add(ItemDropRule.OneFromOptionsNotScalingWithLuck(1, 1,
                ModContent.ItemType<FallenVoidCodex>(),
                ModContent.ItemType<VoidGodScythe>(),
                ModContent.ItemType<PrisonKnife>(),
                ModContent.ItemType<OmniscientTetrahedron>(),
                ModContent.ItemType<CurseRoar>()));

            itemLoot.Add(ItemDropRule.Common(ModContent.ItemType<WraithSoulEssence>(), 1, 15, 20));
        }
    }
}
