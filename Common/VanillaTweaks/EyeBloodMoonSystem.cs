using Microsoft.Xna.Framework;
using System.IO;
using Terraria;
using Terraria.Chat;
using Terraria.GameContent.Achievements;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace CalamityEntropy.Common.VanillaTweaks
{
    /// <summary>首次击败克苏鲁之眼后,下一次黎明之后的第一个黄昏必定血月</summary>
    public class EyeBloodMoonSystem : ModSystem
    {
        public const int Idle = 0;
        public const int WaitDawn = 1;
        public const int WaitDusk = 2;

        /// <summary>世界级进度,只由服务端与单人推进</summary>
        public static int Stage;

        private bool lastDayTime = true;

        public static void Arm() {
            if (Main.netMode != NetmodeID.MultiplayerClient && Stage == Idle) {
                Stage = WaitDawn;
            }
        }

        public override void ClearWorld() {
            Stage = Idle;
        }

        public override void OnWorldLoad() {
            lastDayTime = Main.dayTime;
        }

        public override void SaveWorldData(TagCompound tag) {
            if (Stage != Idle) {
                tag["EyeBloodMoonStage"] = Stage;
            }
        }

        public override void LoadWorldData(TagCompound tag) {
            Stage = tag.GetInt("EyeBloodMoonStage");
        }

        public override void NetSend(BinaryWriter writer) {
            writer.Write((byte)Stage);
        }

        public override void NetReceive(BinaryReader reader) {
            Stage = reader.ReadByte();
        }

        public override void PostUpdateTime() {
            bool day = Main.dayTime;
            if (Main.netMode != NetmodeID.MultiplayerClient && Stage != Idle && day != lastDayTime) {
                if (day && Stage == WaitDawn) {
                    Stage = WaitDusk;
                }
                else if (!day && Stage == WaitDusk) {
                    Stage = Idle;
                    StartBloodMoon();
                }
            }
            lastDayTime = day;
        }

        private static void StartBloodMoon() {
            //原版已在这个黄昏抽中血月时不重复播报
            if (Main.bloodMoon) {
                return;
            }
            Main.bloodMoon = true;
            Main.sundialCooldown = 0;
            Main.moondialCooldown = 0;
            AchievementsHelper.NotifyProgressionEvent(4);
            if (Main.netMode == NetmodeID.SinglePlayer) {
                Main.NewText(Lang.misc[8].Value, 50, 255, 130);
            }
            else if (Main.netMode == NetmodeID.Server) {
                ChatHelper.BroadcastChatMessage(Lang.misc[8].ToNetworkText(), new Color(50, 255, 130));
                NetMessage.SendData(MessageID.WorldData);
            }
        }
    }
}
