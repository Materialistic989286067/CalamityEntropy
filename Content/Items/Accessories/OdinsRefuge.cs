using CalamityEntropy.Content.Items.Armor.Marivinium;
using CalamityEntropy.Content.Rarities;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Accessories
{
    [AutoloadEquip(EquipType.Shield)]
    public class OdinsRefuge : ModItem
    {
        // 2026-08-31 平衡案重做:18防,免疫大多数减益和击退,免疫火块,
        // 拥有神圣屏障格挡,给自己与所有队友15%免伤(不叠加),+600仇恨。
        // 原盾冲/圣佑窗口/恐慌项链切换均退役。
        public const float TeamWardDR = 0.15f;

        public override void SetDefaults()
        {
            Item.width = 86;
            Item.height = 86;
            Item.value = Item.buyPrice(platinum: 1, gold: 50);
            Item.rare = ModContent.RarityType<VoidPurple>();
            Item.accessory = true;
            Item.defense = 18;

        }

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            // 神圣屏障格挡
            player.Entropy().holyMantle = true;
            // 团队免伤光环(结算在 EModPlayer 的减伤汇总处,不可叠加)
            player.Entropy().odinAura = true;
            player.noKnockback = true;
            player.fireWalk = true;
            player.buffImmune[BuffID.OnFire] = true;
            player.buffImmune[BuffID.OnFire3] = true;
            // 免疫大多数减益(与渊洋神迹共用免疫表)
            MariviniumHelmet.ApplyBuffImmune(player);
            player.aggro += 600;
        }

        public override void AddRecipes()
        {
            // "十字章护盾"按最接近的原版物品裁定为十字章项链
            CreateRecipe().
                AddIngredient(ItemID.CrossNecklace, 1).
                AddIngredient(ItemID.HeroShield, 1).
                AddIngredient(ModContent.ItemType<HolyMantle>(), 1).
                AddIngredient(ModContent.ItemType<ChaoticPiece>(), 15).
                AddTile(TileID.LunarCraftingStation).
                Register();
        }
    }
}
