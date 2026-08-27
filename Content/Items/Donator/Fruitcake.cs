using CalamityEntropy.Common;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Donator
{
    public class Fruitcake : ModItem, IDonatorItem
    {
        public static Dictionary<int, List<int>> ammoList = new();
        public string DonatorName => "永霞伊";
        public override void SetDefaults()
        {
            Item.width = 40;
            Item.height = 40;
            Item.value = Item.buyPrice(gold: 60);
            Item.rare = ItemRarityID.Yellow;
            Item.accessory = true;
        }

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            player.Entropy().fruitCake = true;
        }
        public override void AddRecipes()
        {
            // 灾厄原料按 material-map.md 替换：OverloadedSludge→史莱姆王冠（与原有王冠合并为2）、PurifiedGel→粉凝胶
            CreateRecipe()
                .AddIngredient(ItemID.WoodenArrow)
                .AddIngredient(ItemID.SlimeCrown, 2)
                .AddIngredient(ItemID.PinkGel, 8)
                .Register();
        }
        public override void ModifyTooltips(List<TooltipLine> tooltips)
        {
            for (int i = tooltips.Count - 1; i >= 0; i--)
            {
                if (tooltips[i].Mod == "Terraria" && tooltips[i].Text.StartsWith("#"))
                {
                    bool hide = true;
                    if (int.TryParse(tooltips[i].Text[1].ToString(), out int n))
                    {
                        if (Level() >= n)
                        { hide = false; }
                    }
                    tooltips[i].Text = tooltips[i].Text.Substring(2);
                    if (hide)
                    {
                        tooltips.RemoveAt(i);
                    }
                }
            }
        }
        public static int Level()
        {
            // 成长阶梯按 progression-map.md 重排：原版节点 + 自有 Boss 线
            int l = 0;
            if (NPC.downedSlimeKing || NPC.downedBoss1 || NPC.downedBoss2)
            {
                l = 1;
            }
            if (NPC.downedBoss2)
            {
                l = 2;
            }
            if (EDownedBosses.downedApsychos)
            {
                l = 3;
            }
            if (NPC.downedMechBossAny)
            {
                l = 4;
            }
            if (EDownedBosses.downedProphet)
            {
                l = 5;
            }
            if (NPC.downedMoonlord)
            {
                l = 6;
            }
            if (EDownedBosses.downedNihilityTwin)
            {
                l = 7;
            }
            return l;
        }
    }
}
