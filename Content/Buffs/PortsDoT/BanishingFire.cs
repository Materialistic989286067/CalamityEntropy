using CalamityEntropy.Core;

namespace CalamityEntropy.Content.Buffs.PortsDoT
{
    /// <summary>放逐之焰：2000/s；目标生命上限 ≥ 100 万时按上限 0.2%/s 结算</summary>
    public class BanishingFire : CEPortDebuff
    {
        public override CEDoTEntry Entry => new CEDoTEntry { LostRegen = 4000, ScaleWithMaxLife = true };
    }
}
