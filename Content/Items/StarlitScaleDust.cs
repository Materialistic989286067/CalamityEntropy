using CalamityEntropy.Core.CalamityRef;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items
{
    /// <summary>
    /// 星辉鳞尘:困难模式材料,承接原灾厄 StarblightSoot(material-map §一)。
    /// 获取:夜间发光蘑菇群系敌怪掉落(EGlobalNPC)、Luminaris 宝袋掉落。
    /// </summary>
    public class StarlitScaleDust : ModItem
    {
        public override void SetStaticDefaults() {
            Item.ResearchUnlockCount = 99;
            ItemID.Sets.SortingPriorityMaterials[Type] = 58;
        }

        public override void SetDefaults() {
            Item.width = 32;
            Item.height = 32;
            Item.maxStack = 9999;
            Item.value = Item.sellPrice(silver: 2);
            Item.rare = ItemRarityID.LightRed;
        }

        public override void AddRecipes() {
            if (CECal.CalChainReady(CEID.Item_StarblightSoot)) {
                CreateRecipe()
                    .AddIngredient(CEID.Item_StarblightSoot)
                    .Register();
                Recipe.Create(CEID.Item_StarblightSoot)
                    .AddIngredient(Type)
                    .Register();
            }
        }
    }
}
