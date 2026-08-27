namespace CalamityEntropy.Content.Buffs.PortsDoT
{
    /// <summary>时滞哀伤：每帧速度 ÷1.2（结算在 CEDoTGlobalNPC.PostAI）</summary>
    public class TemporalSadness : CEPortDebuff
    {
        public override bool LongerExpert => false;
    }
}
