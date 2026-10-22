using Terraria;
using Terraria.ModLoader;

namespace CalamityEntropy.Common.VanillaTweaks
{
    /// <summary>常规伤害减免与闪避几率的上限,在受击结算前夹紧,不受各来源累加顺序影响</summary>
    public class ReductionCapPlayer : ModPlayer
    {
        public const float EnduranceCap = 0.75f;
        public const float DodgeCap = 0.15f;

        public override void ModifyHurt(ref Player.HurtModifiers modifiers) {
            if (Player.endurance > EnduranceCap) {
                Player.endurance = EnduranceCap;
            }
            EModPlayer entropy = Player.Entropy();
            if (entropy.dodgeChance > DodgeCap) {
                entropy.dodgeChance = DodgeCap;
            }
        }
    }
}
