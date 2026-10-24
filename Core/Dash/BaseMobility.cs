using CalamityEntropy.Common;
using Terraria.ModLoader;

namespace CalamityEntropy.Core.Dash
{
    /// <summary>基础冲刺,距离很短,优先级垫底,任何饰品冲刺都会盖过它</summary>
    public sealed class BaseMobilityDash : CEDashEffect
    {
        public override string ID => "BaseMobilityDash";
        public override int Priority => -100;
        public override int Duration => 4;
        public override float Distance => 1 * 16f;
        public override int Cooldown => 60;
        public override float Curve => 1.6f;
    }

    /// <summary>ServerConfig.BaseMobilityBoost 开启后给 +25% 移动速度和基础冲刺</summary>
    public class BaseMobilityPlayer : ModPlayer
    {
        public const float MoveSpeedBonus = 0.25f;

        public override void PostUpdateEquips() {
            if (!ServerConfig.Instance.BaseMobilityBoost)
                return;
            Player.moveSpeed += MoveSpeedBonus;
            //原版冲刺(盾、忍者装备、套装)存在时不登记,免得压掉它们的双击
            if (Player.dashType == 0)
                Player.GetModPlayer<CEDashPlayer>().Offer(CEDashRegistry.Get<BaseMobilityDash>());
        }
    }
}
