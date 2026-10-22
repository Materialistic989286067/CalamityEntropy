using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Common.VanillaTweaks
{
    public class PostMoonLordRodPlayer : ModPlayer
    {
        /// <summary>原版混沌传送杖施加混乱状态的帧数</summary>
        public const int VanillaChaosFrames = 6 * 60;

        /// <summary>月后 Boss 战里的帧数,按与原版的比例放大实际新增时长,保留冷却倍率</summary>
        public const int BossChaosFrames = 11 * 60;

        private int chaosBefore;

        public override bool CanBeTeleportedTo(Vector2 teleportPosition, string context) {
            if (context == "TeleportRod" && Player.HasBuff(BuffID.ChaosState) && PostMoonLordBoss.IsFighting()) {
                return false;
            }
            return true;
        }

        public override bool PreItemCheck() {
            int index = Player.FindBuffIndex(BuffID.ChaosState);
            chaosBefore = index >= 0 ? Player.buffTime[index] : 0;
            return true;
        }

        public override void PostItemCheck() {
            if (Player.whoAmI != Main.myPlayer || Player.HeldItem.type != ItemID.RodofDiscord || !PostMoonLordBoss.IsFighting()) {
                return;
            }
            int index = Player.FindBuffIndex(BuffID.ChaosState);
            //只在这一帧新加上混乱状态时放大
            if (index >= 0 && Player.buffTime[index] > chaosBefore) {
                Player.buffTime[index] = (int)(Player.buffTime[index] * (BossChaosFrames / (float)VanillaChaosFrames));
            }
        }
    }
}
