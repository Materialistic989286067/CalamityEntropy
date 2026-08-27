namespace CalamityEntropy.Content.Buffs.PortsDoT
{
    /// <summary>碎甲：受击时防御 -8（灾厄原为 DR×0.92 的近似）</summary>
    public class Crumbling : CEPortDebuff
    {
        public const int DefenseReduction = 8;

        public override bool NurseCannotRemove => true;
    }
}
