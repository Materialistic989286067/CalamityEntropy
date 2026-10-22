using CalamityEntropy.Content.Rarities;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Potions
{
    public class VoidManaPotion : ModItem
    {
        public override void SetStaticDefaults() {
            Item.ResearchUnlockCount = 30;
            ItemID.Sets.DrinkParticleColors[Type] = new Color[3]
            {
                new Color(0, 255, 250),
                new Color(40, 140, 220),
                new Color(20, 50, 140)
            };
        }

        public override void SetDefaults() {
            Item.DefaultToFood(24, 32, 0, 0, true);
            Item.healMana = 400;
            Item.value = Item.sellPrice(silver: 10);
            Item.rare = ModContent.RarityType<NihilityBlue>();
        }

        public override void AddRecipes() {
            CreateRecipe(25)
                .AddIngredient(ItemID.BottledWater, 5)
                .AddIngredient<WraithSoulEssence>(2)
                .AddTile(TileID.AlchemyTable)
                .DisableDecraft()
                .Register();
        }

        //魔力病时长:原版固定追加 5 秒,UseItem 记旧值,OnConsumeItem 把新增部分缩到 3 秒
        private const float ManaSickScale = 180f / 300f;
        private int manaSickBefore;

        public override bool? UseItem(Player player) {
            int idx = player.FindBuffIndex(BuffID.ManaSickness);
            manaSickBefore = idx >= 0 ? player.buffTime[idx] : 0;
            return null;
        }

        public override void OnConsumeItem(Player player) {
            int idx = player.FindBuffIndex(BuffID.ManaSickness);
            if (idx < 0) {
                return;
            }
            int added = player.buffTime[idx] - manaSickBefore;
            if (added > 0) {
                player.buffTime[idx] = manaSickBefore + (int)(added * ManaSickScale);
            }
        }
    }
}
