using Terraria;

namespace CalamityEntropy.Content.Buffs.PortsDoT
{
    /// <summary>破甲：受击时防御 -15（NPC 侧结算在 CEDoTGlobalNPC.ModifyIncomingHit，玩家侧在本类 Update）</summary>
    public class ArmorCrunch : CEPortDebuff
    {
        public const int DefenseReduction = 15;

        public override bool NurseCannotRemove => true;

        //虚空护教骑士(M2)起会把破防上到玩家身上,补玩家侧防御结算
        public override void Update(Player player, ref int buffIndex)
        {
            player.statDefense -= DefenseReduction;
        }
    }
}
