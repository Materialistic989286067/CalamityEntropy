using CalamityEntropy.Core;

namespace CalamityEntropy.Content.Buffs.PortsDoT
{
    /// <summary>朱红涌流：静止 37.5/s，横向移动中 ×4 = 150/s</summary>
    public class VermillionFlux : CEPortDebuff
    {
        public override CEDoTEntry Entry => new CEDoTEntry { LostRegen = 75, ElectricMoving = true };
    }
}
