using CalamityEntropy.Common;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using CalamityEntropy.Content.Projectiles.VoidInvasion;
using InnoVault;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.IO;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.NPCs.VoidInvasion
{
    /// <summary>
    /// 混沌嵌合体(void-invasion.md §2.10,显示名"混沌嵌合体",美术文件夹实名"混沌融合体"):
    /// 精英层战场变量。吞噬被动:骚扰层事件怪进腹部 120px → 服务端直接 active=false + SyncNPC
    /// (不走 kill 流程,无掉落无进度)→ 回 2.5% 最大生命 + 双爪合拢演出。
    /// 三主动:双爪连击(近身 2~3 段弧形判定)、插地爆发(每 10s,5 根地刺依次 + 裂纹预警)、
    /// 远距扑击(>600px 伏身 → 16px/t 0.7s → 硬直)。
    /// 绘制:PreDraw 程序化组装(镜像 AcropolisMachine 肢体写法),锚点常量表见类头。
    /// </summary>
    public class ChaosChimera : ModNPC, IVoidInvasionNPC
    {
        public override string Texture => "CalamityEntropy/Content/NPCs/VoidInvasion/Chimera/body";

        //部件贴图只在绘制路径读取(服务器恒 null)
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Chimera/body")]
        private static Asset<Texture2D> bodyTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Chimera/tentacle")]
        private static Asset<Texture2D> tentacleTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Chimera/clawL")]
        private static Asset<Texture2D> clawLTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Chimera/clawR")]
        private static Asset<Texture2D> clawRTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Chimera/armL")]
        private static Asset<Texture2D> armLTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Chimera/armR")]
        private static Asset<Texture2D> armRTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Chimera/belly")]
        private static Asset<Texture2D> bellyTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Chimera/legL", 1, 3, AssetMode = AssetMode.TextureValueArray)]
        private static Texture2D[] legLTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Chimera/legR", 1, 3, AssetMode = AssetMode.TextureValueArray)]
        private static Texture2D[] legRTex;

        // ---- 锚点常量表(§2.10,美术像素坐标,进游戏逐项校) ----
        /// <summary>躯干 400x400 画布,实心约 370x322,整体绘制缩放(世界实占约 200x200)</summary>
        private const float DrawScale = 0.55f;
        /// <summary>躯干画布中心相对判定框中心的偏移(世界 px)</summary>
        private static readonly Vector2 BodyOffset = new Vector2(0, -20);
        /// <summary>以下均为躯干画布中心系的美术像素偏移,绘制时 ×DrawScale</summary>
        private static readonly Vector2 TentacleMount = new Vector2(0, -115);
        private static readonly Vector2 ShoulderL = new Vector2(-130, -75);
        private static readonly Vector2 ShoulderR = new Vector2(130, -75);
        private static readonly Vector2 SmallArmL = new Vector2(-55, -35);
        private static readonly Vector2 SmallArmR = new Vector2(55, -35);
        private static readonly Vector2 BellyMount = new Vector2(0, 85);
        private static readonly Vector2[] LegLMounts = { new Vector2(-62, 112), new Vector2(-30, 126), new Vector2(-2, 118) };
        private static readonly Vector2[] LegRMounts = { new Vector2(62, 112), new Vector2(30, 126), new Vector2(2, 118) };
        /// <summary>部件自身的旋转根(美术像素):触角根在底心,大爪根在内下角,小臂根在顶部,腿根在上缘</summary>
        private static readonly Vector2 TentacleOrigin = new Vector2(98, 95);
        private static readonly Vector2 ClawLOrigin = new Vector2(52, 102);
        private static readonly Vector2 ClawROrigin = new Vector2(208, 102);
        private static readonly Vector2 ArmLOrigin = new Vector2(90, 12);
        private static readonly Vector2 ArmROrigin = new Vector2(50, 12);

        //数值与节拍常量(§2.10)
        private const float WalkSpeed = 1.8f;
        private const float DevourRange = 120f;
        private const float DevourHealFrac = 0.025f;
        private const int ClawWindupTime = 20;
        private const int ClawSwipeTime = 8;
        private const int ClawGapTime = 25;
        private const float ClawTriggerRange = 200f;
        private const int SpikeInterval = 600;
        private const int SpikeWindupTime = 30;
        private const int SpikeRecoverTime = 20;
        private const float PounceTriggerRange = 600f;
        private const int PounceWindupTime = 25;
        private const int PounceTime = 42;
        private const int PounceRecoverTime = 25;
        private const float PounceSpeed = 16f;

        public enum ChimeraState : byte
        {
            Walk,
            ClawWindup,
            ClawSwipe,
            ClawRecover,
            SpikeWindup,
            SpikeRecover,
            PounceWindup,
            Pounce,
            PounceRecover,
        }

        public ChimeraState state = ChimeraState.Walk;
        public int stateTimer = 0;
        /// <summary>当前挥爪:0 = 左爪,1 = 右爪</summary>
        public byte clawPhase = 0;
        public byte comboLeft = 0;
        /// <summary>吞噬合拢演出倒计时(>0 时双爪向腹口合拢)</summary>
        public int devourAnim = 0;
        //插地内置冷却与行走相位:前者只驱动服务端派发,后者纯视觉,均不同步
        private int spikeCD = 0;
        private float walkPhase = 0;

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = 1;
            NPCID.Sets.MPAllowedEnemies[Type] = true;
            NPCID.Sets.NPCBestiaryDrawOffset[Type] = new NPCID.Sets.NPCBestiaryDrawModifiers() { Scale = 0.5f, PortraitScale = 0.6f };
        }

        public override void SetBestiary(Terraria.GameContent.Bestiary.BestiaryDatabase database, Terraria.GameContent.Bestiary.BestiaryEntry bestiaryEntry)
        {
            bestiaryEntry.Info.AddRange(new Terraria.GameContent.Bestiary.IBestiaryInfoElement[]
            {
                new Terraria.GameContent.Bestiary.FlavorTextBestiaryInfoElement("Mods.CalamityEntropy.ChaosChimeraBestiary")
            });
        }

        public override void SetDefaults()
        {
            NPC.width = 160;
            NPC.height = 150;
            NPC.damage = 200;
            if (Main.expertMode)
            {
                NPC.damage += 20;
            }
            if (Main.masterMode)
            {
                NPC.damage += 20;
            }
            NPC.lifeMax = 450000;
            NPC.defense = 105;
            NPC.knockBackResist = 0f;
            NPC.aiStyle = -1;
            NPC.noGravity = false;
            NPC.noTileCollide = false;
            NPC.HitSound = SoundID.NPCHit1;
            NPC.DeathSound = SoundID.NPCDeath1;
            NPC.value = Item.buyPrice(0, 5, 0, 0);
            NPC.Entropy().VoidTouchDR = 0.7f;
        }

        //掉落(M9 已落):铁拳按 §5.4 归魔像 4%;本怪只掉魂髓 1~2 @40%,统一挂在 VoidInvasionGNPC.ModifyNPCLoot

        public override bool CheckActive()
        {
            return false;
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write((byte)state);
            writer.Write(stateTimer);
            writer.Write(clawPhase);
            writer.Write(comboLeft);
            writer.Write(devourAnim);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            state = (ChimeraState)reader.ReadByte();
            stateTimer = reader.ReadInt32();
            clawPhase = reader.ReadByte();
            comboLeft = reader.ReadByte();
            devourAnim = reader.ReadInt32();
        }

        private void SwitchState(ChimeraState next)
        {
            state = next;
            stateTimer = 0;
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                NPC.netUpdate = true;
            }
        }

        /// <summary>腹口位置(吞噬判定与咀嚼粒子锚点)</summary>
        public Vector2 BellyPos => NPC.Center + new Vector2(0, 26);

        public override void AI()
        {
            //扑击 220 / 躯体 200(§2.10)
            NPC.damage = state == ChimeraState.Pounce ? NPC.defDamage + 20 : NPC.defDamage;

            if (devourAnim > 0)
            {
                devourAnim--;
                if (!Main.dedServ)
                {
                    //吞噬拖拽(§2.10):前半段猎物残渣被粒子流拽进腹口(环带取点全力倒吸)
                    if (devourAnim > 15)
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(55f, 120f);
                            var drag = PRTLoader.NewParticle<PRT_Void>(BellyPos + offset, -offset * 0.13f, Color.White, 1f);
                            drag.Opacity = 0.7f;
                        }
                        if (Main.rand.NextBool(2))
                        {
                            Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(60f, 110f);
                            var line = PRTLoader.NewParticle<PRT_LineCal>(BellyPos + offset, -offset * 0.12f,
                                new Color(190, 110, 255), Main.rand.NextFloat(0.4f, 0.65f));
                            line.Configure(false, 12);
                        }
                    }
                    //咀嚼拍:三口,每口侧向溅射 + 进食声
                    if (devourAnim == 20 || devourAnim == 12 || devourAnim == 4)
                    {
                        SoundEngine.PlaySound(SoundID.Item2 with { Volume = 0.8f, Pitch = Main.rand.NextFloat(-0.3f, 0f) }, BellyPos);
                        for (int i = 0; i < 5; i++)
                        {
                            Vector2 vel = new Vector2(Main.rand.NextFloat(-4f, 4f), -Main.rand.NextFloat(1f, 4f));
                            var chew = PRTLoader.NewParticle<PRT_GlowSparkCal>(BellyPos + CEUtils.randomPointInCircle(16f), vel,
                                new Color(200, 110, 255), Main.rand.NextFloat(0.3f, 0.5f));
                            chew.Configure(true, 18, new Vector2(0.5f, 1.5f), quickShrink: true);
                        }
                        var burst = PRTLoader.NewParticle<PRT_Void>(BellyPos, new Vector2(0, -1f), Color.White, 1f);
                        burst.Opacity = 0.8f;
                    }
                }
            }
            TryDevour();

            if (!NPC.HasValidTarget)
            {
                NPC.TargetClosest(false);
                if (!NPC.HasValidTarget)
                {
                    NPC.velocity.X *= 0.9f;
                    return;
                }
            }
            Player target = Main.player[NPC.target];
            stateTimer++;
            bool grounded = NPC.velocity.Y == 0;
            if (state != ChimeraState.Pounce && state != ChimeraState.PounceWindup)
            {
                NPC.direction = NPC.spriteDirection = target.Center.X > NPC.Center.X ? 1 : -1;
            }
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                spikeCD++;
            }

            switch (state)
            {
                case ChimeraState.Walk:
                    {
                        float dist = NPC.Center.Distance(target.Center);
                        NPC.velocity.X = MathHelper.Lerp(NPC.velocity.X, WalkSpeed * NPC.direction, 0.06f);
                        if (Math.Abs(NPC.velocity.X) > 0.2f && grounded)
                        {
                            walkPhase += Math.Abs(NPC.velocity.X) * 0.045f;
                            if (NPC.collideX && !ClimbStep(NPC.direction, 2))
                            {
                                NPC.velocity.Y = -7f;
                            }
                        }
                        if (grounded && stateTimer >= 30 && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            if (spikeCD >= SpikeInterval && dist < 520f)
                            {
                                //插地爆发(每 10s,近中距,§2.10)
                                spikeCD = 0;
                                SwitchState(ChimeraState.SpikeWindup);
                            }
                            else if (dist < ClawTriggerRange)
                            {
                                clawPhase = (byte)(NPC.direction == 1 ? 1 : 0);
                                comboLeft = (byte)Main.rand.Next(2, 4);
                                SwitchState(ChimeraState.ClawWindup);
                            }
                            else if (dist > PounceTriggerRange)
                            {
                                SwitchState(ChimeraState.PounceWindup);
                            }
                        }
                        break;
                    }
                case ChimeraState.ClawWindup:
                    {
                        //左爪前摇 20t 爪后拉(§2.10)
                        NPC.velocity.X *= 0.85f;
                        if (stateTimer >= ClawWindupTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            SwitchState(ChimeraState.ClawSwipe);
                        }
                        break;
                    }
                case ChimeraState.ClawSwipe:
                    {
                        NPC.velocity.X *= 0.9f;
                        if (stateTimer == 1)
                        {
                            if (!Main.dedServ)
                            {
                                SoundEngine.PlaySound(SoundID.Item71, NPC.Center);
                            }
                            //弧形判定弹幕 140px,230 档(§2.10)
                            if (Main.netMode != NetmodeID.MultiplayerClient)
                            {
                                Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center + new Vector2(NPC.direction * 110f, -6f), Vector2.Zero,
                                    ModContent.ProjectileType<ChimeraClawSlash>(), (int)(NPC.damage * 0.575f), 3f, -1, NPC.whoAmI, NPC.direction);
                            }
                        }
                        if (stateTimer >= ClawSwipeTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            comboLeft--;
                            if (comboLeft > 0)
                            {
                                SwitchState(ChimeraState.ClawRecover);
                            }
                            else
                            {
                                SwitchState(ChimeraState.Walk);
                            }
                        }
                        break;
                    }
                case ChimeraState.ClawRecover:
                    {
                        //段间隔 25t,换爪回扫(§2.10)
                        NPC.velocity.X *= 0.9f;
                        if (stateTimer >= ClawGapTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            clawPhase ^= 1;
                            SwitchState(ChimeraState.ClawSwipe);
                        }
                        break;
                    }
                case ChimeraState.SpikeWindup:
                    {
                        //双大爪高举 30t,地面裂纹尘(§2.10)
                        NPC.velocity.X *= 0.8f;
                        if (!Main.dedServ && Main.rand.NextBool(2))
                        {
                            Dust d = Dust.NewDustDirect(NPC.BottomLeft - new Vector2(20, 8), NPC.width + 40, 8, DustID.Smoke, 0, -1.8f, 120, default, 1.2f);
                            d.noGravity = true;
                        }
                        if (stateTimer >= SpikeWindupTime)
                        {
                            if (stateTimer == SpikeWindupTime && !Main.dedServ)
                            {
                                SoundEngine.PlaySound(SoundID.Item14 with { Volume = 0.8f }, NPC.Center);
                                ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.NoDirQuickShake(6), Main.LocalPlayer.Distance(NPC.Center));
                            }
                            if (Main.netMode != NetmodeID.MultiplayerClient)
                            {
                                SpawnGroundSpikes();
                                SwitchState(ChimeraState.SpikeRecover);
                            }
                        }
                        break;
                    }
                case ChimeraState.SpikeRecover:
                    {
                        NPC.velocity.X *= 0.85f;
                        if (stateTimer >= SpikeRecoverTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            SwitchState(ChimeraState.Walk);
                        }
                        break;
                    }
                case ChimeraState.PounceWindup:
                    {
                        //伏身 25t(§2.10)
                        NPC.velocity.X *= 0.8f;
                        if (stateTimer >= PounceWindupTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            NPC.velocity = new Vector2(NPC.direction * PounceSpeed, -5.5f);
                            SwitchState(ChimeraState.Pounce);
                        }
                        break;
                    }
                case ChimeraState.Pounce:
                    {
                        //16px/t 扑击 0.7s,足节乱蹬(§2.10)
                        NPC.velocity.X = NPC.direction * PounceSpeed;
                        walkPhase += 0.35f;
                        if (!Main.dedServ)
                        {
                            //起扑拍:蹬地尘爆 + 前扑速度线
                            if (stateTimer == 1)
                            {
                                for (int i = 0; i < 12; i++)
                                {
                                    Dust d = Dust.NewDustDirect(NPC.BottomLeft - new Vector2(10, 12), NPC.width + 20, 12, DustID.Smoke,
                                        -NPC.direction * Main.rand.NextFloat(1f, 4f), -Main.rand.NextFloat(1f, 3f), 120, default, Main.rand.NextFloat(1f, 1.6f));
                                    d.noGravity = Main.rand.NextBool();
                                }
                            }
                            if (Main.rand.NextBool(2))
                            {
                                var line = PRTLoader.NewParticle<PRT_LineCal>(NPC.Center + CEUtils.randomPointInCircle(50f),
                                    new Vector2(-NPC.direction * Main.rand.NextFloat(6f, 11f), Main.rand.NextFloat(-1f, 1f)),
                                    new Color(190, 110, 255), Main.rand.NextFloat(0.4f, 0.7f));
                                line.Configure(false, 10);
                            }
                        }
                        bool wallStop = NPC.collideX && grounded && !ClimbStep(NPC.direction, 2);
                        if ((stateTimer >= PounceTime || wallStop) && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            SwitchState(ChimeraState.PounceRecover);
                        }
                        break;
                    }
                case ChimeraState.PounceRecover:
                    {
                        //硬直 25t(§2.10):落身拍给闷响尘土 + 轻震屏(重量语义)
                        if (stateTimer == 1 && !Main.dedServ)
                        {
                            SoundEngine.PlaySound(SoundID.Item14 with { Volume = 0.5f, Pitch = -0.4f }, NPC.Center);
                            CEUtils.SetShake(NPC.Center, 3.5f, 1000);
                            for (int i = 0; i < 14; i++)
                            {
                                Dust d = Dust.NewDustDirect(NPC.BottomLeft - new Vector2(10, 14), NPC.width + 20, 14, DustID.Smoke,
                                    Main.rand.NextFloat(-3f, 3f), -Main.rand.NextFloat(1f, 3f), 120, default, Main.rand.NextFloat(1f, 1.7f));
                                d.noGravity = Main.rand.NextBool();
                            }
                        }
                        NPC.velocity.X *= 0.8f;
                        if (stateTimer >= PounceRecoverTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            SwitchState(ChimeraState.Walk);
                        }
                        break;
                    }
            }
        }

        /// <summary>
        /// 吞噬被动(§2.10):骚扰层事件怪(教徒/术士/烛灵)进腹部 120px。
        /// 仅服务端判定与置位:直接 active=false + SyncNPC,不走 kill 流程(无掉落无进度不算击杀),
        /// 回 2.5% 最大生命(HealEffect 飘绿字自带广播),合拢演出经 devourAnim 随 netUpdate 下发。
        /// </summary>
        private void TryDevour()
        {
            if (Main.netMode == NetmodeID.MultiplayerClient || devourAnim > 0)
                return;
            foreach (NPC n in Main.ActiveNPCs)
            {
                if (n.whoAmI == NPC.whoAmI || n.friendly || n.dontTakeDamage)
                    continue;
                if (n.ModNPC is not VoidCultist && n.ModNPC is not VoidCandleWisp)
                    continue;
                if (n.Center.Distance(BellyPos) > DevourRange)
                    continue;

                n.active = false;
                if (Main.netMode == NetmodeID.Server)
                {
                    NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, n.whoAmI);
                }
                int heal = (int)(NPC.lifeMax * DevourHealFrac);
                NPC.life = Math.Min(NPC.life + heal, NPC.lifeMax);
                NPC.HealEffect(heal);
                devourAnim = 30;
                NPC.netUpdate = true;
                break;
            }
        }

        /// <summary>服务端:朝玩家方向依次 5 根地刺,间距 80px,各自贴地 + 依次错拍(§2.10),200 档。</summary>
        private void SpawnGroundSpikes()
        {
            int dir = NPC.direction;
            int damage = (int)(NPC.damage * 0.5f);
            for (int i = 0; i < 5; i++)
            {
                Vector2 basePos = FindGround(NPC.Bottom + new Vector2(dir * (110 + 80 * i), -32));
                Projectile.NewProjectile(NPC.GetSource_FromAI(), basePos, Vector2.Zero,
                    ModContent.ProjectileType<ChimeraGroundSpike>(), damage, 2f, -1, ChimeraGroundSpike.WarnTime + i * 8);
            }
        }

        /// <summary>从起点向下探最近地表。</summary>
        private static Vector2 FindGround(Vector2 from)
        {
            Point tile = from.ToTileCoordinates();
            for (int i = 0; i < 40; i++)
            {
                int y = tile.Y + i;
                if (!WorldGen.InWorld(tile.X, y, 8))
                    break;
                if (WorldGen.SolidOrSlopedTile(tile.X, y))
                {
                    return new Vector2(from.X, y * 16);
                }
            }
            return from;
        }

        /// <summary>≤maxTiles 格台阶直接抬升,双端各自推进。</summary>
        private bool ClimbStep(int dir, int maxTiles)
        {
            for (int h = 1; h <= maxTiles; h++)
            {
                Vector2 test = NPC.position + new Vector2(dir * 8f, -h * 16);
                if (!Collision.SolidCollision(test, NPC.width, NPC.height))
                {
                    NPC.position.Y -= h * 16;
                    NPC.position.X += dir * 4f;
                    return true;
                }
            }
            return false;
        }

        // ---- 程序化组装绘制(镜像 AcropolisMachine 肢体写法,§2.10) ----

        /// <summary>大爪姿态角:内扫方向为正(左爪顺时针内扫 = +,右爪镜像 = -)。</summary>
        private float ClawRotation(bool leftClaw)
        {
            float sweepSign = leftClaw ? 1f : -1f;
            bool isActing = (clawPhase == 0) == leftClaw;
            float idle = (float)Math.Sin(Main.GlobalTimeWrappedHourly * 1.7f + (leftClaw ? 0 : 1.4f)) * 0.06f;

            //吞噬合拢:双爪一起向腹口收(0→1→0 包络)
            if (devourAnim > 0)
            {
                float t = devourAnim / 30f;
                float close = (float)Math.Sin((1f - t) * MathHelper.Pi);
                return sweepSign * 1.1f * close + idle;
            }
            switch (state)
            {
                case ChimeraState.ClawWindup:
                    {
                        if (!isActing)
                            return idle;
                        float p = MathHelper.Clamp(stateTimer / (float)ClawWindupTime, 0f, 1f);
                        return -sweepSign * 0.85f * p + idle;
                    }
                case ChimeraState.ClawSwipe:
                    {
                        if (!isActing)
                            return idle;
                        float p = MathHelper.Clamp(stateTimer / (float)ClawSwipeTime, 0f, 1f);
                        return sweepSign * MathHelper.Lerp(-0.85f, 1.35f, p) + idle;
                    }
                case ChimeraState.ClawRecover:
                    {
                        if (!isActing)
                            return idle;
                        float p = MathHelper.Clamp(stateTimer / (float)ClawGapTime, 0f, 1f);
                        return sweepSign * MathHelper.Lerp(1.35f, 0f, p) + idle;
                    }
                case ChimeraState.SpikeWindup:
                    {
                        //双大爪高举(§2.10)
                        float p = MathHelper.Clamp(stateTimer / (float)SpikeWindupTime, 0f, 1f);
                        return -sweepSign * 1.5f * p + idle;
                    }
                case ChimeraState.SpikeRecover:
                    {
                        //插地:爪插在地上缓收
                        float p = MathHelper.Clamp(stateTimer / (float)SpikeRecoverTime, 0f, 1f);
                        return sweepSign * MathHelper.Lerp(1.2f, 0f, p) + idle;
                    }
                case ChimeraState.PounceWindup:
                case ChimeraState.Pounce:
                    return -sweepSign * 0.4f + idle;
                default:
                    return idle;
            }
        }

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (NPC.IsABestiaryIconDummy)
            {
                return true;
            }
            Color c = drawColor * NPC.Opacity;
            //伏身/硬直时整体下压(前摇可读性)
            float crouch = state == ChimeraState.PounceWindup ? MathHelper.Clamp(stateTimer / (float)PounceWindupTime, 0f, 1f) * 14f
                : state == ChimeraState.PounceRecover ? 10f : 0f;
            Vector2 bodyCenter = NPC.Center + BodyOffset + new Vector2(0, crouch) - screenPos;
            float speedFactor = MathHelper.Clamp(Math.Abs(NPC.velocity.X) / WalkSpeed, 0f, 2f);

            Vector2 Art(Vector2 artOffset) => bodyCenter + artOffset * DrawScale;

            //远侧三腿(右腿组画在躯干后)
            for (int i = 0; i < 3; i++)
            {
                float legRot = (float)Math.Sin(walkPhase + i * 1.05f + MathHelper.Pi) * 0.3f * Math.Max(0.25f, speedFactor);
                Texture2D leg = legRTex[i];
                spriteBatch.Draw(leg, Art(LegRMounts[i]), null, c * 0.82f, legRot, new Vector2(leg.Width * 0.32f, leg.Height * 0.22f), DrawScale, SpriteEffects.None, 0);
            }
            //远侧小臂与大爪
            Texture2D armR = armRTex.Value;
            float armSwayR = (float)Math.Sin(Main.GlobalTimeWrappedHourly * 2.1f + 0.8f) * 0.09f;
            spriteBatch.Draw(armR, Art(SmallArmR), null, c * 0.9f, armSwayR, ArmROrigin, DrawScale, SpriteEffects.None, 0);
            Texture2D clawR = clawRTex.Value;
            spriteBatch.Draw(clawR, Art(ShoulderR), null, c * 0.92f, ClawRotation(false), ClawROrigin, DrawScale, SpriteEffects.None, 0);

            //头部触角(躯干顶,sin 摆)
            Texture2D tentacle = tentacleTex.Value;
            float tentacleSway = (float)Math.Sin(Main.GlobalTimeWrappedHourly * 1.9f) * 0.1f;
            spriteBatch.Draw(tentacle, Art(TentacleMount), null, c, tentacleSway, TentacleOrigin, DrawScale, SpriteEffects.None, 0);

            //躯干基座
            Texture2D body = bodyTex.Value;
            spriteBatch.Draw(body, bodyCenter, null, c, 0, new Vector2(200, 200), DrawScale, SpriteEffects.None, 0);

            //腹节呼吸缩放 1±0.03(§2.10);吞噬时鼓动加剧
            Texture2D belly = bellyTex.Value;
            float breath = 1f + 0.03f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 2.4f) + (devourAnim > 0 ? 0.05f * (float)Math.Sin(devourAnim * 0.6f) : 0f);
            spriteBatch.Draw(belly, Art(BellyMount), null, c, 0, belly.Size() / 2, DrawScale * breath, SpriteEffects.None, 0);

            //近侧三腿
            for (int i = 0; i < 3; i++)
            {
                float legRot = (float)Math.Sin(walkPhase + i * 1.05f) * 0.3f * Math.Max(0.25f, speedFactor);
                Texture2D leg = legLTex[i];
                spriteBatch.Draw(leg, Art(LegLMounts[i]), null, c, legRot, new Vector2(leg.Width * 0.68f, leg.Height * 0.22f), DrawScale, SpriteEffects.None, 0);
            }
            //近侧小臂与大爪
            Texture2D armL = armLTex.Value;
            float armSwayL = (float)Math.Sin(Main.GlobalTimeWrappedHourly * 2.1f) * 0.09f;
            spriteBatch.Draw(armL, Art(SmallArmL), null, c, armSwayL, ArmLOrigin, DrawScale, SpriteEffects.None, 0);
            Texture2D clawL = clawLTex.Value;
            spriteBatch.Draw(clawL, Art(ShoulderL), null, c, ClawRotation(true), ClawLOrigin, DrawScale, SpriteEffects.None, 0);
            return false;
        }

        public override void HitEffect(NPC.HitInfo hit)
        {
            if (NPC.life > 0 || Main.dedServ)
                return;
            for (int i = 0; i < 64; i++)
            {
                var p = PRTLoader.NewParticle<PRT_Void>(NPC.Center,
                    CEUtils.randomRot().ToRotationVector2() * Main.rand.Next(0, 500) * 0.01f, Color.White, 1f);
                p.Opacity = Main.rand.Next(20, 100) * 0.01f;
            }
        }
    }
}
