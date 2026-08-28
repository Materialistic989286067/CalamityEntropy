using CalamityEntropy.Content.Items.Lores;
using CalamityEntropy.Content.Rarities;
using Terraria;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.VoidInvasion
{
    /// <summary>
    /// 虚空教皇传颂之物(void-invasion.md §5.3):首杀按人掉落的 Lore 物品,
    /// 佩戴(启用)效果:受到虚空入侵敌人的伤害降低 5%(LEVoidPope,对齐现役传颂物强度档)。
    /// </summary>
    public class VoidPopeLore : CELoreItem
    {
        /// <summary>虚空系受伤降低比例(§5.3:-5% 档)</summary>
        public static float voidDamageReduction = 0.05f;

        public override void SetDefaults()
        {
            Item.width = 30;
            Item.height = 40;
            Item.rare = ModContent.RarityType<VoidPurple>();
            Item.maxStack = 1;
        }
    }
}
