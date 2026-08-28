using CalamityEntropy.Content.NPCs.AbyssalWraith;
using CalamityEntropy.Content.NPCs.VoidInvasion;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Projectiles
{
    /// <summary>
    /// 召唤仪式法阵(void-invasion.md §1.6):教徒吟唱蓄能,蓄满召唤。
    /// 来源字段 ai[0]:0 = 深渊祭印路径(事件外,召唤深渊亡魂,行为照旧;祭印自带的可靠召唤走
    /// AbyssalSigilRitual 独立链路,不经过本类),1 = 教徒仪式(M5 起改召裂隙恶灵)。
    /// 吟唱池 900 点(15s 基准):每 tick 增量 = 1 × (1 + 0.15 × max(0, 吟唱人数 - 3)) × 光环系数;
    /// 光环系数 = 800px 内有存活术士取 1.25、有存活主教取 1.50,两者取大不叠乘。
    /// 蓄能存 ai[1]:服务端为权威值并周期 netUpdate 校正,客户端跑同一公式仅作视觉镜像;
    /// 召唤判定仅服务端。无人吟唱时仪式中断,直接淡出不召唤(杀教徒即打断,§0.3 反制点)。
    /// </summary>
    public class VoidRitualCircle : ModProjectile
    {
        /// <summary>吟唱池总量(15s 基准,§1.6)</summary>
        public const float ChantPoolMax = 900f;
        /// <summary>光环检索半径(§1.6)</summary>
        public const float AuraRange = 800f;
        /// <summary>召唤完成后全体教徒的再开阵冷却(§1.6:5s,旧值 46s)</summary>
        public const int SummonCooldown = 5 * 60;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Projectile.type] = 1;
        }

        public float alpha = 0;
        /// <summary>true = 已收尾(召唤完成或吟唱中断),进入淡出</summary>
        public bool summoned = false;
        /// <summary>来源(§1.6):0 = 深渊祭印路径,1 = 教徒仪式</summary>
        public int Source => (int)Projectile.ai[0];
        /// <summary>已积累吟唱点(0~900)。存 ai 槽借 netUpdate 原生同步,服务端为权威值</summary>
        public ref float ChantPoints => ref Projectile.ai[1];

        public override void SetDefaults()
        {
            Projectile.width = 64;
            Projectile.height = 64;
            Projectile.friendly = false;
            Projectile.hostile = false;
            Projectile.tileCollide = false;
            Projectile.light = 0f;
            Projectile.timeLeft = 15000;
            Projectile.penetrate = -1;
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write(summoned);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            summoned = reader.ReadBoolean();
        }

        /// <summary>光环系数(§1.6):800px 内有存活术士 1.25、有存活主教 1.50,取大不叠乘。</summary>
        private float AuraFactor()
        {
            float factor = 1f;
            foreach (NPC n in Main.npc)
            {
                if (!n.active || n.Center.Distance(Projectile.Center) > AuraRange)
                    continue;
                if (n.ModNPC is VoidCardinal)
                    return 1.5f; //主教档即上限,可直接短路
                if (n.ModNPC is VoidCultistWarlock)
                    factor = 1.25f;
            }
            return factor;
        }

        public override void AI()
        {
            Projectile.light = alpha * 6;
            if (!summoned)
            {
                int chanters = 0;
                foreach (NPC n in Main.npc)
                {
                    if (n.active && n.ModNPC is VoidCultist vc && vc.aiStyle == VoidCultist.AIStyle.Summoning)
                        chanters++;
                }
                if (chanters == 0)
                {
                    //无人吟唱:仪式中断,不召唤直接淡出(旧语义保留)
                    summoned = true;
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                        Projectile.netUpdate = true;
                }
                else
                {
                    //900 点池(§1.6):基准 1 点/t,人数超 3 每人 +15%,光环系数取大;
                    //双端同跑公式,客户端镜像由服务端每 60t 的 netUpdate 校正
                    ChantPoints += 1f * (1f + 0.15f * Math.Max(0, chanters - 3)) * AuraFactor();
                    //视觉进度 = 已积累/900 直接映射(§1.6)
                    alpha = Math.Min(1f, ChantPoints / ChantPoolMax);
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        if (Main.GameUpdateCount % 60 == 0)
                            Projectile.netUpdate = true;
                        if (ChantPoints >= ChantPoolMax)
                            CompleteSummon();
                    }
                }
            }
            else
            {
                alpha -= 0.025f;
                if (alpha <= 0)
                    Projectile.Kill();
            }
        }

        /// <summary>
        /// 召唤完成(仅服务端/单人):按来源出怪,并给全体教徒上 5s 再开阵冷却(§1.6)。
        /// </summary>
        private void CompleteSummon()
        {
            summoned = true;
            Projectile.netUpdate = true;
            int type = ModContent.NPCType<AbyssalWraith>();
            if (Source == 1)
            {
                //教徒仪式(§1.6/§3.1):事件档召唤裂隙恶灵,阵心上浮入场
                type = ModContent.NPCType<RiftWraith>();
            }
            int np = NPC.NewNPC(new EntitySource_WorldEvent(), (int)Projectile.Center.X, (int)Projectile.Center.Y + 42, type);
            if (np < Main.maxNPCs)
            {
                if (Main.npc[np].ModNPC is RiftWraith rw)
                {
                    rw.spawnSource = 1;
                }
                NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, np);
            }
            foreach (NPC n in Main.npc)
            {
                if (n.active && n.ModNPC is VoidCultist vc)
                {
                    vc.noSummon = SummonCooldown;
                    n.netUpdate = true;
                }
            }
        }

        public override void DrawBehind(int index, List<int> behindNPCsAndTiles, List<int> behindNPCs, List<int> behindProjectiles, List<int> overPlayers, List<int> overWiresUI)
        {
            overPlayers.Add(index);
        }

        public override bool ShouldUpdatePosition()
        {
            return false;
        }
        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            return false;
        }
        float rotCount = 0;

        public override bool PreDraw(ref Color lightColor)
        {
            rotCount += 0.16f;
            Texture2D tex = TextureAssets.Projectile[Projectile.type].Value;
            Main.spriteBatch.Draw(tex, Projectile.Center - Main.screenPosition, null, Color.White, rotCount, tex.Size() / 2, Projectile.scale * 2 * alpha, SpriteEffects.None, 0);
            return false;
        }
    }


}