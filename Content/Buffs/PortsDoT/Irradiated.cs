using CalamityEntropy.Core;

namespace CalamityEntropy.Content.Buffs.PortsDoT
{
    /// <summary>辐射：10/s（灾厄的水蛭弹幕叠加逻辑属它物专用，不移植）</summary>
    public class Irradiated : CEPortDebuff
    {
        public override CEDoTEntry Entry => new CEDoTEntry { LostRegen = 20 };
    }
}
