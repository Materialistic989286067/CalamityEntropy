using CalamityEntropy.Content.Items;
using System;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Common.VanillaTweaks
{
    /// <summary>给原版物品追加的配方:药水、生命果、幸运马掌、闪亮红气球、云朵瓶</summary>
    public class VanillaExtraRecipes : ModSystem
    {
        public const string GoldBarGroup = "CalamityEntropy:AnyGoldBar";

        //没有 buff 也没有治疗量的饮用型药水
        private static readonly int[] UtilityPotions = {
            ItemID.RecallPotion, ItemID.TeleportationPotion, ItemID.WormholePotion,
            ItemID.PotionOfReturn, ItemID.GenderChangePotion
        };

        public override void AddRecipeGroups() {
            RecipeGroup gold = new RecipeGroup(() => Lang.misc[37].Value + " " + Lang.GetItemNameValue(ItemID.GoldBar), ItemID.GoldBar, ItemID.PlatinumBar);
            RecipeGroup.RegisterGroup(GoldBarGroup, gold);
        }

        public override void AddRecipes() {
            AddPotionRecipes();

            Recipe.Create(ItemID.LifeFruit)
                .AddIngredient(ItemID.ChlorophyteBar)
                .AddIngredient<StarlitScaleDust>(2)
                .AddTile(TileID.CookingPots)
                .Register();

            Recipe.Create(ItemID.LuckyHorseshoe)
                .AddRecipeGroup(GoldBarGroup, 20)
                .AddRecipeGroup(RecipeGroupID.IronBar, 20)
                .AddTile(TileID.Anvils)
                .Register();

            Recipe.Create(ItemID.ShinyRedBalloon)
                .AddIngredient(ItemID.Cloud, 30)
                .AddIngredient(ItemID.WhiteString)
                .AddRecipeGroup(CERecipeGroups.evilBar_, 15)
                .AddTile(TileID.Solidifier)
                .Register();

            Recipe.Create(ItemID.CloudinaBottle)
                .AddIngredient(ItemID.Bottle)
                .AddIngredient(ItemID.Cloud, 30)
                .AddIngredient(ItemID.RainCloud, 30)
                .AddTile(TileID.Solidifier)
                .Register();
        }

        private static void AddPotionRecipes() {
            for (int type = 1; type < ItemID.Count; type++) {
                Item sample = ContentSamples.ItemsByType.TryGetValue(type, out Item found) ? found : new Item(type);
                if (!IsVanillaPotion(sample)) {
                    continue;
                }
                Recipe.Create(type)
                    .AddIngredient(ItemID.BottledWater)
                    .AddIngredient<StarlitScaleDust>()
                    .AddTile(TileID.AlchemyTable)
                    .Register();
            }
        }

        /// <summary>可堆叠的饮用型消耗品,带 buff 或治疗量,另加不带二者的传送类药水;排除食物、水瓶与投掷型</summary>
        private static bool IsVanillaPotion(Item item) {
            int type = item.type;
            if (type == ItemID.BottledWater) {
                return false;
            }
            if (Array.IndexOf(UtilityPotions, type) >= 0) {
                return true;
            }
            if (!item.consumable || item.maxStack <= 1 || ItemID.Sets.IsFood[type] || item.createTile >= 0 || item.createWall >= 0) {
                return false;
            }
            if (item.useStyle != ItemUseStyleID.DrinkLiquid) {
                return false;
            }
            return (item.buffType > 0 && item.buffTime > 0) || item.healLife > 0 || item.healMana > 0;
        }
    }
}
