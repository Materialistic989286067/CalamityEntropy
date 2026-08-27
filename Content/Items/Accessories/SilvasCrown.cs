using CalamityEntropy.Content.Rarities;
using Terraria;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Accessories
{
    public class SilvasCrown : ModItem
    {
        public static float DDR = 0.25f;

        public override void SetDefaults()
        {
            Item.width = 42;
            Item.height = 42;
            Item.defense = 5;
            Item.value = Item.buyPrice(gold: 45);
            Item.rare = ModContent.RarityType<GlowGreen>();
            Item.accessory = true;


        }

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            // 脱离灾厄:灾厄防御损伤机制(defenseDamageRatio)随灾厄退场删除(player-api.md §5),自有 SCrown 效果保留
            player.Entropy().SCrown = true;
        }

    }
}
