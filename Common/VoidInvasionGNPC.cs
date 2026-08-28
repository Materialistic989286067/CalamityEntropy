using CalamityEntropy.Content.Events;
using CalamityEntropy.Content.Items;
using CalamityEntropy.Content.Items.Weapons.VoidInvasion;
using CalamityEntropy.Content.NPCs.VoidInvasion;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Common
{
    /// <summary>
    /// 虚空入侵单位标记接口:不继承 VoidCultist 的事件怪统一挂它,计入清场、术士辅助目标等
    /// 事件家族判定。M3 起蠕虫的体节尾也挂(保证清场覆盖);进度结算按 OnKill 的类型表走。
    /// </summary>
    public interface IVoidInvasionNPC { }

    /// <summary>
    /// 虚空入侵刷怪控制(void-invasion.md §1.4,仓库首个 EditSpawnPool 用例):
    /// 事件激活且玩家在地表时压刷怪间隔、清空原池换事件怪;击杀进度在服务端结算(§1.3);
    /// 事件未激活时渐隐清场残留事件怪。
    /// </summary>
    public class VoidInvasionGNPC : GlobalNPC
    {
        //§1.3 击杀进度表:骚扰层 0.3% / 主力层 0.8% / 精英层 2% / 主教 5% / 恶灵 12%
        public const float ProgressHarass = 0.003f;    //教徒(M2 起:术士、烛灵同档)
        public const float ProgressMainline = 0.008f;  //M2/M3:护教骑士、虚空爬行者
        public const float ProgressElite = 0.02f;      //M3:魔像、掠食者、噬虚鲨、混沌嵌合体
        public const float ProgressCardinal = 0.05f;   //M4:红衣主教
        public const float ProgressRiftWraith = 0.12f; //M5:裂隙恶灵

        /// <summary>事件接管刷怪的条件:激活中且该玩家在地表(§1.4)。地下照常刷原怪。</summary>
        private static bool EventTakesOver(Player player)
        {
            return VoidInvasion.Active && (player.ZoneOverworldHeight || player.ZoneSkyHeight);
        }

        /// <summary>虚空入侵家族 NPC 判定(教皇传颂之物受伤减免用,§5.3):事件怪 + 教皇及其演出体。</summary>
        public static bool IsVoidFamily(NPC npc)
        {
            return npc.ModNPC is VoidCultist or IVoidInvasionNPC or VoidPope or VoidPopeHand or VoidWormlet or LightningOrb;
        }

        /// <summary>虚空入侵家族弹幕判定(§5.3):按命名空间归族(事件弹幕 + 恶灵沿用的亡魂弹幕),只认敌对弹。</summary>
        public static bool IsVoidFamilyProjectile(Projectile proj)
        {
            if (!proj.hostile || proj.ModProjectile == null)
            {
                return false;
            }
            string ns = proj.ModProjectile.GetType().Namespace ?? string.Empty;
            return ns.EndsWith(".Projectiles.VoidInvasion") || ns.EndsWith(".Projectiles.AbyssalWraithProjs");
        }

        public override void EditSpawnRate(Player player, ref int spawnRate, ref int maxSpawns)
        {
            if (!EventTakesOver(player))
                return;
            spawnRate = (int)(spawnRate * 0.2f);
            maxSpawns = 14;
        }

        public override void EditSpawnPool(IDictionary<int, float> pool, NPCSpawnInfo spawnInfo)
        {
            if (!EventTakesOver(spawnInfo.Player))
                return;
            pool.Clear();
            //§1.2/§1.4:进度到 99% 起停止一切事件刷怪,虚熵魔物在场时池保持清空(守门决斗)
            if (VoidInvasion.Progress >= 0.99f || VoidInvasion.EntropyFiendAlive)
                return;
            //§1.4 权重表:0~50% 档 / 50%+ 档(后半场精英上调)。未实现单位留注释行,后续里程碑解注即可
            bool late = VoidInvasion.Progress >= 0.5f;
            pool[ModContent.NPCType<VoidCultistAssassin>()] = late ? 18 : 24;
            pool[ModContent.NPCType<VoidCultistWarlock>()] = late ? 14 : 16;
            pool[ModContent.NPCType<VoidCandleWisp>()] = late ? 12 : 15;
            pool[ModContent.NPCType<VoidTemplar>()] = late ? 12 : 10;
            pool[ModContent.NPCType<VoidCrawlerHead>()] = late ? 12 : 10;
            pool[ModContent.NPCType<VoidGolem>()] = late ? 9 : 7;
            pool[ModContent.NPCType<VoidPredatorHead>()] = late ? 8 : 6;
            pool[ModContent.NPCType<VoidmawShark>()] = late ? 8 : 6;
            pool[ModContent.NPCType<ChaosChimera>()] = late ? 7 : 6;
            //红衣主教/裂隙恶灵/虚熵魔物为脚本生成,不进池(§1.4)
        }

        /// <summary>
        /// 教皇纪念章进度乘区(§5.3):最后交互玩家佩戴纪念章时该次击杀进度 ×1.1。
        /// 饰品旗标在各端的 UpdateAccessory 里逐帧刷新,服务端可直接读。
        /// </summary>
        private static float KillProgressMult(NPC npc)
        {
            int who = npc.lastInteraction;
            if (who >= 0 && who < Main.maxPlayers && Main.player[who].active && Main.player[who].Entropy().popeMedal)
            {
                return Content.Items.VoidInvasion.PopeMedal.ProgressMult;
            }
            return 1f;
        }

        public override void OnKill(NPC npc)
        {
            //OnKill 只在服务端/单人触发,进度天然服务端结算(§1.3)
            if (!VoidInvasion.Active)
                return;
            float mult = KillProgressMult(npc);
            if (npc.ModNPC is VoidCultist)
            {
                //骚扰层:教徒与术士(继承 VoidCultist 自动落进本档)
                VoidInvasion.AddProgress(ProgressHarass * mult);
            }
            else if (npc.ModNPC is VoidCandleWisp)
            {
                VoidInvasion.AddProgress(ProgressHarass * mult);
            }
            else if (npc.ModNPC is VoidTemplar)
            {
                VoidInvasion.AddProgress(ProgressMainline * mult);
            }
            else if (npc.ModNPC is VoidCrawlerHead)
            {
                //蠕虫整条只在头死亡结算一次(体节尾不在结算表里)
                VoidInvasion.AddProgress(ProgressMainline * mult);
            }
            else if (npc.ModNPC is VoidGolem or VoidPredatorHead or VoidmawShark or ChaosChimera)
            {
                VoidInvasion.AddProgress(ProgressElite * mult);
            }
            else if (npc.ModNPC is VoidCardinal)
            {
                //主教 +5%(§1.3);60s 重生冷却在 VoidCardinal.OnKill 里记
                VoidInvasion.AddProgress(ProgressCardinal * mult);
            }
            else if (npc.ModNPC is RiftWraith)
            {
                //裂隙恶灵 +12%(§1.3 小 Boss 档;仪式可量产,同屏 ≤3 是护栏)
                VoidInvasion.AddProgress(ProgressRiftWraith * mult);
            }
            //虚熵魔物不走进度表:真死直接 SetVictory(99%→100%,EntropyFiend.OnKill);
            //嵌合体吞怪走 active=false 不进本钩子
        }

        /// <summary>
        /// 事件材料与武器掉落中央挂点(§5.1/§5.4,M9):
        /// 精英五怪魂髓 1~2 @40%;魔像 4% 超绝虚空铁拳;掠食者头 5% 召唤杖。
        /// 裂隙恶灵/虚熵魔物的掉落在各自 NPC 文件的 ModifyNPCLoot 里(它们的 TODO 位)。
        /// </summary>
        public override void ModifyNPCLoot(NPC npc, NPCLoot npcLoot)
        {
            bool elite = npc.type == ModContent.NPCType<VoidGolem>()
                || npc.type == ModContent.NPCType<VoidPredatorHead>()
                || npc.type == ModContent.NPCType<VoidmawShark>()
                || npc.type == ModContent.NPCType<ChaosChimera>()
                || npc.type == ModContent.NPCType<VoidCardinal>();
            if (elite)
            {
                //40% 掉 1~2(CommonDrop 分数写法:分母 5 分子 2)
                npcLoot.Add(new CommonDrop(ModContent.ItemType<WraithSoulEssence>(), 5, 1, 2, 2));
            }
            if (npc.type == ModContent.NPCType<VoidGolem>())
            {
                npcLoot.Add(ItemDropRule.Common(ModContent.ItemType<SupremeVoidFist>(), 25));
            }
            if (npc.type == ModContent.NPCType<VoidPredatorHead>())
            {
                npcLoot.Add(ItemDropRule.Common(ModContent.ItemType<VoidPredatorStaff>(), 20));
            }
        }

        public override void PostAI(NPC npc)
        {
            if (VoidInvasion.Active)
                return;
            //事件未激活时清场(§1.4):事件怪渐隐后移除。
            //VoidCultist.CheckActive 恒 false,EncourageDespawn 对它无效,只能走透明度递减+主动 active=false
            if (npc.ModNPC is VoidCultist cultist)
            {
                npc.dontTakeDamage = true;
                cultist.drawAlpha -= 1f / 45f;
                if (cultist.drawAlpha <= 0 && Main.netMode != NetmodeID.MultiplayerClient)
                {
                    npc.active = false;
                    if (Main.netMode == NetmodeID.Server)
                        NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, npc.whoAmI);
                }
            }
            else if (npc.ModNPC is IVoidInvasionNPC)
            {
                //非教徒系事件怪(烛灵/骑士等):CheckActive 恒 false,原生脱战无效,
                //走 npc.alpha 渐隐(原版绘制吃 GetAlpha,自绘单位在 PreDraw 乘 npc.Opacity)
                npc.dontTakeDamage = true;
                npc.alpha = Math.Min(255, npc.alpha + 6);
                if (npc.alpha >= 255 && Main.netMode != NetmodeID.MultiplayerClient)
                {
                    npc.active = false;
                    if (Main.netMode == NetmodeID.Server)
                        NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, npc.whoAmI);
                }
            }
        }
    }
}
