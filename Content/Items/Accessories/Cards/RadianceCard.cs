using CalamityEntropy.Common;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Accessories.Cards
{
    public class RadianceCard : ModItem
    {
        public static float LifeRegenMul = 0.2f; //+20%生命恢复

        public override void SetDefaults()
        {
            Item.width = 22;
            Item.height = 22;
            Item.value = Item.buyPrice(gold: 5);
            Item.rare = ItemRarityID.Orange;
            Item.accessory = true;

        }

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            player.lifeRegen = (int)(player.lifeRegen * (1 + LifeRegenMul));
            player.GetModPlayer<EModPlayer>().radianceCard = true;
        }
        public override void ModifyTooltips(List<TooltipLine> tooltips)
        {
            tooltips.Replace("[LR]", (int)(Math.Round(LifeRegenMul * 100)));
        }

        public override void AddRecipes()
        {
            // 脱离灾厄:StarblightSoot→星辉鳞尘,EssenceofSunlight 与原有光明之魂合并
            CreateRecipe()
                .AddIngredient(ModContent.ItemType<StarlitScaleDust>(), 5)
                .AddIngredient(ItemID.SoulofLight, 8)
                .AddTile(TileID.CrystalBall)
                .Register();
        }
    }
}
