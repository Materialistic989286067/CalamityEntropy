using CalamityEntropy.Core;

namespace CalamityEntropy.Content.Buffs.PortsDoT
{
    /// <summary>圣金斥退：静止 100/s，横向移动中 ×4 = 400/s</summary>
    public class AuricRebuke : CEPortDebuff
    {
        public override CEDoTEntry Entry => new CEDoTEntry { LostRegen = 200, ElectricMoving = true };
    }
}
