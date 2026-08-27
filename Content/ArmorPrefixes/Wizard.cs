using CalamityEntropy.Common;
using Terraria;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.ArmorPrefixes
{
    public class Wizard : ArmorPrefix
    {
        public override void UpdateEquip(Player player, Item item)
        {
            player.GetDamage(DamageClass.Generic) -= 0.10f;
            player.maxMinions += 1;
        }
        public override bool? canApplyTo(Item item)
        {
            // 脱离灾厄:原终灾门槛按 progression-map.md 重映射为击败巡游者
            if (!EDownedBosses.downedCruiser)
            {
                return false;
            }
            return base.canApplyTo(item);
        }
        public override int getRollChance()
        {
            return 1;
        }
    }
}
