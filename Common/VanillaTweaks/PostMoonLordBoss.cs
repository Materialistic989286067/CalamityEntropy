using CalamityEntropy.Content.NPCs.Cruiser;
using CalamityEntropy.Content.NPCs.NihilityTwin;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using VDBoss = CalamityEntropy.Content.NPCs.VoidDestroyer.VoidDestroyer;

namespace CalamityEntropy.Common.VanillaTweaks
{
    /// <summary>月亮领主与本模组月后 Boss 的在场判定,和谐杖与混沌传送杖共用</summary>
    public static class PostMoonLordBoss
    {
        public static bool IsPostMoonLordBoss(int npcType) {
            return npcType == NPCID.MoonLordCore
                || npcType == ModContent.NPCType<NihilityActeriophage>()
                || npcType == ModContent.NPCType<CruiserHead>()
                || npcType == ModContent.NPCType<VDBoss>();
        }

        public static bool IsFighting() {
            foreach (NPC npc in Main.ActiveNPCs) {
                if (IsPostMoonLordBoss(npc.type)) {
                    return true;
                }
            }
            return false;
        }
    }
}
