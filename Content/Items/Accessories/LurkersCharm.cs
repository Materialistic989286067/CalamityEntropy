using CalamityEntropy.Core.Weapons;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Accessories
{
    public class LurkersCharm : ModItem
    {
        public static float damage = 0.12f;
        public static float MoveSpeed = 0.10f;
        public static float endurance = 0.10f;
        // 大招充能速度 +12%(原潜行回复 8%×1.5,rogue-weapons.md §三)
        public static float chargeRate = 0.12f;
        public override void SetDefaults()
        {
            Item.width = 42;
            Item.defense = 4;
            Item.height = 42;
            Item.value = Item.buyPrice(gold: 20);
            Item.rare = ItemRarityID.Blue;
            Item.accessory = true;
        }
        public override void ModifyTooltips(List<TooltipLine> tooltips)
        {
            tooltips.Replace("[A]", damage.ToPercent());
            tooltips.Replace("[B]", MoveSpeed.ToPercent());
            tooltips.Replace("[D]", endurance.ToPercent());
            tooltips.Replace("[C]", chargeRate.ToPercent());

        }
        public static string ID = "LurkersCharm";

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            // 新效果:盗贼伤害转全伤害,潜行回复转大招充能速度(潜行体系退役)
            player.GetDamage(DamageClass.Generic) += damage;
            player.Entropy().moveSpeed += MoveSpeed;
            player.GetModPlayer<CEChargePlayer>().ChargeRateMult += chargeRate;
            player.Entropy().addEquip(ID, !hideVisual);
        }
        public override void AddRecipes()
        {
            // 脱离灾厄:灾厄盗贼徽章(RogueEmblem)改为任一原版职业徽章的平行配方
            int[] emblems = [ItemID.WarriorEmblem, ItemID.RangerEmblem, ItemID.SorcererEmblem, ItemID.SummonerEmblem];
            foreach (int emblem in emblems)
            {
                CreateRecipe().AddIngredient(ItemID.Magiluminescence)
                    .AddIngredient(emblem)
                    .AddIngredient(ItemID.SoulofNight, 4)
                    .AddTile(TileID.MythrilAnvil)
                    .Register();
            }
        }
    }
}
