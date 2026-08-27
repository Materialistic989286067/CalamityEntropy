namespace CalamityEntropy.Content.Buffs.PortsDoT
{
    /// <summary>死亡标记：受到的一切伤害 ×1.1（结算在 CEDoTGlobalNPC.ModifyIncomingHit）</summary>
    public class MarkedforDeath : CEPortDebuff
    {
        public const float DamageTakenMult = 1.1f;

        public override bool LongerExpert => false;
    }
}
