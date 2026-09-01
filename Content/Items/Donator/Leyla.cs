using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Donator
{
    public class Leyla : ModItem, IDonatorItem
    {
        public string DonatorName => "Fortun3Rod1on";
        // 2026-08-31 平衡案重做:去除成长属性。6防,+40生命,+2hp/s生命再生,
        // 给自己与附近队友蜂蜜效果,+50% debuff伤害,攻击造成霜冻/酸性毒液/咒火。
        public const float DoTBonus = 0.5f;

        public override void SetDefaults()
        {
            Item.width = 32;
            Item.height = 32;
            Item.value = Item.buyPrice(gold: 2);
            Item.rare = ItemRarityID.Yellow;
            Item.accessory = true;
            Item.defense = 6;
        }

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            player.Entropy().addEquip("Leyla", !hideVisual);
            player.Entropy().leylaAura = true;
            player.statLifeMax2 += 40;
            player.lifeRegen += 4;
        }
        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient(ModContent.ItemType<Accessories.SilvasCrown>())
                .AddIngredient(ItemID.BottledHoney, 10)
                .AddIngredient(ItemID.Sunflower)
                .AddIngredient(ItemID.Ruby, 2)
                .AddTile(TileID.LunarCraftingStation)
                .Register();
        }

        /// <summary>攻击附加的减益:霜冻、酸性毒液(原版剧毒)、咒火。</summary>
        public static List<int> ApplyBuffType()
        {
            return new List<int>
            {
                BuffID.Frostburn,
                BuffID.Venom,
                BuffID.CursedInferno
            };
        }
    }
}
