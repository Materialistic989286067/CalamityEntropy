using CalamityEntropy.Core;

namespace CalamityEntropy.Content.Buffs.PortsDoT
{
    /// <summary>瘟疫：50/s</summary>
    public class Plague : CEPortDebuff
    {
        public override CEDoTEntry Entry => new CEDoTEntry { LostRegen = 100 };
    }
}
