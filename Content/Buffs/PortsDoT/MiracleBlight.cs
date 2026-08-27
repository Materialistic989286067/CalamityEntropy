using CalamityEntropy.Core;

namespace CalamityEntropy.Content.Buffs.PortsDoT
{
    /// <summary>奇迹凋朽：1500/s（灾厄的全屏 shader 视觉不移植）</summary>
    public class MiracleBlight : CEPortDebuff
    {
        public override CEDoTEntry Entry => new CEDoTEntry { LostRegen = 3000 };
    }
}
