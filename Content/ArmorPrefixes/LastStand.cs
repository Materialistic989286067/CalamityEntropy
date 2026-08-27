using CalamityEntropy.Common;
using Terraria;

namespace CalamityEntropy.Content.ArmorPrefixes
{
    public class LastStand : ArmorPrefix
    {
        public override void UpdateEquip(Player player, Item item)
        {
            player.Entropy().damageReduce += 0.02f;
            player.Entropy().LastStand = true;
        }
        public override float AddDefense()
        {
            return 0.15f;
        }
        public override int getRollChance()
        {
            return 1;
        }
        public override Color getColor()
        {
            return Color.Violet;
        }
        public override bool Dramatic()
        {
            return true;
        }
        public override bool Precious()
        {
            return true;
        }
        public override bool? canApplyTo(Item item)
        {
            // 脱离灾厄:原终灾门槛按 progression-map.md 重映射为击败巡游者
            if (!EDownedBosses.downedCruiser)
            {
                return false;
            }
            return Main.rand.NextBool(3);
        }
    }
}
