using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Accessories
{
    public class ShadeCloak : ModItem
    {
        // 2026-08-31 平衡案重做:自带暗影冲刺(忍者大师足具同源),冲刺期间无敌,
        // 固定5秒冷却,装备期间排斥其他冲刺来源。
        public static int CooldownTicks = 5 * 60;
        public override void SetDefaults()
        {
            Item.width = 42;
            Item.height = 42;
            Item.value = Item.buyPrice(gold: 20);
            Item.rare = ItemRarityID.Orange;
            Item.accessory = true;
            Item.expert = true;
        }
        public static string ID = "ShadeCloak";

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            player.Entropy().addEquip(ID, !hideVisual);
            player.Entropy().shadeDashExclusive = true;
            if (player.GetModPlayer<SCDashMP>().Cooldown <= 0)
            {
                player.dashType = 1;
            }
            else
            {
                player.dashType = 0;
            }
        }
        public override void UpdateVanity(Player player)
        {
            player.Entropy().addEquipVisual(ID);
        }
        public override void AddRecipes()
        {
            /*CreateRecipe().AddIngredient(ItemID.SoulofNight, 8)
                .AddIngredient(ItemID.Ectoplasm, 12)
                .AddIngredient(ItemID.SoulofNight, 4)
                .AddIngredient(ItemID.Ectoplasm, 8);*/
        }
    }
    public class SCDashMP : ModPlayer
    {
        public bool flag = true;
        public int Cooldown = 0;
        public override void PostUpdate()
        {

        }
    }
}
