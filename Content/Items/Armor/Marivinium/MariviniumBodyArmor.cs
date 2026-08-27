using CalamityEntropy.Content.Rarities;
using CalamityEntropy.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Armor.Marivinium
{
    [AutoloadEquip(EquipType.Body)]
    public class MariviniumBodyArmor : ModItem
    {

        public override void SetDefaults()
        {
            Item.width = 34;
            Item.height = 18;
            Item.value = Item.buyPrice(platinum: 2, gold: 80);
            Item.defense = 60;
            Item.rare = ModContent.RarityType<AbyssalBlue>();
        }

        public override void UpdateEquip(Player player)
        {
            player.Entropy().mariviniumBody = true;
            player.GetDamage(DamageClass.Generic) += 0.15f;
            player.GetCritChance(DamageClass.Generic) += 15;
        }

        public override void AddRecipes()
        {
            // 脱离灾厄:灾厄欧米茄蓝甲改为蘑菇矿潜袭胸甲(表外裁定,档位由龙牙把关)
            CreateRecipe()
                .AddIngredient(ItemID.ShroomiteBreastplate)
                .AddIngredient<WyrmTooth>(6)
                .AddIngredient<FadingRunestone>()
                .AddTile<AbyssalAltarTile>()
                .Register();
        }
    }
}
