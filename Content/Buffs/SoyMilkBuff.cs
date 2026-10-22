using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Buffs
{
    public class SoyMilkBuff : ModBuff
    {
        public int counter;
        public const int SpeedMult = 2;  //攻速与弹幕更新倍数
        public const float ProjDamageMult = 0.45f;  //玩家弹幕最终伤害系数
        public override void SetStaticDefaults() {
            Main.debuff[Type] = false;
            Main.pvpBuff[Type] = true;
            Main.buffNoSave[Type] = false;
            BuffID.Sets.LongerExpertDebuff[Type] = false;

        }

        public override void Update(Player player, ref int buffIndex) {
            // 灾厄潜行清零随潜行系统退役移除
            player.GetAttackSpeed(DamageClass.Generic) *= SpeedMult;
            player.pickSpeed -= 0.6f;
        }
    }
}
