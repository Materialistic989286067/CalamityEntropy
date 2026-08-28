using CalamityEntropy.Common;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Projectiles.AbyssalWraithProjs;
using CalamityEntropy.Content.Projectiles.VoidInvasion;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.NPCs.VoidInvasion
{
    /// <summary>
    /// 红衣主教(void-invasion.md §2.3):场上限 1 的战场指挥官,悬浮于玩家侧上方 500~700px。
    /// 在场时全场事件怪(不含自身)受击 ×0.75(在场缓存 VoidInvasion.CardinalAlive,结算在 EGlobalNPC.ModifyIncomingHit);
    /// 对召唤仪式提供 ×1.5 提速光环(VoidRitualCircle 侧查表,本类不写)。
    /// 招式:法球连射(40t 前摇 → 3 波 ×5 扇形 VoidLightBall → 收 30t)与虚空闪电(VoidLightningBolt,
    /// 警示线 30t → 沿线放电 2 次)轮转;每 25s 三位分身 12s(±120° 纯绘制体,分身期三位齐射法球,
    /// 位置由本体与目标坐标确定性推导,不新建 NPC 不同步坐标);每 15s 传送门投放一只事件怪。
    /// 脚本生成与 60s 死亡冷却见 VoidInvasion.UpdateCardinalSpawn。
    /// 贴图:竖排 4 帧单图(128x512,npcFrameCount=4 原生动画),悬浮 8t/帧;判定框 60x90。
    /// </summary>
    public class VoidCardinal : ModNPC, IVoidInvasionNPC
    {
        public enum CardinalAttack : byte
        {
            Hover,      //招间呼吸拍漂移
            OrbBarrage, //法球连射
            Lightning,  //虚空闪电
        }

        //节拍常量(§2.3)
        private const int BarrageWindup = 40;
        private const int BarrageWaveGap = 30;
        private const int BarrageRecover = 30;
        private const int LightningWarn = VoidLightningBolt.WarnTime;
        private const int LightningRecover = 40;
        private const int HoverPause = 60;          //招间呼吸拍(§0.3 支柱 2)
        private const int CloneInterval = 25 * 60;  //分身周期(§2.3:每 25s)
        private const int CloneDuration = 12 * 60;  //分身持续(§2.3:12s)
        private const int PortalInterval = 15 * 60; //投放周期(§2.3:每 15s)
        private const float PortalCrowdRange = 700f;
        private const int PortalCrowdLimit = 8;

        public byte attackID = (byte)CardinalAttack.Hover;
        public int attackTimer = 0;
        /// <summary>分身周期计时:达到 CloneInterval 进入 12s 分身窗口,窗口结束归零</summary>
        public int cloneTimer = 0;
        public int portalCD = PortalInterval;
        //传送门投放的待出怪状态(仅服务端读写,选型与出怪都是服务端事,§2.3)
        private int pendingDropTimer = 0;
        private Vector2 pendingDropPos;
        private int pendingDropType = 0;
        //招式轮转指针(仅服务端选招时读,结果经 attackID 同步)
        private bool lightningNext = false;
        //悬浮侧向黏滞与入场渐显(双端各自推的视觉级状态,不同步)
        private int hoverSide = 1;
        public float drawAlpha = 0;

        /// <summary>分身窗口是否激活(cloneTimer 进 SendExtraAI,双端一致)</summary>
        public bool ClonesActive => cloneTimer >= CloneInterval;

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = 4;
            NPCID.Sets.MPAllowedEnemies[Type] = true;
        }

        public override void SetBestiary(Terraria.GameContent.Bestiary.BestiaryDatabase database, Terraria.GameContent.Bestiary.BestiaryEntry bestiaryEntry)
        {
            bestiaryEntry.Info.AddRange(new Terraria.GameContent.Bestiary.IBestiaryInfoElement[]
            {
                new Terraria.GameContent.Bestiary.FlavorTextBestiaryInfoElement("Mods.CalamityEntropy.VoidCardinalBestiary")
            });
        }

        public override void SetDefaults()
        {
            NPC.width = 60;
            NPC.height = 90;
            NPC.damage = 140;
            if (Main.expertMode)
            {
                NPC.damage += 20;
            }
            if (Main.masterMode)
            {
                NPC.damage += 20;
            }
            NPC.lifeMax = 600000;
            NPC.defense = 90;
            NPC.knockBackResist = 0f;
            NPC.aiStyle = -1;
            NPC.noGravity = true;
            NPC.noTileCollide = true;
            NPC.HitSound = SoundID.NPCHit1;
            NPC.DeathSound = SoundID.NPCDeath6;
            NPC.value = Item.buyPrice(0, 5, 0, 0);
            NPC.Entropy().VoidTouchDR = 0.7f;
        }

        public override bool CheckActive()
        {
            return false;
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write(attackID);
            writer.Write(attackTimer);
            writer.Write(cloneTimer);
            writer.Write(portalCD);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            attackID = reader.ReadByte();
            attackTimer = reader.ReadInt32();
            cloneTimer = reader.ReadInt32();
            portalCD = reader.ReadInt32();
        }

        public override void OnKill()
        {
            //死亡记时(§2.3):60s 后才允许刷下一只(OnKill 仅服务端/单人触发);进度 +5% 在 VoidInvasionGNPC.OnKill。
            //注:本文件所在命名空间与事件类同名,须经 Content.Events 限定
            Content.Events.VoidInvasion.CardinalRespawnCooldown = 60 * 60;
        }

        private void SwitchAttack(CardinalAttack next)
        {
            attackID = (byte)next;
            attackTimer = 0;
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                NPC.netUpdate = true;
            }
        }

        public override void AI()
        {
            //入场拍每端各自一次:紫闪 + 雷声(§1.7/§2.3)
            if (NPC.localAI[0] == 0)
            {
                NPC.localAI[0] = 1;
                SpawnFlash();
            }
            drawAlpha = Math.Min(1f, drawAlpha + 1f / 30f);
            NPC.ai[0]++; //全局拍:悬浮呼吸用

            if (!NPC.HasValidTarget)
            {
                NPC.TargetClosest(false);
                if (!NPC.HasValidTarget)
                {
                    NPC.velocity *= 0.96f;
                    return;
                }
            }
            Player target = Main.player[NPC.target];
            NPC.direction = NPC.spriteDirection = target.Center.X > NPC.Center.X ? 1 : -1;

            HoverMovement(target);
            UpdateCloneWindow();
            attackTimer++;

            switch ((CardinalAttack)attackID)
            {
                case CardinalAttack.Hover: HoverAI(); break;
                case CardinalAttack.OrbBarrage: OrbBarrageAI(target); break;
                case CardinalAttack.Lightning: LightningAI(target); break;
            }

            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                UpdatePortalDrop(target);
            }
        }

        /// <summary>悬浮基线(§2.3):玩家侧上方缓漂,总距在 500~700px 摆动(纵向 sin 呼吸)。</summary>
        private void HoverMovement(Player target)
        {
            float dx = NPC.Center.X - target.Center.X;
            if (Math.Abs(dx) > 60f)
            {
                //侧向黏滞:贴近正上方时保持原侧,防抖
                hoverSide = dx >= 0 ? 1 : -1;
            }
            float bob = (float)Math.Sin(NPC.ai[0] * 0.02f) * 80f;
            Vector2 want = target.Center + new Vector2(hoverSide * 330f, -510f + bob);
            Vector2 drift = (want - NPC.Center) * 0.03f;
            if (drift.Length() > 11f)
            {
                drift = drift.SafeNormalize(Vector2.Zero) * 11f;
            }
            NPC.velocity = Vector2.Lerp(NPC.velocity, drift, 0.07f);
        }

        /// <summary>分身窗口推进(§2.3):每 25s 一次持续 12s;窗口内头顶魂火冠标记(可读性阀门,纯客户端)。</summary>
        private void UpdateCloneWindow()
        {
            cloneTimer++;
            if (cloneTimer >= CloneInterval + CloneDuration)
            {
                cloneTimer = 0;
            }
            if (ClonesActive && !Main.dedServ && Main.rand.NextBool(3))
            {
                var p = PRTLoader.NewParticle<PRT_Light>(NPC.Top + new Vector2(Main.rand.NextFloat(-10f, 10f), -14f),
                    new Vector2(0, -Main.rand.NextFloat(0.6f, 1.4f)),
                    Main.rand.NextBool() ? new Color(200, 120, 255) : new Color(255, 230, 160), 0.5f);
                p.Configure(0.85f, lifetime: 22);
            }
        }

        /// <summary>呼吸拍:漂移 60t 后服务端选下一招。分身期锁定法球连射(窗口主题演出是三位齐射,§2.3)。</summary>
        private void HoverAI()
        {
            if (attackTimer < HoverPause || Main.netMode == NetmodeID.MultiplayerClient)
            {
                return;
            }
            if (ClonesActive || !lightningNext)
            {
                SwitchAttack(CardinalAttack.OrbBarrage);
                lightningNext = true;
            }
            else
            {
                SwitchAttack(CardinalAttack.Lightning);
                lightningNext = false;
            }
        }

        /// <summary>法球连射(§2.3):双手举起 40t 前摇(周身微光)→ 3 波 ×5 扇形法球(波间隔 30t)→ 收 30t。</summary>
        private void OrbBarrageAI(Player target)
        {
            if (attackTimer <= BarrageWindup)
            {
                ChantGlow();
                return;
            }
            int t = attackTimer - BarrageWindup;
            if (t % BarrageWaveGap == 1 && t <= BarrageWaveGap * 2 + 1)
            {
                //波拍 t = 1 / 31 / 61,共 3 波
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    FireOrbWave(target);
                }
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.Item72, NPC.Center);
                }
            }
            if (attackTimer >= BarrageWindup + BarrageWaveGap * 2 + BarrageRecover && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchAttack(CardinalAttack.Hover);
            }
        }

        /// <summary>
        /// 一波扇形法球:本体一组,分身期从两个推导位各补一组(§2.3 分身期三位齐射,弹幕全部服务端生成)。
        /// VoidLightBall 约定镜像深渊亡魂:15px/t 甩出减速,蓄能 60t 后沿自身朝向放光束,ai2 = 本体(蓄能期转向本体目标);
        /// 150 经典档 = 敌对弹幕命中 ×2,故弹幕伤害取 defDamage(140)×0.536≈75。
        /// </summary>
        private void FireOrbWave(Player target)
        {
            int damage = (int)(NPC.defDamage * 0.536f);
            foreach (Vector2 origin in FiringPositions(target))
            {
                Vector2 baseDir = (target.Center - origin).SafeNormalize(Vector2.UnitX * NPC.direction);
                for (int i = -2; i <= 2; i++)
                {
                    Projectile.NewProjectile(NPC.GetSource_FromAI(), origin, baseDir.RotatedBy(i * 0.26f) * 15f,
                        ModContent.ProjectileType<VoidLightBall>(), damage, 6f, -1, 0, 0, NPC.whoAmI);
                }
            }
        }

        /// <summary>齐射位置:本体 + 分身期两个推导位(本体相对目标的偏移旋转 ±120°,确定性,双端同式)。</summary>
        private List<Vector2> FiringPositions(Player target)
        {
            var list = new List<Vector2> { NPC.Center };
            if (ClonesActive)
            {
                Vector2 rel = NPC.Center - target.Center;
                list.Add(target.Center + rel.RotatedBy(MathHelper.TwoPi / 3f));
                list.Add(target.Center + rel.RotatedBy(-MathHelper.TwoPi / 3f));
            }
            return list;
        }

        /// <summary>
        /// 虚空闪电(§2.3):锁定玩家当前位,VoidLightningBolt 自带警示线 30t 与沿线放电 2 次;
        /// 线段几何随弹幕实体原生同步(位置 + 借初速度通道的朝向),NPC 侧无需追加同步字段。
        /// 170 经典档 = 敌对弹幕命中 ×2,故弹幕伤害取 defDamage(140)×0.61≈85。
        /// </summary>
        private void LightningAI(Player target)
        {
            if (attackTimer == 1 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                int damage = (int)(NPC.defDamage * 0.61f);
                Vector2 dir = (target.Center - NPC.Center).SafeNormalize(Vector2.UnitY);
                Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, dir * 0.02f,
                    ModContent.ProjectileType<VoidLightningBolt>(), damage, 3f, -1);
            }
            if (attackTimer <= LightningWarn)
            {
                ChantGlow();
            }
            if (attackTimer >= LightningWarn + VoidLightningBolt.DischargeDuration + LightningRecover
                && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchAttack(CardinalAttack.Hover);
            }
        }

        /// <summary>
        /// 传送门投放(§2.3,仅服务端):每 15s 且 700px 内事件怪 <8 → 玩家旁 400px 开 60t 门,
        /// 门张开(40t)后门心投放一只:掠食者 30% / 噬虚鲨 30% / 护教骑士 25% / 冲撞态魔像 15%。
        /// </summary>
        private void UpdatePortalDrop(Player target)
        {
            //虚熵魔物在场/99% 后投放停摆(§1.2/§1.4 守门决斗),挂起的投放一并作废;镜像 CardinalAlive 的缓存判定
            if (Content.Events.VoidInvasion.EntropyFiendAlive || Content.Events.VoidInvasion.Progress >= 0.99f)
            {
                pendingDropTimer = 0;
                return;
            }
            if (pendingDropTimer > 0)
            {
                pendingDropTimer--;
                if (pendingDropTimer == 0)
                {
                    //魔像走生成时 ai[3]=1 约定(VoidGolem.OnSpawn):出场即冲撞狂奔
                    float ai3 = pendingDropType == ModContent.NPCType<VoidGolem>() ? 1f : 0f;
                    int np = NPC.NewNPC(NPC.GetSource_FromAI(), (int)pendingDropPos.X, (int)pendingDropPos.Y, pendingDropType, 0, 0, 0, 0, ai3);
                    if (np < Main.maxNPCs)
                    {
                        NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, np);
                    }
                }
                return;
            }
            if (portalCD > 0)
            {
                portalCD--;
                return;
            }
            if (CountEventNPCsAround() >= PortalCrowdLimit)
            {
                return;
            }
            int side = Main.rand.NextBool() ? 1 : -1;
            pendingDropPos = target.Center + new Vector2(side * 400f, -Main.rand.NextFloat(60f, 180f));
            pendingDropType = Main.rand.Next(100) switch
            {
                < 30 => ModContent.NPCType<VoidPredatorHead>(),
                < 60 => ModContent.NPCType<VoidmawShark>(),
                < 85 => ModContent.NPCType<VoidTemplar>(),
                _ => ModContent.NPCType<VoidGolem>(),
            };
            VoidPortal.Open(NPC.GetSource_FromAI(), pendingDropPos, target.Center - pendingDropPos, 60, 1.15f);
            pendingDropTimer = VoidPortal.OpenTime;
            portalCD = PortalInterval;
        }

        private int CountEventNPCsAround()
        {
            int count = 0;
            foreach (NPC n in Main.npc)
            {
                if (n.active && n.whoAmI != NPC.whoAmI && (n.ModNPC is IVoidInvasionNPC || n.ModNPC is VoidCultist)
                    && n.Center.Distance(NPC.Center) < PortalCrowdRange)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>前摇周身微光:外圈光点向体心汇聚(纯客户端)。</summary>
        private void ChantGlow()
        {
            if (Main.dedServ || !Main.rand.NextBool(2))
            {
                return;
            }
            Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(50f, 90f);
            var p = PRTLoader.NewParticle<PRT_Light>(NPC.Center + offset, -offset * 0.07f, new Color(190, 110, 255), 0.5f);
            p.Configure(0.8f, lifetime: 18);
        }

        /// <summary>入场演出(§1.7 量力档):紫闪用 PRT 大光斑 + 原版雷声,外加 PRT_Void 爆散。</summary>
        private void SpawnFlash()
        {
            if (Main.dedServ)
            {
                return;
            }
            SoundEngine.PlaySound(SoundID.Thunder, NPC.Center);
            for (int i = 0; i < 3; i++)
            {
                var p = PRTLoader.NewParticle<PRT_Light>(NPC.Center, Vector2.Zero, new Color(170, 90, 255), 3.5f - i);
                p.Configure(0.9f, lifetime: 26 - i * 4);
            }
            for (int i = 0; i < 40; i++)
            {
                var v = PRTLoader.NewParticle<PRT_Void>(NPC.Center,
                    CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(1f, 6f), Color.White, 1f);
                v.Opacity = Main.rand.Next(30, 100) * 0.01f;
            }
        }

        public override void FindFrame(int frameHeight)
        {
            //悬浮 4 帧循环 8t/帧(§2.3)
            NPC.frameCounter++;
            if (NPC.frameCounter >= 8)
            {
                NPC.frameCounter = 0;
                NPC.frame.Y += frameHeight;
                if (NPC.frame.Y >= frameHeight * Main.npcFrameCount[Type])
                {
                    NPC.frame.Y = 0;
                }
            }
        }

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (NPC.IsABestiaryIconDummy)
            {
                return true;
            }
            Texture2D tex = TextureAssets.Npc[Type].Value;
            Rectangle frame = NPC.frame;
            if (frame.Height <= 0)
            {
                frame = new Rectangle(0, 0, tex.Width, tex.Height / Main.npcFrameCount[Type]);
            }
            SpriteEffects fx = NPC.spriteDirection > 0 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            Vector2 origin = frame.Size() / 2f;
            float alpha = drawAlpha * NPC.Opacity;

            //三位分身(§2.3):±120° 纯绘制体,0.8 透明度,仅本体可被命中;窗口首尾 20t 渐显/渐隐
            if (ClonesActive && NPC.HasValidTarget)
            {
                int wt = cloneTimer - CloneInterval;
                float ramp = Math.Min(wt, CloneInterval + CloneDuration - cloneTimer) / 20f;
                float cloneAlpha = 0.8f * MathHelper.Clamp(ramp, 0f, 1f);
                Vector2 rel = NPC.Center - Main.player[NPC.target].Center;
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector2 pos = Main.player[NPC.target].Center + rel.RotatedBy(s * MathHelper.TwoPi / 3f);
                    spriteBatch.Draw(tex, pos - screenPos, frame, drawColor * (alpha * cloneAlpha), NPC.rotation, origin, NPC.scale, fx, 0);
                }
            }
            spriteBatch.Draw(tex, NPC.Center - screenPos, frame, drawColor * alpha, NPC.rotation, origin, NPC.scale, fx, 0);
            return false;
        }

        public override void HitEffect(NPC.HitInfo hit)
        {
            if (NPC.life > 0 || Main.dedServ)
            {
                return;
            }
            for (int i = 0; i < 64; i++)
            {
                var p = PRTLoader.NewParticle<PRT_Void>(NPC.Center,
                    CEUtils.randomRot().ToRotationVector2() * Main.rand.Next(0, 400) * 0.01f, Color.White, 1f);
                p.Opacity = Main.rand.Next(20, 100) * 0.01f;
            }
        }
    }
}
