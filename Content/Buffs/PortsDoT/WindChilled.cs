using CalamityEntropy.Core;

namespace CalamityEntropy.Content.Buffs.PortsDoT
{
    /// <summary>风寒：6/s，目标浸湿时 ×1.5</summary>
    public class WindChilled : CEPortDebuff
    {
        public override CEDoTEntry Entry => new CEDoTEntry { LostRegen = 12, WetBoost = true };
    }
}
