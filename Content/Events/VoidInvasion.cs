using CalamityEntropy.Common;
using CalamityEntropy.Content.NPCs.VoidInvasion;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using CalamityEntropy.Content.Projectiles.VoidInvasion;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.Audio;
using Terraria.Chat;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace CalamityEntropy.Content.Events
{
    /// <summary>
    /// 虚空入侵事件框架(void-invasion.md §一):激活态、进度经济、存档与联机同步、进度条 UI。
    /// 权威端在服务器:进度只走 <see cref="AddProgress"/> 结算,客户端只读 <see cref="Progress"/> 画 UI。
    /// </summary>
    public class VoidInvasion : ModSystem
    {
        public static bool Active = false;
        /// <summary>事件进度 0~1。击杀结算钳 0.99,虚熵魔物真死经 SetVictory 收到 1.0(§1.2)。</summary>
        public static float Progress = 0;
        /// <summary>50% 脚本恶灵是否已给过(存档字段;胜利与清档时重置,下一场事件再次可用)。</summary>
        public static bool spawned50Wraith = false;
        /// <summary>定位仪使用后的开场倒计时(§1.1 的 150t)。服务端权威推进,客户端仅作本地镜像挡重复使用,不存档。</summary>
        public static int StartCountdown = 0;
        /// <summary>本端上一帧激活态,用于客户端检测事件开启拍音效。</summary>
        private static bool prevActive = false;
        /// <summary>
        /// 场上是否有存活红衣主教(§2.3):每 tick 双端各自缓存(击打端的 ModifyIncomingHit 结算需要本地值)。
        /// 世界级状态,static 合规;减伤光环在 EGlobalNPC 侧读它。
        /// </summary>
        public static bool CardinalAlive = false;
        /// <summary>主教重生冷却(§2.3:距上一只死亡 ≥60s,VoidCardinal.OnKill 置位)。不进存档,重进世界重置。</summary>
        public static int CardinalRespawnCooldown = 0;
        //主教入场脚本状态(仅服务端读写):门已开、等待门心出怪的倒计时与落点
        private static int cardinalSpawnDelay = 0;
        private static Vector2 cardinalSpawnPos;
        /// <summary>
        /// 场上是否有存活虚熵魔物(§1.4):每 tick 双端缓存,镜像 CardinalAlive 的写法。
        /// 在场时清池停刷、主教脚本与其传送门投放全停(守门决斗)。
        /// </summary>
        public static bool EntropyFiendAlive = false;
        /// <summary>魔物脱战后的重生等待(§1.2:10s 后重新生成)。仅服务端读写,不存档。</summary>
        public static int FiendRespawnDelay = 0;
        //魔物脱战位置:重生锚点取"离它最近的地表玩家";Zero = 本场还没生成过(取随机地表玩家)
        private static Vector2 fiendLastPos = Vector2.Zero;
        //50% 脚本恶灵状态(仅服务端读写):门已开、等待门心出怪
        private static int wraith50Delay = 0;
        private static Vector2 wraith50Pos;

        /// <summary>
        /// 服务端进度入口(§1.3):只在服务端/单人生效,进度只增不减,
        /// 上限钳 0.99(99% 守门:虚熵魔物真死后由 SetVictory 收到 100%)。
        /// </summary>
        public static void AddProgress(float amount)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient || !Active || amount <= 0)
                return;
            float old = Progress;
            //钳 0.99(§1.2 守门):到 0.99 后停刷与虚熵魔物脚本在 EditSpawnPool / UpdateFiendSpawn 侧接管
            Progress = Math.Min(Progress + amount, 0.99f);
            if (Progress != old)
                SyncEvent();
        }

        /// <summary>
        /// 胜利结算(§1.2):虚熵魔物死亡演出末尾的真死(OnKill)调用。
        /// 进度置 1.0(99%→100% 语义,事件关闭后进度条随之收起)、关事件、落 downed 旗标、
        /// 重置 50% 脚本恶灵标记(下一场事件重新可用)、清脚本暂存、广播并同步。
        /// </summary>
        public static void SetVictory()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;
            Progress = 1f;
            Active = false;
            StartCountdown = 0;
            spawned50Wraith = false;
            wraith50Delay = 0;
            FiendRespawnDelay = 0;
            fiendLastPos = Vector2.Zero;
            //SetEventFlagCleared 在服务器上自动同步 WorldData,镜像 AbyssalWraith 的旗标落地写法
            NPC.SetEventFlagCleared(ref EDownedBosses.downedVoidInvasion, -1);
            BroadcastText("Mods.CalamityEntropy.VoidInvasion.Victory");
            SyncEvent();
        }

        /// <summary>
        /// 魔物脱战回调(§1.2:玩家全灭/远离,EntropyFiend 侧在 despawn 前调用,仅服务端):
        /// 进度保持 99%,10s 后在离脱战位置最近的地表玩家附近重新生成。真死走 SetVictory,与本路径互斥。
        /// </summary>
        public static void OnFiendEscape(Vector2 pos)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;
            FiendRespawnDelay = 10 * 60;
            fiendLastPos = pos;
        }

        /// <summary>
        /// 定位仪使用入口(§1.1):服务端/单人调用,广播开场文本并启动 150t 倒计时,倒计时走完才置 Active。
        /// </summary>
        public static void BeginStartCountdown()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient || Active || StartCountdown > 0)
                return;
            StartCountdown = 150;
            BroadcastText("Mods.CalamityEntropy.VoidInvasion.Begin");
        }

        /// <summary>
        /// 服务端广播事件状态(CEMessageType.SyncVoidInvasion)。
        /// 载荷 = bool Active + float Progress + bool spawned50Wraith,与 CENetWork.Handle 的读取端逐字节一致。
        /// </summary>
        public static void SyncEvent()
        {
            if (Main.netMode != NetmodeID.Server)
                return;
            ModPacket packet = CalamityEntropy.Instance.GetPacket();
            packet.Write((byte)CEMessageType.SyncVoidInvasion);
            packet.Write(Active);
            packet.Write(Progress);
            packet.Write(spawned50Wraith);
            packet.Send();
        }

        /// <summary>全服广播本地化文本,虚空紫。单机直接进聊天栏,服务器走 ChatHelper。</summary>
        private static void BroadcastText(string key)
        {
            Color color = new Color(150, 80, 255);
            if (Main.netMode == NetmodeID.SinglePlayer)
                Main.NewText(Language.GetTextValue(key), color);
            else if (Main.netMode == NetmodeID.Server)
                ChatHelper.BroadcastChatMessage(NetworkText.FromKey(key), color);
        }

        public override void PostUpdateEverything()
        {
            if (StartCountdown > 0)
            {
                //客户端的本地镜像倒计时只用于挡定位仪重复使用,激活始终由服务端置位后广播
                StartCountdown--;
                if (StartCountdown <= 0 && !Active && Main.netMode != NetmodeID.MultiplayerClient)
                {
                    Active = true;
                    SyncEvent();
                }
            }
            //主教/魔物在场缓存(§2.3/§1.4):双端各自扫,世界级状态
            CardinalAlive = NPC.AnyNPCs(ModContent.NPCType<VoidCardinal>());
            EntropyFiendAlive = NPC.AnyNPCs(ModContent.NPCType<EntropyFiend>());
            if (Active)
            {
                Main.LocalPlayer.Entropy().VortexSky = 5;
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    UpdateCardinalSpawn();
                    Update50WraithSpawn();
                    UpdateFiendSpawn();
                }
            }
            else
            {
                //衔接改动3(void-invasion.md §八):事件未激活时进度归零(胜利演出在魔物死亡侧,结算时事件已收口)
                Progress = 0;
                //事件中断时丢弃未完成的脚本暂存,防止事件外补生成
                cardinalSpawnDelay = 0;
                wraith50Delay = 0;
                FiendRespawnDelay = 0;
            }
            if (!Main.dedServ)
            {
                //开场倒计时氛围(仅使用者本机有镜像倒计时):身周虚空浮尘渐密,空间"开始不对劲"
                if (StartCountdown > 0 && !Active && !Main.gamePaused)
                {
                    float ramp = 1f - StartCountdown / 150f;
                    if (Main.rand.NextFloat() < 0.25f + 0.5f * ramp)
                    {
                        Vector2 pos = Main.LocalPlayer.Center + new Vector2(Main.rand.NextFloat(-700f, 700f), Main.rand.NextFloat(-80f, 320f));
                        var p = PRTLoader.NewParticle<PRT_Void>(pos, new Vector2(0, -Main.rand.NextFloat(1f, 2.5f) * (0.5f + ramp)), Color.White, 1f);
                        p.Opacity = 0.35f + 0.3f * ramp;
                    }
                }
                if (Active && !prevActive)
                {
                    //开幕拍:吼声 + 全屏震 + 身周紫环爆开(所有端都走这条,不依赖倒计时镜像)
                    SoundEngine.PlaySound(SoundID.Roar, Main.LocalPlayer.Center);
                    ScreenShaker.AddShake(new ScreenShaker.NoDirQuickShake(6));
                    var ring = PRTLoader.NewParticle<PRT_PulseRing>(Main.LocalPlayer.Center, Vector2.Zero, new Color(170, 80, 255), 0.3f);
                    ring.Configure(6f, 30);
                    var ring2 = PRTLoader.NewParticle<PRT_PulseRing>(Main.LocalPlayer.Center, Vector2.Zero, new Color(255, 255, 255), 0.2f);
                    ring2.Configure(4f, 22);
                    for (int i = 0; i < 22; i++)
                    {
                        Vector2 dir = CEUtils.randomRot().ToRotationVector2();
                        var s = PRTLoader.NewParticle<PRT_GlowSparkCal>(Main.LocalPlayer.Center + dir * 40f, dir * Main.rand.NextFloat(5f, 12f),
                            new Color(190, 110, 255), Main.rand.NextFloat(0.35f, 0.6f));
                        s.Configure(false, 24, new Vector2(0.5f, 1.7f), quickShrink: true);
                    }
                }
                prevActive = Active;
            }
        }

        /// <summary>
        /// 红衣主教脚本生成(§2.3,仅服务端/单人,事件激活时每 tick 调用):
        /// 进度 ≥20%、场上无主教、距上一只死亡 ≥60s → 随机地表玩家旁 400px 开 60t 传送门,
        /// 门张开(40t)后在门心生成主教;入场演出(紫闪 + 雷声)在主教自身 AI 的首拍播放。
        /// </summary>
        private static void UpdateCardinalSpawn()
        {
            //99% 起/魔物在场:主教脚本全停(§1.2/§1.4 守门决斗),挂起的入场一并作废
            if (EntropyFiendAlive || Progress >= 0.99f)
            {
                cardinalSpawnDelay = 0;
                return;
            }
            if (CardinalRespawnCooldown > 0)
            {
                CardinalRespawnCooldown--;
            }
            if (cardinalSpawnDelay > 0)
            {
                cardinalSpawnDelay--;
                if (cardinalSpawnDelay == 0)
                {
                    int np = NPC.NewNPC(new EntitySource_WorldEvent(), (int)cardinalSpawnPos.X, (int)cardinalSpawnPos.Y, ModContent.NPCType<VoidCardinal>());
                    if (np < Main.maxNPCs)
                    {
                        NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, np);
                    }
                }
                return;
            }
            if (Progress < 0.2f || CardinalAlive || CardinalRespawnCooldown > 0)
            {
                return;
            }
            //随机取一名地表玩家(§1.4 同款地表判定)
            var surface = new List<Player>();
            foreach (Player p in Main.ActivePlayers)
            {
                if (!p.dead && (p.ZoneOverworldHeight || p.ZoneSkyHeight))
                {
                    surface.Add(p);
                }
            }
            if (surface.Count == 0)
            {
                return;
            }
            Player anchor = surface[Main.rand.Next(surface.Count)];
            int side = Main.rand.NextBool() ? 1 : -1;
            cardinalSpawnPos = anchor.Center + new Vector2(side * 400f, -Main.rand.NextFloat(120f, 260f));
            VoidPortal.Open(new EntitySource_WorldEvent(), cardinalSpawnPos, anchor.Center - cardinalSpawnPos, 60, 1.25f);
            cardinalSpawnDelay = VoidPortal.OpenTime;
        }

        /// <summary>
        /// 50% 脚本恶灵(§3.1,仅服务端/单人,每场事件一次):进度 ≥50% 且未给过 →
        /// 地表玩家上方 400px 开大型传送门 60t,门张开(40t)后门心生成裂隙恶灵(坠出+咆哮档),
        /// 置位 spawned50Wraith 并同步(存档字段,胜利/清档时重置)。
        /// </summary>
        private static void Update50WraithSpawn()
        {
            if (spawned50Wraith || Progress < 0.5f)
            {
                return;
            }
            if (wraith50Delay > 0)
            {
                wraith50Delay--;
                if (wraith50Delay == 0)
                {
                    int np = NPC.NewNPC(new EntitySource_WorldEvent(), (int)wraith50Pos.X, (int)wraith50Pos.Y, ModContent.NPCType<RiftWraith>());
                    if (np < Main.maxNPCs)
                    {
                        if (Main.npc[np].ModNPC is RiftWraith rw)
                        {
                            rw.spawnSource = 0;
                        }
                        NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, np);
                    }
                    spawned50Wraith = true;
                    SyncEvent();
                }
                return;
            }
            var surface = new List<Player>();
            foreach (Player p in Main.ActivePlayers)
            {
                if (!p.dead && (p.ZoneOverworldHeight || p.ZoneSkyHeight))
                {
                    surface.Add(p);
                }
            }
            if (surface.Count == 0)
            {
                return;
            }
            Player anchor = surface[Main.rand.Next(surface.Count)];
            wraith50Pos = anchor.Center + new Vector2(0, -400f);
            VoidPortal.Open(new EntitySource_WorldEvent(), wraith50Pos, Vector2.UnitY, 60, 1.6f);
            wraith50Delay = VoidPortal.OpenTime;
        }

        /// <summary>
        /// 99% 守门脚本(§1.2/§3.2,仅服务端/单人):进度到 99% 且场上无虚熵魔物 → 生成;
        /// 入场演出(暗脉冲→巨门→部件拼合→咆哮)由魔物自身 AI 驱动,此处只管出生。
        /// 脱战重生等待 10s(OnFiendEscape 置位),锚点取离脱战位置最近的地表玩家;
        /// 首次生成(fiendLastPos 为零)取随机地表玩家。
        /// </summary>
        private static void UpdateFiendSpawn()
        {
            if (Progress < 0.99f || EntropyFiendAlive)
            {
                return;
            }
            if (FiendRespawnDelay > 0)
            {
                FiendRespawnDelay--;
                return;
            }
            var surface = new List<Player>();
            foreach (Player p in Main.ActivePlayers)
            {
                if (!p.dead && (p.ZoneOverworldHeight || p.ZoneSkyHeight))
                {
                    surface.Add(p);
                }
            }
            if (surface.Count == 0)
            {
                return;
            }
            Player anchor;
            if (fiendLastPos == Vector2.Zero)
            {
                anchor = surface[Main.rand.Next(surface.Count)];
            }
            else
            {
                anchor = surface[0];
                foreach (Player p in surface)
                {
                    if (p.Center.Distance(fiendLastPos) < anchor.Center.Distance(fiendLastPos))
                    {
                        anchor = p;
                    }
                }
            }
            Vector2 pos = anchor.Center + new Vector2(0, -420f);
            int np = NPC.NewNPC(new EntitySource_WorldEvent(), (int)pos.X, (int)pos.Y, ModContent.NPCType<EntropyFiend>());
            if (np < Main.maxNPCs)
            {
                NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, np);
            }
        }

        public override void ClearWorld()
        {
            Active = false;
            Progress = 0;
            spawned50Wraith = false;
            StartCountdown = 0;
            prevActive = false;
            CardinalAlive = false;
            CardinalRespawnCooldown = 0;
            cardinalSpawnDelay = 0;
            EntropyFiendAlive = false;
            FiendRespawnDelay = 0;
            fiendLastPos = Vector2.Zero;
            wraith50Delay = 0;
            barFlash = 0;
            lastMilestone = -1;
        }

        public override void SaveWorldData(TagCompound tag)
        {
            if (Active)
                tag["VoidInvasionActive"] = true;
            tag["VoidInvasionProgress"] = Progress;
            if (spawned50Wraith)
                tag["VoidInvasionSpawned50Wraith"] = true;
        }

        public override void LoadWorldData(TagCompound tag)
        {
            Active = tag.ContainsKey("VoidInvasionActive");
            Progress = tag.GetFloat("VoidInvasionProgress");
            spawned50Wraith = tag.ContainsKey("VoidInvasionSpawned50Wraith");
            //读档进入进行中的事件不该再放一次开场音效
            prevActive = Active;
        }

        public override void NetSend(BinaryWriter writer)
        {
            var flags = new BitsByte();
            flags[0] = Active;
            flags[1] = spawned50Wraith;
            writer.Write(flags);
            writer.Write(Progress);
        }

        public override void NetReceive(BinaryReader reader)
        {
            BitsByte flags = reader.ReadByte();
            Active = flags[0];
            spawned50Wraith = flags[1];
            Progress = reader.ReadSingle();
            //中途进服的玩家不该补听一次开场音效
            prevActive = Active;
        }

        public override void PostDrawInterface(SpriteBatch spriteBatch)
        {
            if (!Active || Main.gameMenu || Main.mapFullscreen)
                return;
            DrawProgressBar(spriteBatch);
        }

        //进度条脉光状态(纯本端 UI 视觉):每跨过一个 10% 段落闪一拍;-1 = 未初始化(进图不补闪)
        private static int barFlash = 0;
        private static int lastMilestone = -1;

        /// <summary>
        /// 事件进度条(§1.7):仿原版入侵条的右下角双面板,标题条(图标+事件名)在上,进度条在下。
        /// 底/前景条用 Assets/Extra/Ports/GenericBarBack/Front,前景按进度横向裁剪并染虚空紫。
        /// 存在感:填充段流光扫过 + 条头光点呼吸;每跨过 10% 段落整条脉光一拍。
        /// </summary>
        private static void DrawProgressBar(SpriteBatch sb)
        {
            //PostDrawInterface 的批次处于 UIScale 矩阵下,屏幕尺寸除以 UIScale 才是 UI 空间锚点
            float uiW = Main.screenWidth / Main.UIScale;
            float uiH = Main.screenHeight / Main.UIScale;
            var font = FontAssets.MouseText.Value;
            string title = Language.GetTextValue("Mods.CalamityEntropy.VoidInvasion.Title");
            int percent = (int)(Progress * 100f);
            string percentText = Language.GetTextValue("Mods.CalamityEntropy.VoidInvasion.ProgressPercent", percent);

            //段落脉光:跨过 10% 一闪;进图首帧只对表不闪
            int milestone = (int)(Progress * 10f);
            if (lastMilestone < 0)
                lastMilestone = milestone;
            if (milestone > lastMilestone)
            {
                lastMilestone = milestone;
                barFlash = 36;
            }
            float flashP = barFlash > 0 ? barFlash / 36f : 0f;
            if (barFlash > 0)
                barFlash--;

            //下面板:进度条 + 百分比(向下取整到 1%)
            Vector2 barCenter = new Vector2(uiW - 120, uiH - 40);
            Utils.DrawInvBG(sb, new Rectangle((int)barCenter.X - 100, (int)barCenter.Y - 22, 200, 45), new Color(63, 65, 151) * 0.785f);
            Color textColor = Color.Lerp(Color.White, new Color(255, 230, 160), flashP);
            Utils.DrawBorderString(sb, percentText, new Vector2(barCenter.X, barCenter.Y - 11), textColor, 0.85f, 0.5f, 0.5f);

            Texture2D barBack = CEUtils.getExtraTex("Ports/GenericBarBack");
            Texture2D barFront = CEUtils.getExtraTex("Ports/GenericBarFront");
            Rectangle barRect = new Rectangle((int)barCenter.X - 90, (int)barCenter.Y + 2, 180, 14);
            sb.Draw(barBack, barRect, Color.White);
            int fillWidth = (int)(barRect.Width * Progress);
            if (fillWidth > 0)
            {
                Rectangle src = new Rectangle(0, 0, Math.Max(1, (int)(barFront.Width * Progress)), barFront.Height);
                //底色随脉光提亮
                Color fill = Color.Lerp(new Color(168, 92, 255), new Color(235, 200, 255), flashP);
                Rectangle dest = new Rectangle(barRect.X, barRect.Y, fillWidth, barRect.Height);
                sb.Draw(barFront, dest, src, fill);

                //流光扫带:一段亮白窗口在填充段内往复扫过(4s 周期)
                float sweep = (Main.GlobalTimeWrappedHourly * 0.25f) % 1f;
                int bandW = 26;
                int bandX = (int)(sweep * (fillWidth + bandW)) - bandW;
                int clipX = Math.Max(0, bandX);
                int clipRight = Math.Min(fillWidth, bandX + bandW);
                if (clipRight > clipX)
                {
                    float srcRatioL = clipX / (float)barRect.Width;
                    float srcRatioR = clipRight / (float)barRect.Width;
                    Rectangle bandSrc = new Rectangle((int)(barFront.Width * srcRatioL), 0,
                        Math.Max(1, (int)(barFront.Width * (srcRatioR - srcRatioL))), barFront.Height);
                    Rectangle bandDest = new Rectangle(barRect.X + clipX, barRect.Y, clipRight - clipX, barRect.Height);
                    sb.Draw(barFront, bandDest, bandSrc, Color.White * 0.35f);
                }

                //条头光点:呼吸 + 脉光时增亮(进度"活着"的信号)
                Texture2D tipGlow = CEUtils.getExtraTex("Glow");
                float breathe = 0.55f + 0.2f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 5f);
                float tipScale = (14f + 10f * flashP) / tipGlow.Width * 2.2f;
                sb.Draw(tipGlow, new Vector2(barRect.X + fillWidth, barRect.Y + barRect.Height / 2f), null,
                    new Color(210, 140, 255) * (breathe + 0.4f * flashP), 0, tipGlow.Size() / 2, tipScale, SpriteEffects.None, 0);
            }

            //上面板:事件图标 + 本地化标题(脉光拍标题同步提亮)
            Texture2D icon = CEUtils.RequestTex("CalamityEntropy/Content/UI/VoidInvasion/eventIcon");
            const float iconScale = 0.5f;
            Vector2 iconSize = new Vector2(icon.Width, icon.Height) * iconScale;
            Vector2 titleSize = font.MeasureString(title);
            Vector2 panelSize = new Vector2(iconSize.X + titleSize.X + 26, Math.Max(iconSize.Y, titleSize.Y) + 10);
            Vector2 panelCenter = new Vector2(uiW - 120, uiH - 80);
            Rectangle titleRect = Utils.CenteredRectangle(panelCenter, panelSize);
            Utils.DrawInvBG(sb, titleRect, Color.Lerp(new Color(74, 22, 122), new Color(120, 50, 190), flashP) * 0.785f);
            sb.Draw(icon, new Vector2(titleRect.X + 8, panelCenter.Y - iconSize.Y / 2), null, Color.White, 0, Vector2.Zero, iconScale, SpriteEffects.None, 0);
            Utils.DrawBorderString(sb, title, new Vector2(titleRect.X + iconSize.X + 16, panelCenter.Y), textColor, 1f, 0f, 0.5f);
        }
    }
}
