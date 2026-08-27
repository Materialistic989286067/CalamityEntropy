using CalamityEntropy.Content.Rarities;
using CalamityEntropy.Content.Tiles;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Armor.Marivinium
{
    [AutoloadEquip(EquipType.Legs)]
    public class MariviniumLeggings : ModItem
    {
        public override void SetDefaults()
        {
            Item.width = 34;
            Item.height = 18;
            Item.value = Item.buyPrice(platinum: 2, gold: 80);
            Item.defense = 56;
            Item.rare = ModContent.RarityType<AbyssalBlue>();
        }

        public override void UpdateEquip(Player player)
        {
            player.Entropy().moveSpeed += 0.36f;
            player.Entropy().ManaCost -= 0.2f;
            player.GetDamage(DamageClass.Generic) += 0.05f;
            player.GetCritChance(DamageClass.Generic) += 5;

        }
        public override void AddRecipes()
        {
            // 脱离灾厄:灾厄欧米茄蓝腿甲改为蘑菇矿潜袭护腿(表外裁定,档位由龙牙把关)
            CreateRecipe()
                .AddIngredient(ItemID.ShroomiteLeggings)
                .AddIngredient<WyrmTooth>(5)
                .AddIngredient<FadingRunestone>()
                .AddTile<AbyssalAltarTile>()
                .Register();
        }
    }

}
