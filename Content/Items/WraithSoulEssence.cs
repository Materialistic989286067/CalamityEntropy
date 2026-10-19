using CalamityEntropy.Core.CalamityRef;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items
{
    /// <summary>
    /// 聚星之灵,类名沿用旧材料幽渊魂髓以保旧档
    /// 虚空驱逐舰后地牢敌怪掉落(VDAftermathGlobalNPC),巨龙商店和抽奖机 p6/p7 也有
    /// 承接 CosmiliteBar / AscendantSpiritEssence / TwistingNether
    /// </summary>
    public class WraithSoulEssence : ModItem
    {
        public override void SetStaticDefaults() {
            Item.ResearchUnlockCount = 25;
            ItemID.Sets.SortingPriorityMaterials[Type] = 104;
            ItemID.Sets.AnimatesAsSoul[Type] = true;
            ItemID.Sets.ItemNoGravity[Type] = true;
            ItemID.Sets.ItemIconPulse[Type] = true;
            Main.RegisterItemAnimation(Type, new DrawAnimationVertical(5, 5));
        }

        public override void SetDefaults() {
            Item.width = 40;
            Item.height = 40;
            Item.maxStack = 9999;
            Item.value = Item.sellPrice(silver: 40);
            Item.rare = ItemRarityID.Cyan;
        }

        public override void Update(ref float gravity, ref float maxFallSpeed) {
            float brightness = Main.essScale * Main.rand.NextFloat(0.9f, 1.1f);
            Lighting.AddLight(Item.Center, 0.45f * brightness, 0.4f * brightness, 0.85f * brightness);
        }

        public override Color? GetAlpha(Color lightColor) => new Color(255, 255, 255, 200);

        public override void AddRecipes() {
            if (CECal.CalChainReady(CEID.Item_AscendantSpiritEssence)) {
                CreateRecipe()
                    .AddIngredient(CEID.Item_AscendantSpiritEssence)
                    .Register();
                Recipe.Create(CEID.Item_AscendantSpiritEssence)
                    .AddIngredient(Type)
                    .Register();
            }
        }
    }
}
