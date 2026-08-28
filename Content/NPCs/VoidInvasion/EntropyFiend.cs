using CalamityEntropy.Assets.Register;
using CalamityEntropy.Common;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using CalamityEntropy.Content.Projectiles.VoidInvasion;
using InnoVault;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Utilities;

namespace CalamityEntropy.Content.NPCs.VoidInvasion
{
    /// <summary>
    /// 虚熵魔物(void-invasion.md §3.2):99% 守门小 Boss,事件的"期末考"。主题 = 虚空结晶。
    /// 技能循环 扇射→俯冲→扇射→牢笼→(HP&lt;50% 后插入熵爆)→回扇射,同招不连发,招间呼吸 1s;
    /// 被动蜕晶:每损失 25% 生命抖落 8 枚环形水晶。
    /// 绘制为 PreDraw 程序化组装:body 基座 + 双翼程序化扑翼(±22°、纵向 0.9~1.1、周期 40t,俯冲后掠)
    /// + 双浮游手椭圆轨道(施法收拢胸前)。
    /// 入场由 99% 脚本生成后自驱(暗脉冲→巨门→部件飞入→咆哮),全程无敌不攻击;
    /// 死亡走部件逐个碎裂 + 核心爆裂,服务端在演出末尾结算 <see cref="Events.VoidInvasion.SetVictory"/>。
    /// 脱战(玩家全灭/远离)与真死互斥:脱战只置 active=false 并挂 10s 重生延迟,不走 OnKill。
    /// 状态字段全进 SendExtraAI;牢笼中心服务端定格广播;熵爆安全环由 seed+burstCount 确定性推导。
    /// </summary>
    public class EntropyFiend : ModNPC, IVoidInvasionNPC
    {
        public override string Texture => "CalamityEntropy/Content/NPCs/VoidInvasion/EntropyFiend/body";

        public enum FiendState : byte
        {
            Entrance,     //入场演出(无敌不攻击)
            Hover,        //招间呼吸拍
            CrystalFan,   //1 水晶扇射
            DiveTriple,   //2 俯冲三连
            CrystalCage,  //3 水晶牢笼
            EntropyBurst, //4 熵爆(<50% 解锁)
            Death         //死亡演出
        }

        //———节拍常量(§3.2)———
        //入场:暗脉冲 2.5s → 巨门 → 部件飞入 1.5s → 咆哮
        private const int EntrancePulse = 150;
        private const int EntranceTotal = 240;
        private const int HoverPause = 60;            //招间呼吸 1s
        //扇射:前摇 35t → 3 波 ×7(波间 18t)→ 收 40t
        private const int FanWindup = 35;
        private const int FanWaveGap = 18;
        private const int FanTotal = FanWindup + FanWaveGap * 2 + 40;
        //俯冲:爬升 30t / 俯冲 40t / 拉起 20t ×3,第 3 次拉起抛环形水晶 → 喘息 50t
        private const int DiveClimb = 30;
        private const int DiveActive = 40;
        private const int DiveCycle = 90;
        private const int DiveTotal = DiveCycle * 3 + 50;
        //牢笼:前摇 50t(定格牢笼中心)→ 围环存续期悬停(可打窗口)
        private const int CageWindup = 50;
        private const int CageTotal = CageWindup + VoidCrystalCage.TotalLife;
        //熵爆:长前摇 80t(70~80 静默拍)→ 爆发 → 虚脱 60t
        private const int BurstWindup = 80;
        private const int BurstTotal = BurstWindup + 60;
        private const float BurstRadius = 1400f;
        private const float SafeRingRadius = 120f;
        //死亡演出:40 过曝 → 部件逐个剥离 → 150 核心爆裂 → 165 真死
        private const int DeathBlast = 150;
        private const int DeathTotal = 165;

        //———同步状态(全进 SendExtraAI,双端同序)———
        public byte attackID = (byte)FiendState.Entrance;
        public int attackTimer = 0;
        public byte diveCount = 0;
        /// <summary>技能循环指针(踩 20 取模,4 招与 5 招循环的公倍数)</summary>
        public byte cycleIndex = 0;
        /// <summary>熵爆次数:安全环位置推导的确定性盐</summary>
        public byte burstCount = 0;
        public int seed = -1;
        public bool entropyUnlocked = false;
        /// <summary>牢笼中心(§3.2:服务端定格目标玩家位置后广播)</summary>
        public Vector2 cageCenter = Vector2.Zero;

        //———服务端本地(生成弹幕都在服务端,无需同步)———
        private int moltStage = 0;
        //———双端各自推进的视觉/杂项———
        private int escape = 0;
        private float flapCounter = 0;
        private float handOrbit = 0;
        /// <summary>0 = 常态扑翼,1 = 俯冲后掠锁定,-0.4 = 定身展翼</summary>
        private float wingSweep = 0;
        /// <summary>0 = 浮游手绕轨,1 = 收拢胸前(施法)</summary>
        private float castGather = 0;
        private int hoverSide = 1;
        private bool prevUnlocked = false;
        private readonly List<Vector2> odp = new List<Vector2>();
        /// <summary>蜕晶余韵:翼面碎裂纹白闪(纯客户端,由同步 life 推层级)</summary>
        private float moltFlash = 0;
        /// <summary>蜕晶余韵:翼震颤强度</summary>
        private float wingShake = 0;
        private int clientMoltStage = 0;
        private bool clientMoltInit = false;

        public FiendState State => (FiendState)attackID;
        /// <summary>牢笼弹幕查询用:死亡演出期间牢笼中断不碎裂</summary>
        public bool InDeathAnim => State == FiendState.Death;
        /// <summary>熵爆蓄力进度(EntropyBossbar 演出用):非蓄力窗口恒 0</summary>
        public float BurstCharge => State == FiendState.EntropyBurst ? MathHelper.Clamp(attackTimer / (float)BurstWindup, 0f, 1f) : 0f;

        //部件贴图只在绘制路径读取(专用服务器上恒为 null)
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/EntropyFiend/wingL")]
        private static Asset<Texture2D> wingLTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/EntropyFiend/wingR")]
        private static Asset<Texture2D> wingRTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/EntropyFiend/handL")]
        private static Asset<Texture2D> handLTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/EntropyFiend/handR")]
        private static Asset<Texture2D> handRTex;
        [VaultLoaden("CalamityEntropy/Content/Projectiles/VoidInvasion/VoidGlyph")]
        private static Asset<Texture2D> glyphTex;
        [VaultLoaden("CalamityEntropy/Content/Projectiles/VoidInvasion/VoidCrystal")]
        private static Asset<Texture2D> crystalTex;

        public static int icon = -1;
        public static void loadHead()
        {
            string path = "CalamityEntropy/Content/NPCs/VoidInvasion/EntropyFiend/icon";
            CalamityEntropy.Instance.AddBossHeadTexture(path, -1);
            icon = ModContent.GetModBossHeadSlot(path);
        }
        public override void BossHeadSlot(ref int index)
        {
            index = icon;
        }

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[NPC.type] = 1;
            NPCID.Sets.MustAlwaysDraw[NPC.type] = true;
            NPCID.Sets.MPAllowedEnemies[Type] = true;
        }

        public override void SetBestiary(Terraria.GameContent.Bestiary.BestiaryDatabase database, Terraria.GameContent.Bestiary.BestiaryEntry bestiaryEntry)
        {
            bestiaryEntry.Info.AddRange(new Terraria.GameContent.Bestiary.IBestiaryInfoElement[]
            {
                new Terraria.GameContent.Bestiary.FlavorTextBestiaryInfoElement("Mods.CalamityEntropy.EntropyFiendBestiary")
            });
        }

        public override void SetDefaults()
        {
            //§3.2 数值档:NPC.boss=false + EntropyBossbar.bigBarMiniBoss 大血条
            NPC.boss = false;
            NPC.width = 130;
            NPC.height = 130;
            NPC.damage = 220;
            if (Main.expertMode)
            {
                NPC.damage += 20;
            }
            if (Main.masterMode)
            {
                NPC.damage += 20;
            }
            NPC.defense = 110;
            NPC.lifeMax = 1800000;
            NPC.HitSound = SoundID.NPCHit5;
            NPC.DeathSound = SoundID.NPCDeath6;
            NPC.value = Item.buyPrice(0, 15, 0, 0);
            NPC.knockBackResist = 0f;
            NPC.noTileCollide = true;
            NPC.noGravity = true;
            NPC.dontCountMe = true;
            NPC.netAlways = true;
            NPC.Entropy().VoidTouchDR = 0.7f;
            if (!Main.dedServ)
            {
                //守门战与教皇同曲(§3.2:"教皇的先锋"叙事)
                Music = MusicLoader.GetMusicSlot(Mod, "Assets/Sounds/Music/RepBossTrack");
            }
        }

        public override void OnSpawn(IEntitySource source)
        {
            seed = Main.rand.Next(0, 10000);
            if (Main.netMode == NetmodeID.Server)
            {
                NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, NPC.whoAmI);
            }
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write(attackID);
            writer.Write(attackTimer);
            writer.Write(diveCount);
            writer.Write(cycleIndex);
            writer.Write(burstCount);
            writer.Write(seed);
            writer.Write(entropyUnlocked);
            writer.WriteVector2(cageCenter);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            attackID = reader.ReadByte();
            attackTimer = reader.ReadInt32();
            diveCount = reader.ReadByte();
            cycleIndex = reader.ReadByte();
            burstCount = reader.ReadByte();
            seed = reader.ReadInt32();
            entropyUnlocked = reader.ReadBoolean();
            cageCenter = reader.ReadVector2();
        }

        public override bool CheckActive()
        {
            return false;
        }

        public override bool CanHitPlayer(Player target, ref int cooldownSlot)
        {
            //入场/死亡全程无伤;熵爆虚脱段是最大可打窗口,接触归零(§3.2 公平阀)
            if (State == FiendState.Entrance || State == FiendState.Death)
            {
                return false;
            }
            if (State == FiendState.EntropyBurst && attackTimer > BurstWindup)
            {
                return false;
            }
            return true;
        }

        /// <summary>熵爆安全环圆心(§3.2:seed + burstCount 确定性推导,绘制与判定共用同一函数)</summary>
        public Vector2 SafeRingPos(int k)
        {
            var rnd = new UnifiedRandom(seed * 131 + burstCount * 17 + k * 7);
            float ang = rnd.NextFloat() * MathHelper.TwoPi;
            return NPC.Center + ang.ToRotationVector2() * (280f + k * 240f);
        }

        private void SwitchState(FiendState next)
        {
            attackID = (byte)next;
            attackTimer = 0;
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                NPC.netUpdate = true;
                if (NPC.netSpam >= 10)
                    NPC.netSpam = 9;
            }
        }

        /// <summary>选下一招(仅服务端):固定循环 扇→冲→扇→笼(→爆),同招不连发由循环表保证。</summary>
        private void NextAttack()
        {
            FiendState[] cycle = entropyUnlocked
                ? new[] { FiendState.CrystalFan, FiendState.DiveTriple, FiendState.CrystalFan, FiendState.CrystalCage, FiendState.EntropyBurst }
                : new[] { FiendState.CrystalFan, FiendState.DiveTriple, FiendState.CrystalFan, FiendState.CrystalCage };
            FiendState next = cycle[cycleIndex % cycle.Length];
            cycleIndex = (byte)((cycleIndex + 1) % 20);
            diveCount = 0;
            SwitchState(next);
        }

        public override void AI()
        {
            attackTimer++;
            UpdateVisualCounters();

            if (State == FiendState.Entrance)
            {
                EntranceAI();
                return;
            }

            //死亡演出入口:镜像深渊亡魂的 life<2 陷阱(CheckDead 回填 life=1)
            if (NPC.life < 2 && State != FiendState.Death)
            {
                SwitchState(FiendState.Death);
                NPC.netUpdate = true;
                if (NPC.netSpam >= 10)
                    NPC.netSpam = 9;
                if (Main.netMode == NetmodeID.Server)
                {
                    NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, NPC.whoAmI);
                }
            }
            if (State == FiendState.Death)
            {
                DeathAI();
                return;
            }

            NPC.dontTakeDamage = false;
            NPC.target = NPC.FindClosestPlayer();
            //脱战判定(§1.2):玩家全灭立即计,远离(>4000px)持续 5s 也计;都走 10s 重生路径
            bool farAway = NPC.HasValidTarget && NPC.Center.Distance(Main.player[NPC.target].Center) > 4000f;
            if (!NPC.HasValidTarget || (farAway && escape > 0))
            {
                //升空渐隐,despawn 后由事件系统在最近地表玩家附近重生(进度保持 99%)
                escape++;
                if (!NPC.HasValidTarget)
                {
                    NPC.velocity.Y -= 1;
                    NPC.velocity *= 0.98f;
                }
                if (escape >= (NPC.HasValidTarget ? 300 : 160) && Main.netMode != NetmodeID.MultiplayerClient)
                {
                    Events.VoidInvasion.OnFiendEscape(NPC.Center);
                    NPC.active = false;
                    if (Main.netMode == NetmodeID.Server)
                    {
                        NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, NPC.whoAmI);
                    }
                }
                if (!NPC.HasValidTarget)
                {
                    return;
                }
            }
            else
            {
                escape = farAway ? 1 : 0;
            }
            Player target = Main.player[NPC.target];

            //被动·蜕晶(§3.2):每损失 25% 生命抖落一圈 8 枚慢速水晶(服务端)
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                int stage = (int)((1f - NPC.life / (float)NPC.lifeMax) * 4f);
                while (moltStage < stage && moltStage < 3)
                {
                    moltStage++;
                    SpawnCrystalRing(NPC.Center, 8, 8f);
                    if (!Main.dedServ)
                    {
                        SoundEngine.PlaySound(SoundID.Item27 with { Pitch = -0.4f }, NPC.Center);
                    }
                }
                //熵爆解锁(§3.2:<50%)
                if (!entropyUnlocked && NPC.life < NPC.lifeMax / 2)
                {
                    entropyUnlocked = true;
                    NPC.netUpdate = true;
                }
            }
            //解锁拍的可见提示(双端在同步值翻转时各自放一次)
            if (entropyUnlocked && !prevUnlocked)
            {
                prevUnlocked = true;
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.ForceRoarPitched with { Volume = 0.7f, Pitch = -0.3f }, NPC.Center);
                    for (int i = 0; i < 30; i++)
                    {
                        var v = PRTLoader.NewParticle<PRT_Void>(NPC.Center, CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(2f, 7f), Color.White, 1f);
                        v.Opacity = Main.rand.Next(30, 100) * 0.01f;
                    }
                }
            }

            //接触伤害档(§3.2:常态 220,俯冲拍 250,虚脱归零),由同步态确定性推导
            bool divingHit = State == FiendState.DiveTriple && NPC.velocity.Length() > 24f;
            NPC.damage = divingHit ? (int)(NPC.defDamage * (250f / 220f) + 0.5f) : NPC.defDamage;
            if (State == FiendState.EntropyBurst && attackTimer > BurstWindup)
            {
                NPC.damage = 0;
            }

            switch (State)
            {
                case FiendState.Hover: HoverAI(target); break;
                case FiendState.CrystalFan: CrystalFanAI(target); break;
                case FiendState.DiveTriple: DiveTripleAI(target); break;
                case FiendState.CrystalCage: CrystalCageAI(target); break;
                case FiendState.EntropyBurst: EntropyBurstAI(target); break;
            }

            if (State != FiendState.DiveTriple)
            {
                NPC.rotation = NPC.rotation * 0.9f + NPC.velocity.X * 0.004f;
            }
        }

        /// <summary>视觉计数(双端各自推):扑翼周期 40t;施法收手与后掠由状态推导目标值后缓动;
        /// 蜕晶预兆余韵从同步的 life 推层级,两端各自闪(首帧对齐现状,防中途加入误闪)。</summary>
        private void UpdateVisualCounters()
        {
            flapCounter += MathHelper.TwoPi / 40f;
            handOrbit += 0.045f;
            if (moltFlash > 0)
            {
                moltFlash -= 0.05f;
            }
            if (wingShake > 0)
            {
                wingShake -= 0.033f;
            }

            //蜕晶拍检测:每损失 25% 翼面碎裂纹一闪 + 翼震颤 + 晶屑自翼面剥落
            int stageNow = (int)MathHelper.Clamp((1f - NPC.life / (float)NPC.lifeMax) * 4f, 0f, 4f);
            if (!clientMoltInit)
            {
                clientMoltInit = true;
                clientMoltStage = stageNow;
            }
            if (stageNow > clientMoltStage && State != FiendState.Death && State != FiendState.Entrance)
            {
                clientMoltStage = stageNow;
                moltFlash = 1f;
                wingShake = 1f;
                if (!Main.dedServ)
                {
                    for (int dir = -1; dir <= 1; dir += 2)
                    {
                        Vector2 wa = WingAnchor(dir);
                        for (int i = 0; i < 8; i++)
                        {
                            PRTLoader.NewParticle<PRT_CrystalGlow>(wa + CEUtils.randomPointInCircle(46),
                                new Vector2(dir * Main.rand.NextFloat(0.5f, 2f), Main.rand.NextFloat(1f, 3f)),
                                new Color(150, 115, 255), Main.rand.NextFloat(0.3f, 0.6f)).Configure(0.85f, true, PRTDrawModeEnum.AdditiveBlend, CEUtils.randomRot(), 30);
                        }
                        for (int i = 0; i < 5; i++)
                        {
                            Dust.NewDust(wa - new Vector2(20, 20), 40, 40, ModContent.DustType<Dusts.GlassBreak>());
                        }
                    }
                }
            }

            float sweepTarget = 0f;
            float gatherTarget = 0f;
            if (State == FiendState.DiveTriple && attackTimer % DiveCycle >= DiveClimb && attackTimer < DiveCycle * 3)
            {
                sweepTarget = 1f; //俯冲后掠锁定
            }
            else if (State == FiendState.CrystalCage && attackTimer <= CageWindup)
            {
                sweepTarget = -0.4f; //展翼定身
            }
            else if (State == FiendState.EntropyBurst && attackTimer > BurstWindup)
            {
                sweepTarget = 0.6f; //虚脱垂翼
            }
            if (State == FiendState.CrystalFan && attackTimer <= FanWindup)
            {
                gatherTarget = 1f; //双手收拢胸前凝晶
            }
            else if (State == FiendState.EntropyBurst && attackTimer <= BurstWindup)
            {
                gatherTarget = 1f;
            }
            wingSweep += (sweepTarget - wingSweep) * 0.15f;
            castGather += (gatherTarget - castGather) * 0.12f;

            //俯冲拖尾采样(双端视觉)
            odp.Add(NPC.Center);
            if (odp.Count > 10)
            {
                odp.RemoveAt(0);
            }
        }

        /// <summary>悬浮基线:玩家侧上方缓漂(侧向黏滞防抖),扇射前摇期减速让读招(公平阀)。</summary>
        private void HoverMovement(Player target, float sideDist, float upDist, float accel)
        {
            float dx = NPC.Center.X - target.Center.X;
            if (Math.Abs(dx) > 70f)
            {
                hoverSide = dx >= 0 ? 1 : -1;
            }
            float bob = (float)Math.Sin(flapCounter * 0.5f) * 26f;
            Vector2 want = target.Center + new Vector2(hoverSide * sideDist, -upDist + bob);
            Vector2 drift = (want - NPC.Center) * 0.035f;
            if (drift.Length() > 13f)
            {
                drift = drift.SafeNormalize(Vector2.Zero) * 13f;
            }
            NPC.velocity = Vector2.Lerp(NPC.velocity, drift, accel);
        }

        //———入场(§3.2):暗脉冲 2.5s → 巨门 → 部件飞入 1.5s → 咆哮 + 无伤冲击环———
        private void EntranceAI()
        {
            NPC.dontTakeDamage = true;
            NPC.damage = 0;
            NPC.velocity *= 0.9f;

            if (attackTimer == 1 && !Main.dedServ)
            {
                SoundEngine.PlaySound(SoundID.Roar with { Volume = 1.1f, Pitch = -0.8f }, NPC.Center);
            }
            if (attackTimer <= EntrancePulse)
            {
                //暗脉冲量力档:低鸣 + 爬坡震屏(天空已有事件滤镜,不再另开)
                if (!Main.dedServ && attackTimer % 10 == 0)
                {
                    float ramp = attackTimer / (float)EntrancePulse;
                    ScreenShaker.AddShake(new ScreenShaker.ScreenShake(Vector2.Zero,
                        Utils.Remap(Main.LocalPlayer.Distance(NPC.Center), 2600f, 800f, 0f, 1.5f + ramp * 3f)));
                }
                //巨门在脉冲中段开(服务端;门自带 40t 张开)
                if (attackTimer == 80 && Main.netMode != NetmodeID.MultiplayerClient)
                {
                    VoidPortal.Open(NPC.GetSource_FromAI(), NPC.Center, Vector2.UnitY, 140, 3f);
                }
            }
            else if (attackTimer < EntranceTotal)
            {
                //部件飞入拼合段:飞入曲线在 PreDraw 由 attackTimer 推导;此处做光流汇聚 + 逐件落位拍
                if (!Main.dedServ)
                {
                    if (Main.rand.NextBool(2))
                    {
                        Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(160f, 320f);
                        var p = PRTLoader.NewParticle<PRT_Light>(NPC.Center + offset, -offset * 0.06f, new Color(150, 90, 255), 0.6f);
                        p.Configure(0.85f, lifetime: 20);
                    }
                    //光流汇聚:仍在飞行的部件身后拉出向锚点收束的光线
                    if (attackTimer % 2 == 0)
                    {
                        for (int idx = 0; idx < 5; idx++)
                        {
                            (Vector2 flyOfs, float pAlpha) = EntrancePartLerp(idx);
                            if (pAlpha <= 0.02f || pAlpha >= 0.98f)
                            {
                                continue;
                            }
                            Vector2 anchor = idx switch
                            {
                                1 => WingAnchor(-1),
                                2 => WingAnchor(1),
                                3 => HandPos(0),
                                4 => HandPos(1),
                                _ => NPC.Center,
                            };
                            Vector2 partPos = anchor + flyOfs;
                            PRTLoader.NewParticle<PRT_LineCal>(partPos + CEUtils.randomPointInCircle(26), (anchor - partPos) * 0.09f,
                                new Color(160, 110, 255), 0.7f).Configure(false, 12);
                        }
                    }
                    //落位拍:每件部件抵达锚点的一瞬,一记闪 + 音高逐件抬升(由飞入曲线反解的确定性拍点)
                    for (int idx = 0; idx < 5; idx++)
                    {
                        int landTick = EntrancePulse + (int)Math.Ceiling((1f + idx * 0.18f) / 1.9f * (EntranceTotal - EntrancePulse));
                        if (attackTimer != landTick)
                        {
                            continue;
                        }
                        Vector2 anchor = idx switch
                        {
                            1 => WingAnchor(-1),
                            2 => WingAnchor(1),
                            3 => HandPos(0),
                            4 => HandPos(1),
                            _ => NPC.Center,
                        };
                        SoundEngine.PlaySound(SoundID.Item27 with { Volume = 0.6f, Pitch = -0.5f + idx * 0.18f }, anchor);
                        var flash = PRTLoader.NewParticle<PRT_Light>(anchor, Vector2.Zero, new Color(210, 170, 255), 0.9f);
                        flash.Configure(1f, lifetime: 12);
                        PRTLoader.NewParticle<PRT_PulseRing>(anchor, Vector2.Zero, new Color(170, 110, 255), 0.05f).Configure(0.9f, 16);
                        CEUtils.SetShake(NPC.Center, 2f, 1600);
                    }
                }
            }
            if (attackTimer == EntranceTotal && !Main.dedServ)
            {
                //咆哮 + 无伤冲击环(§3.2)
                SoundEngine.PlaySound(SoundID.ForceRoarPitched with { Volume = 1f }, NPC.Center);
                ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.ScreenShake(Vector2.Zero, 12), Main.LocalPlayer.Distance(NPC.Center), 2600);
                PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, new Color(170, 90, 255), 0.1f).Configure(4.5f, 40);
                PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, Color.White, 0.1f).Configure(3f, 30);
                for (int i = 0; i < 50; i++)
                {
                    var v = PRTLoader.NewParticle<PRT_Void>(NPC.Center, CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(2f, 9f), Color.White, 1f);
                    v.Opacity = Main.rand.Next(30, 100) * 0.01f;
                }
            }
            if (attackTimer >= EntranceTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchState(FiendState.Hover);
            }
        }

        //———呼吸拍:1s 漂移后服务端按循环表出招———
        private void HoverAI(Player target)
        {
            HoverMovement(target, 280f, 230f, 0.08f);
            if (attackTimer >= HoverPause && Main.netMode != NetmodeID.MultiplayerClient)
            {
                NextAttack();
            }
        }

        //———招式 1·水晶扇射(§3.2):前摇 35t 收手凝晶 → 3 波 ×7 枚(波间 ±12°)→ 收 40t———
        private void CrystalFanAI(Player target)
        {
            if (attackTimer <= FanWindup)
            {
                //读招减速(公平阀:前摇期少移动)
                HoverMovement(target, 280f, 230f, 0.04f);
                NPC.velocity *= 0.92f;
                if (!Main.dedServ)
                {
                    //指间凝晶 + 音调渐高
                    if (Main.rand.NextBool(2))
                    {
                        Vector2 chest = NPC.Center + new Vector2(0, 10);
                        Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(30f, 70f);
                        var p = PRTLoader.NewParticle<PRT_Light>(chest + offset, -offset * 0.08f, new Color(160, 110, 255), 0.45f);
                        p.Configure(0.8f, lifetime: 15);
                    }
                    if (attackTimer % 12 == 5)
                    {
                        SoundEngine.PlaySound(SoundID.Item72 with { Volume = 0.6f, Pitch = -0.4f + attackTimer / 12 * 0.25f }, NPC.Center);
                    }
                }
                return;
            }
            HoverMovement(target, 280f, 230f, 0.05f);
            int t = attackTimer - FanWindup;
            if (t % FanWaveGap == 1 && t <= FanWaveGap * 2 + 1)
            {
                //波拍 t = 1 / 19 / 37;波间摆角 0/+12°/-12°(§3.2)
                int wave = t / FanWaveGap;
                float waveOffset = wave == 0 ? 0f : (wave == 1 ? MathHelper.ToRadians(12) : MathHelper.ToRadians(-12));
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int damage = (int)(NPC.defDamage * 0.387f + 0.5f); //水晶 170 经典档(敌对弹幕命中 ×2)
                    Vector2 origin = NPC.Center + new Vector2(0, 14);
                    Vector2 baseDir = (target.Center - origin).SafeNormalize(Vector2.UnitX * hoverSide).RotatedBy(waveOffset);
                    for (int i = -3; i <= 3; i++)
                    {
                        Projectile.NewProjectile(NPC.GetSource_FromAI(), origin, baseDir.RotatedBy(i * MathHelper.ToRadians(12)) * 14f,
                            ModContent.ProjectileType<VoidCrystal>(), damage, 4f, -1, 0f);
                    }
                }
                if (!Main.dedServ)
                {
                    //发射拍:碎裂声 + 胸口爆闪脉冲 + 一记轻震(凝晶预览在这拍炸开成扇)
                    SoundEngine.PlaySound(SoundID.Item27 with { Volume = 0.9f }, NPC.Center);
                    Vector2 chest = NPC.Center + new Vector2(0, 14);
                    PRTLoader.NewParticle<PRT_PulseRing>(chest, Vector2.Zero, new Color(180, 120, 255), 0.05f).Configure(1.2f, 16);
                    var flash = PRTLoader.NewParticle<PRT_Light>(chest, Vector2.Zero, new Color(220, 180, 255), 0.9f);
                    flash.Configure(1f, lifetime: 10);
                    CEUtils.SetShake(NPC.Center, 3f, 1400);
                }
            }
            if (attackTimer >= FanTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchState(FiendState.Hover);
            }
        }

        //———招式 2·俯冲三连(§3.2):爬升收翼 → 锁定俯冲 30px/t(拖尾+破空音)→ 拉起 ×3,第 3 次抛 12 环晶 → 喘息 50t———
        private void DiveTripleAI(Player target)
        {
            if (attackTimer < DiveCycle * 3)
            {
                int phase = attackTimer % DiveCycle;
                if (phase < DiveClimb)
                {
                    //爬升前摇:去玩家侧上方高位,收翼(视觉在 UpdateVisualCounters)
                    Vector2 want = target.Center + new Vector2(hoverSide * 360f, -380f);
                    Vector2 drift = (want - NPC.Center) * 0.06f;
                    if (drift.Length() > 16f)
                    {
                        drift = drift.SafeNormalize(Vector2.Zero) * 16f;
                    }
                    NPC.velocity = Vector2.Lerp(NPC.velocity, drift, 0.12f);
                    NPC.rotation *= 0.85f;
                }
                else if (phase == DiveClimb)
                {
                    //俯冲发射拍:锁定玩家当前位,一帧点火(发射即定线,可预读)
                    if (Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        Vector2 dir = (target.Center - NPC.Center).SafeNormalize(Vector2.UnitY);
                        NPC.velocity = dir * 30f;
                        diveCount++;
                        NPC.netUpdate = true;
                    }
                    if (!Main.dedServ)
                    {
                        //点火帧:破空音 + 沿冲线的方向震 + 出发位炸开一圈粒子
                        SoundEngine.PlaySound(SoundID.DD2_WyvernDiveDown with { Volume = 1f, Pitch = -0.2f }, NPC.Center);
                        CEUtils.SetShake(NPC.Center, 4.5f, 1600);
                        PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, new Color(150, 90, 255), 0.05f).Configure(1.4f, 14);
                        for (int i = 0; i < 8; i++)
                        {
                            PRTLoader.NewParticle<PRT_LineCal>(NPC.Center + CEUtils.randomPointInCircle(30), -NPC.velocity.SafeNormalize(Vector2.UnitY).RotatedByRandom(0.5f) * Main.rand.NextFloat(4f, 9f),
                                new Color(140, 90, 255), Main.rand.NextFloat(0.5f, 0.9f)).Configure(false, 14);
                        }
                    }
                }
                else if (phase < DiveClimb + DiveActive)
                {
                    //直线冲刺段:不转向(速度即读招),身体顺速度倾斜;
                    //掠底:一旦冲过玩家水平线即提前进入拉起曲线(不改计时,只改速度,双端同式)
                    if (NPC.Center.Y > target.Center.Y + 140f && NPC.velocity.Y > 0)
                    {
                        NPC.velocity = Vector2.Lerp(NPC.velocity, new Vector2(NPC.velocity.X * 0.6f, -9f), 0.12f);
                    }
                    NPC.rotation = NPC.velocity.X * 0.02f;
                    if (!Main.dedServ)
                    {
                        if (Main.rand.NextBool(2))
                        {
                            var p = PRTLoader.NewParticle<PRT_Light>(NPC.Center + CEUtils.randomPointInCircle(40), -NPC.velocity * 0.1f, new Color(120, 70, 230), 0.6f);
                            p.Configure(0.8f, lifetime: 14);
                        }
                        //破空线:身侧拉出的反向风线,速度门控(只在真冲刺时出现)
                        if (NPC.velocity.Length() > 22f && Main.rand.NextBool(2))
                        {
                            PRTLoader.NewParticle<PRT_LineCal>(NPC.Center + CEUtils.randomPointInCircle(46), -NPC.velocity * Main.rand.NextFloat(0.15f, 0.3f),
                                new Color(170, 130, 255), Main.rand.NextFloat(0.6f, 1.1f)).Configure(false, 12);
                        }
                    }
                }
                else
                {
                    //掠底拉起:横速衰减 + 上拉
                    NPC.velocity = Vector2.Lerp(NPC.velocity, new Vector2(NPC.velocity.X * 0.35f, -11f), 0.14f);
                    //拉起首拍:掠底冲击(脉冲环 + 底部尘雾),把"贴地"这拍做实
                    if (phase == DiveClimb + DiveActive && !Main.dedServ)
                    {
                        PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center + new Vector2(0, 40), Vector2.Zero, new Color(140, 90, 255), 0.1f).Configure(2.2f, 22);
                        CEUtils.SetShake(NPC.Center, 3f, 1400);
                        for (int i = 0; i < 6; i++)
                        {
                            PRTLoader.NewParticle<PRT_HeavySmokeCal>(NPC.Center + new Vector2(Main.rand.NextFloat(-60f, 60f), 40f),
                                new Vector2(Main.rand.NextFloat(-3f, 3f), -Main.rand.NextFloat(1f, 3f)), new Color(90, 60, 200), Main.rand.NextFloat(0.5f, 0.9f)).Configure(0.65f, 22, 0, false, 0, true);
                        }
                    }
                    //第 3 次拉起开始时抛 12 枚环形水晶(§3.2)
                    if (attackTimer == DiveCycle * 2 + DiveClimb + DiveActive && Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        SpawnCrystalRing(NPC.Center, 12, 9f);
                    }
                }
            }
            else
            {
                //喘息 50t(可打窗口)
                NPC.velocity *= 0.93f;
                NPC.rotation *= 0.85f;
                if (attackTimer >= DiveTotal && Main.netMode != NetmodeID.MultiplayerClient)
                {
                    SwitchState(FiendState.Hover);
                }
            }
        }

        //———招式 3·水晶牢笼(§3.2):瞬移玩家正上方 500px → 前摇 50t → 定格牢笼中心并生成围环 → 围环存续期悬停———
        private void CrystalCageAI(Player target)
        {
            if (attackTimer == 1)
            {
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    NPC.Center = target.Center + new Vector2(0, -500);
                    NPC.velocity = Vector2.Zero;
                    //清上一轮定格,等本轮前摇结束再冻结(死亡间隙防呆:见下方 Zero 判定)
                    cageCenter = Vector2.Zero;
                    NPC.netUpdate = true;
                }
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.Item8 with { Pitch = -0.4f }, NPC.Center);
                    for (int i = 0; i < 30; i++)
                    {
                        var v = PRTLoader.NewParticle<PRT_Void>(NPC.Center, CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(1f, 6f), Color.White, 1f);
                        v.Opacity = Main.rand.Next(20, 90) * 0.01f;
                    }
                }
            }
            if (attackTimer <= CageWindup)
            {
                //展翼定身,周身法阵(法阵渐大在 PreDraw)
                NPC.velocity *= 0.85f;
                return;
            }
            if (cageCenter == Vector2.Zero)
            {
                //前摇结束的首个有目标拍才定格牢笼中心(§3.2:以玩家为心)并广播,再生成承载弹幕;
                //客户端在同步到定格值前原地缓停等待
                NPC.velocity *= 0.85f;
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    cageCenter = target.Center;
                    NPC.netUpdate = true;
                    int damage = (int)(NPC.defDamage * 0.387f + 0.5f); //牢笼水晶 170 经典档(敌对弹幕命中 ×2)
                    Projectile.NewProjectile(NPC.GetSource_FromAI(), cageCenter, Vector2.Zero,
                        ModContent.ProjectileType<VoidCrystalCage>(), damage, 2f, -1, NPC.whoAmI, CEUtils.randomRot());
                }
                return;
            }
            //围环存续期:悬在牢笼上空缓漂(长可打窗口,§3.2 牢笼本身就是这一拍的压力)
            Vector2 want = cageCenter + new Vector2(0, -560);
            NPC.velocity = Vector2.Lerp(NPC.velocity, (want - NPC.Center) * 0.02f, 0.06f);
            if (attackTimer >= CageTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchState(FiendState.Hover);
            }
        }

        //———招式 4·熵爆(§3.2,<50% 解锁):瞬移定身 → 80t 长前摇(安全环+紫光渐胀+音调爬升+末 10t 静默)→ 全场爆发 → 虚脱 60t———
        private void EntropyBurstAI(Player target)
        {
            if (attackTimer == 1)
            {
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    NPC.Center = target.Center + new Vector2(0, -500);
                    NPC.velocity = Vector2.Zero;
                    burstCount++;
                    NPC.netUpdate = true;
                }
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.Item8 with { Pitch = -0.6f }, NPC.Center);
                }
            }
            NPC.velocity = Vector2.Zero;

            if (attackTimer <= BurstWindup)
            {
                bool silence = attackTimer > BurstWindup - 10; //爆发前 10t 粒子骤停(静默拍)
                float charge = attackTimer / (float)BurstWindup;
                if (!Main.dedServ)
                {
                    //全场紫涨:全屏管线参数逐帧供给(EnablePixelEffect=false 时管线不跑,退化为纯粒子)
                    EffectLoader.FiendBurstCenter = NPC.Center;
                    EffectLoader.FiendBurstProgress = charge * 0.85f;
                    if (silence)
                    {
                        //静默拍:画面轻度去饱和,吸气感
                        float sp = (attackTimer - (BurstWindup - 10)) / 10f;
                        EffectLoader.FiendBurstDesat = Math.Max(EffectLoader.FiendBurstDesat, sp * 0.45f);
                    }
                }
                if (!Main.dedServ && !silence)
                {
                    //紫光渐胀:内聚粒子密度随进度爬升,末段自然让位静默
                    if (Main.rand.NextFloat() < 0.3f + charge * 0.6f)
                    {
                        Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(120f, 420f);
                        var p = PRTLoader.NewParticle<PRT_Light>(NPC.Center + offset, -offset * 0.055f, new Color(170, 80, 255), 0.5f + charge * 0.4f);
                        p.Configure(0.9f, lifetime: 18);
                    }
                    //切向环流:内聚之外再加一族绕旋粒子,汇聚有"涡"而不只有"吸"
                    if (Main.rand.NextFloat() < 0.2f + charge * 0.4f)
                    {
                        Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(150f, 380f);
                        Vector2 swirl = offset.RotatedBy(MathHelper.PiOver2).SafeNormalize(Vector2.UnitX) * (2f + charge * 4f) - offset * 0.02f;
                        PRTLoader.NewParticle<PRT_LineCal>(NPC.Center + offset, swirl, new Color(150, 90, 255), 0.7f).Configure(false, 16);
                    }
                    //安全环圣光:环内升腾的金白光尘(与全场紫涨形成庇护所对比)
                    for (int k = 0; k < 2; k++)
                    {
                        if (Main.rand.NextBool(3))
                        {
                            Vector2 rp = SafeRingPos(k) + CEUtils.randomPointInCircle(SafeRingRadius * 0.8f);
                            var holy = PRTLoader.NewParticle<PRT_Light>(rp, new Vector2(0, -Main.rand.NextFloat(1.5f, 3.5f)), new Color(255, 236, 180), 0.5f);
                            holy.Configure(0.85f, lifetime: 22);
                        }
                    }
                    //音调爬升 + 低鸣震感(charge² 涨幅)
                    if (attackTimer % 16 == 8)
                    {
                        SoundEngine.PlaySound(SoundID.Item72 with { Volume = 0.7f, Pitch = -0.5f + charge }, NPC.Center);
                    }
                    if (attackTimer % 8 == 0)
                    {
                        ScreenShaker.AddShake(new ScreenShaker.ScreenShake(Vector2.Zero,
                            Utils.Remap(Main.LocalPlayer.Distance(NPC.Center), 2200f, 700f, 0f, charge * charge * 5f)));
                    }
                }
                return;
            }
            if (attackTimer == BurstWindup + 1)
            {
                //爆发拍:全场 260,安全环内免伤(各端只结算本机玩家,镜像原版接触伤害的本机权威)
                if (!Main.dedServ)
                {
                    if (Main.LocalPlayer.Distance(NPC.Center) < 2200f)
                    {
                        CalamityEntropy.FlashEffectStrength = 0.55f;
                    }
                    //冲击帧:全屏去饱和 + 对比度一瞬拉满,EffectLoader 侧 10 帧内自衰
                    EffectLoader.FiendBurstCenter = NPC.Center;
                    EffectLoader.FiendBurstProgress = 0f;
                    EffectLoader.FiendBurstDesat = 1f;
                    EffectLoader.FiendBurstContrast = 0.4f;
                    SoundEngine.PlaySound(SoundID.Item14 with { Volume = 1.2f, Pitch = -0.5f }, NPC.Center);
                    SoundEngine.PlaySound(SoundID.ForceRoarPitched with { Volume = 0.9f, Pitch = -0.2f }, NPC.Center);
                    ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.ScreenShake(Vector2.Zero, 16), Main.LocalPlayer.Distance(NPC.Center), 2600);
                    PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, new Color(190, 100, 255), 0.1f).Configure(9f, 50);
                    PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, Color.White, 0.1f).Configure(6f, 40);
                    for (int i = 0; i < 90; i++)
                    {
                        var v = PRTLoader.NewParticle<PRT_Void>(NPC.Center, CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(3f, 16f), Color.White, 1f);
                        v.Opacity = Main.rand.Next(30, 100) * 0.01f;
                    }
                    //放射光涌:全向光线喷出,近快远慢两层
                    for (int i = 0; i < 26; i++)
                    {
                        Vector2 dir = CEUtils.randomRot().ToRotationVector2();
                        PRTLoader.NewParticle<PRT_LineCal>(NPC.Center + dir * 30f, dir * Main.rand.NextFloat(10f, 30f),
                            i % 3 == 0 ? Color.White : new Color(180, 110, 255), Main.rand.NextFloat(0.8f, 1.5f)).Configure(false, 22);
                    }

                    Player lp = Main.LocalPlayer;
                    if (!lp.dead && lp.active && lp.Distance(NPC.Center) < BurstRadius && !InSafeRing(lp.Center))
                    {
                        int damage = (int)(NPC.defDamage * (260f / 220f) + 0.5f);
                        lp.Hurt(PlayerDeathReason.ByNPC(NPC.whoAmI), damage, 0);
                    }
                }
                return;
            }
            //虚脱 60t(§3.2:最大可打窗口),缓慢下坠垂翼 + 体表余烬散溢
            NPC.velocity = new Vector2(0, 0.4f);
            if (!Main.dedServ && attackTimer % 4 == 0)
            {
                PRTLoader.NewParticle<PRT_HeavySmokeCal>(NPC.Center + CEUtils.randomPointInCircle(50),
                    new Vector2(Main.rand.NextFloat(-0.6f, 0.6f), -Main.rand.NextFloat(0.5f, 1.5f)),
                    new Color(110, 80, 190), Main.rand.NextFloat(0.5f, 0.9f)).Configure(0.5f, 26, 0, false, 0, true);
            }
            if (attackTimer >= BurstTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                SwitchState(FiendState.Hover);
            }
        }

        private bool InSafeRing(Vector2 pos)
        {
            for (int k = 0; k < 2; k++)
            {
                if (pos.Distance(SafeRingPos(k)) < SafeRingRadius)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>环形水晶(蜕晶 8 枚 / 俯冲收尾 12 枚,ai0=1 环形模式)。仅服务端调用。</summary>
        private void SpawnCrystalRing(Vector2 center, int count, float speed)
        {
            int damage = (int)(NPC.defDamage * 0.387f + 0.5f);
            float baseRot = CEUtils.randomRot();
            for (int i = 0; i < count; i++)
            {
                Vector2 dir = (baseRot + MathHelper.TwoPi * i / count).ToRotationVector2();
                Projectile.NewProjectile(NPC.GetSource_FromAI(), center, dir * speed,
                    ModContent.ProjectileType<VoidCrystal>(), damage, 2f, -1, 1f);
            }
        }

        //———死亡演出(§3.2):过曝发白 → 翼手逐个碎晶剥离 → 核心爆裂(白闪+震屏)→ 真死 → SetVictory———
        private void DeathAI()
        {
            NPC.dontTakeDamage = true;
            NPC.damage = 0;
            NPC.velocity *= 0.9f;

            if (attackTimer == 1)
            {
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.Roar with { Volume = 0.9f, Pitch = -0.55f }, NPC.Center);
                }
                //死亡清弹(公平阀,镜像深渊亡魂清光球):在场水晶全灭,牢笼由自身的 InDeathAnim 中断
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int crystalType = ModContent.ProjectileType<VoidCrystal>();
                    foreach (Projectile p in Main.ActiveProjectiles)
                    {
                        if (p.type == crystalType)
                        {
                            p.Kill();
                        }
                    }
                }
            }
            //部件剥离:崩离瞬间碎晶爆散(锚点与 PreDraw 一致),躯干在翼断时下沉一记(重量反作用)
            if (attackTimer == 40 || attackTimer == 70)
            {
                NPC.velocity.Y += 0.9f;
            }
            if (!Main.dedServ)
            {
                if (attackTimer == 40)
                    ShatterPart(WingAnchor(-1));
                if (attackTimer == 70)
                    ShatterPart(WingAnchor(1));
                if (attackTimer == 100)
                    ShatterPart(HandPos(0));
                if (attackTimer == 115)
                    ShatterPart(HandPos(1));
                //坠落中的部件拖出烟尾(位置与 PreDraw 的剥离曲线同源)
                SpawnFallingPartTrail(40, WingAnchor(-1), -1);
                SpawnFallingPartTrail(70, WingAnchor(1), 1);
                SpawnFallingPartTrail(100, HandPos(0), -1);
                SpawnFallingPartTrail(115, HandPos(1), 1);
                if (attackTimer == DeathBlast)
                {
                    if (Main.LocalPlayer.Distance(NPC.Center) < 2200f)
                    {
                        CalamityEntropy.FlashEffectStrength = 0.6f;
                    }
                    //核心爆裂冲击帧:复用熵爆全屏管线(去饱和 + 对比度,自衰)
                    EffectLoader.FiendBurstCenter = NPC.Center;
                    EffectLoader.FiendBurstDesat = 1f;
                    EffectLoader.FiendBurstContrast = 0.35f;
                    SoundEngine.PlaySound(SoundID.Item14 with { Volume = 1.2f }, NPC.Center);
                    ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.ScreenShake(Vector2.Zero, 18), Main.LocalPlayer.Distance(NPC.Center), 2600);
                    PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, new Color(200, 130, 255), 0.1f).Configure(8f, 46);
                    PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, Color.White, 0.1f).Configure(5f, 36);
                    for (int i = 0; i < 100; i++)
                    {
                        var v = PRTLoader.NewParticle<PRT_Void>(NPC.Center, CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(2f, 14f), Color.White, 1f);
                        v.Opacity = Main.rand.Next(30, 100) * 0.01f;
                    }
                    //核心爆裂放射晶屑
                    for (int i = 0; i < 18; i++)
                    {
                        Vector2 dir = CEUtils.randomRot().ToRotationVector2();
                        PRTLoader.NewParticle<PRT_CrystalGlow>(NPC.Center + dir * 20f, dir * Main.rand.NextFloat(3f, 11f),
                            new Color(190, 150, 255), Main.rand.NextFloat(0.35f, 0.7f)).Configure(0.9f, true, PRTDrawModeEnum.AdditiveBlend, CEUtils.randomRot(), 30);
                    }
                }
            }
            if (attackTimer >= DeathTotal && Main.netMode != NetmodeID.MultiplayerClient)
            {
                NPC.StrikeInstantKill();
                NPC.netSpam = 9;
                NPC.netUpdate = true;
            }
        }

        private void ShatterPart(Vector2 pos)
        {
            SoundEngine.PlaySound(SoundID.Item27 with { Volume = 0.9f, Pitch = -0.35f }, pos);
            for (int i = 0; i < 26; i++)
            {
                var v = PRTLoader.NewParticle<PRT_Void>(pos, CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(1f, 8f), Color.White, 1f);
                v.Opacity = Main.rand.Next(30, 100) * 0.01f;
            }
            for (int i = 0; i < 10; i++)
            {
                Dust.NewDust(pos - new Vector2(20, 20), 40, 40, ModContent.DustType<Dusts.GlassBreak>());
            }
        }

        /// <summary>
        /// 死亡剥离曲线(双端由 attackTimer 确定性推导):
        /// 裂纹预闪(T-10 起)→ 崩离(T,ShatterPart 落拍)→ 悬停一拍 14t(重量的静默)→ 坠落自旋渐隐 46t。
        /// 返回:位置偏移 / 附加旋转 / 透明度 / 预闪强度 / 是否仍可见。drift = 左右漂移方向。
        /// </summary>
        private static (Vector2 ofs, float rotOfs, float alpha, float crackFlash, bool visible) DeathPartLerp(int detachTick, int dt, int drift)
        {
            if (dt < detachTick - 10)
            {
                return (Vector2.Zero, 0f, 1f, 0f, true);
            }
            if (dt < detachTick)
            {
                float f = (dt - (detachTick - 10)) / 10f;
                return (Vector2.Zero, 0f, 1f, (float)Math.Sin(f * MathHelper.Pi), true);
            }
            int t = dt - detachTick;
            if (t < 14)
            {
                //悬停一拍:轻微上浮 + 摇晃,坠落前的静默
                float p = t / 14f;
                return (new Vector2(drift * p * 2f, -7f * (float)Math.Sin(p * MathHelper.PiOver2)), drift * 0.06f * (float)Math.Sin(p * MathHelper.TwoPi), 1f, 0f, true);
            }
            float tt = t - 14;
            if (tt >= 46f)
            {
                return (Vector2.Zero, 0f, 0f, 0f, false);
            }
            //坠落:重力 + 侧漂 + 渐增自旋,渐隐
            float fallA = 1f - tt / 46f;
            return (new Vector2(drift * (2f + tt * 0.5f), -7f + tt * tt * 0.16f), drift * (0.06f + tt * 0.022f), fallA, 0f, true);
        }

        /// <summary>坠落中的部件烟尾(仅客户端调用;曲线与 DeathPartLerp 同源)</summary>
        private void SpawnFallingPartTrail(int detachTick, Vector2 basePos, int drift)
        {
            (Vector2 ofs, _, float partAlpha, _, bool visible) = DeathPartLerp(detachTick, attackTimer, drift);
            if (!visible || attackTimer < detachTick + 14 || partAlpha <= 0f || !Main.rand.NextBool(2))
            {
                return;
            }
            PRTLoader.NewParticle<PRT_HeavySmokeCal>(basePos + ofs + CEUtils.randomPointInCircle(14),
                new Vector2(0, -0.5f), new Color(120, 80, 200), Main.rand.NextFloat(0.4f, 0.7f)).Configure(0.55f * partAlpha, 18, 0, false, 0, true);
        }

        public override bool CheckDead()
        {
            //镜像深渊亡魂:死亡演出未播完前回填 1 血锁活(真死只在 DeathAI 末尾的 StrikeInstantKill)
            if (State == FiendState.Death && attackTimer >= DeathBlast)
            {
                return true;
            }
            NPC.damage = 0;
            NPC.life = 1;
            NPC.dontTakeDamage = true;
            NPC.active = true;
            NPC.netUpdate = true;
            if (NPC.netSpam >= 10)
                NPC.netSpam = 9;
            return false;
        }

        public override void OnKill()
        {
            //真死(非脱战)→ 事件胜利(§1.2)。OnKill 仅服务端/单人触发,结算天然权威端
            Events.VoidInvasion.SetVictory();
        }

        public override void ModifyNPCLoot(NPCLoot npcLoot)
        {
            //§5.1 胜利结算战利品:魂髓 10~15 必掉 + 首杀额外 +10。
            //掉落先于 OnKill 落旗标结算(镜像 NihilityTwin 首杀传记的时序假设),条件可直接读 downed
            npcLoot.Add(ItemDropRule.Common(ModContent.ItemType<Items.WraithSoulEssence>(), 1, 10, 15));
            npcLoot.Add(ItemDropRule.ByCondition(new FiendFirstKill(), ModContent.ItemType<Items.WraithSoulEssence>(), 1, 10, 10));
            //§5.4 掠食者杖补给:20%(tML 无"世界未拥有"原生条件,按纯 20% 落地并在交付报告注明)
            npcLoot.Add(ItemDropRule.Common(ModContent.ItemType<Items.Weapons.VoidInvasion.VoidPredatorStaff>(), 5));
        }

        //首杀条件:事件旗标未置位时多给一份魂髓(§5.1)
        private class FiendFirstKill : IItemDropRuleCondition, IProvideItemConditionDescription
        {
            public bool CanDrop(DropAttemptInfo info) => !EDownedBosses.downedVoidInvasion;
            public bool CanShowItemDropInUI() => true;
            public string GetConditionDescription() => null;
        }

        //———程序化组装绘制———

        /// <summary>翼根锚点(§3.2:锚肩点)。dir = -1 左翼 / 1 右翼。</summary>
        private Vector2 WingAnchor(int dir)
        {
            return NPC.Center + new Vector2(dir * 34f, -16f).RotatedBy(NPC.rotation);
        }

        /// <summary>浮游手位置:绕躯干椭圆轨道,施法时收拢胸前(castGather 插值)。k = 0 左 / 1 右。</summary>
        private Vector2 HandPos(int k)
        {
            float phase = handOrbit + k * MathHelper.Pi;
            Vector2 orbit = NPC.Center + new Vector2((float)Math.Cos(phase) * 118f, (float)Math.Sin(phase * 2f) * 24f + 26f);
            Vector2 chest = NPC.Center + new Vector2((k == 0 ? -1 : 1) * 30f, 22f);
            return Vector2.Lerp(orbit, chest, castGather);
        }

        /// <summary>入场部件飞入偏移与透明度(attackTimer 推导,双端一致)。partIndex:0 躯体 1 左翼 2 右翼 3 左手 4 右手。</summary>
        private (Vector2 offset, float alpha) EntrancePartLerp(int partIndex)
        {
            if (State != FiendState.Entrance)
            {
                return (Vector2.Zero, 1f);
            }
            if (attackTimer <= EntrancePulse)
            {
                return (Vector2.Zero, 0f);
            }
            float p = (attackTimer - EntrancePulse) / (float)(EntranceTotal - EntrancePulse);
            float pp = MathHelper.Clamp(p * 1.9f - partIndex * 0.18f, 0f, 1f);
            float ease = 1f - (1f - pp) * (1f - pp);
            Vector2 flyFrom = partIndex switch
            {
                1 => new Vector2(-520f, -160f),
                2 => new Vector2(520f, -160f),
                3 => new Vector2(-360f, 300f),
                4 => new Vector2(360f, 300f),
                _ => new Vector2(0f, -560f),
            };
            return (flyFrom * (1f - ease), pp);
        }

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (NPC.IsABestiaryIconDummy)
            {
                return true;
            }
            Texture2D body = TextureAssets.Npc[NPC.type].Value;
            Texture2D wingL = wingLTex.Value;
            Texture2D wingR = wingRTex.Value;
            Texture2D handL = handLTex.Value;
            Texture2D handR = handRTex.Value;
            SpriteBatch sb = Main.spriteBatch;
            float opacity = NPC.Opacity; //事件清场渐隐走 npc.alpha

            //死亡演出的部件存活与过曝白
            bool deathAnim = State == FiendState.Death;
            int dt = deathAnim ? attackTimer : 0;
            float overexpose = deathAnim ? MathHelper.Clamp(dt / 40f, 0f, 1f) * 0.75f : 0f;
            Vector2 jitter = deathAnim ? CEUtils.randomPointInCircle(1 + (int)(dt / 40f)) : Vector2.Zero;
            if (deathAnim && dt >= DeathBlast)
            {
                //核心爆裂后本体不再绘制(白闪与粒子接管收尾)
                return false;
            }

            //俯冲拖尾(速度门控:只在冲刺拍出现):预乘 alpha=0 即加法,能量残影渐细渐暗
            if (NPC.velocity.Length() > 22f && odp.Count > 1 && !deathAnim)
            {
                for (int i = 0; i < odp.Count - 1; i++)
                {
                    float fade = (i + 1f) / odp.Count;
                    sb.Draw(body, odp[i] + jitter - screenPos, null, new Color(130, 80, 255, 0) * (fade * fade * 0.55f * opacity), NPC.rotation, body.Size() / 2, NPC.scale * (0.7f + 0.3f * fade), SpriteEffects.None, 0);
                }
            }

            //翼:扑翼摆角 ±22° + 纵向 0.9~1.1 压缩(周期 40t);俯冲后掠锁定,虚脱下垂(wingSweep 插值)
            float flap = (float)Math.Sin(flapCounter) * MathHelper.ToRadians(22);
            float squash = 1f + (float)Math.Cos(flapCounter) * 0.1f;
            float sweptRot = MathHelper.ToRadians(58);
            float spreadRot = MathHelper.ToRadians(-14);
            for (int dir = -1; dir <= 1; dir += 2)
            {
                //死亡剥离曲线:预闪 → 崩离 → 悬停一拍 → 坠落(替代旧的"直接消失")
                int detachTick = dir == -1 ? 40 : 70;
                (Vector2 deathOfs, float deathRot, float deathAlpha, float crackFlash, bool visible) =
                    deathAnim ? DeathPartLerp(detachTick, dt, dir) : (Vector2.Zero, 0f, 1f, 0f, true);
                if (!visible)
                {
                    continue;
                }
                int partIndex = dir == -1 ? 1 : 2;
                (Vector2 flyOfs, float partAlpha) = EntrancePartLerp(partIndex);
                if (partAlpha <= 0.01f)
                {
                    continue;
                }
                Texture2D wing = dir == -1 ? wingL : wingR;
                float wingRot = flap;
                if (wingSweep > 0)
                {
                    wingRot = MathHelper.Lerp(flap, sweptRot, wingSweep);
                }
                else if (wingSweep < 0)
                {
                    wingRot = MathHelper.Lerp(flap, spreadRot, -wingSweep / 0.4f);
                }
                //蜕晶余韵:翼震颤(高频小幅抖)
                if (wingShake > 0)
                {
                    wingRot += (float)Math.Sin(Main.GlobalTimeWrappedHourly * 70f + dir * 2f) * 0.13f * wingShake;
                }
                //翼根在内缘锚点,外端随摆角挥动;右翼水平镜像
                Vector2 anchor = WingAnchor(dir) + flyOfs + jitter + deathOfs;
                Vector2 origin = dir == -1 ? new Vector2(wing.Width - 12, wing.Height / 2f + 20) : new Vector2(12, wing.Height / 2f + 20);
                SpriteEffects fx = dir == -1 ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
                float rot = NPC.rotation + dir * wingRot + deathRot;
                float wa = opacity * partAlpha * deathAlpha;
                Color wc = drawColor * wa;
                sb.Draw(wing, anchor - screenPos, null, wc, rot, origin, new Vector2(1f, squash) * NPC.scale, fx, 0);
                //白閃叠层:死亡过曝 / 剥离预闪 / 蜕晶碎裂纹共用(取最强者)
                float whiteA = Math.Max(overexpose, Math.Max(crackFlash * 0.85f, moltFlash * 0.65f));
                if (whiteA > 0)
                {
                    sb.Draw(wing, anchor - screenPos, null, Color.White * (whiteA * partAlpha * deathAlpha), rot, origin, new Vector2(1f, squash) * NPC.scale, fx, 0);
                }
                //入场能量壳:飞行中的部件裹一层紫光(预乘 alpha=0 加法),落位后消散
                if (State == FiendState.Entrance && partAlpha < 0.98f)
                {
                    sb.Draw(wing, anchor - screenPos, null, new Color(150, 90, 255, 0) * ((1f - partAlpha) * 0.9f), rot, origin, new Vector2(1f, squash) * NPC.scale, fx, 0);
                }
            }

            //躯体
            (Vector2 bodyOfs, float bodyAlpha) = EntrancePartLerp(0);
            if (bodyAlpha > 0.01f)
            {
                Vector2 bodyPos = NPC.Center + bodyOfs + jitter - screenPos;
                sb.Draw(body, bodyPos, null, drawColor * (opacity * bodyAlpha), NPC.rotation, body.Size() / 2, NPC.scale, SpriteEffects.None, 0);
                if (overexpose > 0)
                {
                    float swell = dt >= 130 ? 1f + (dt - 130) / 20f * 0.25f : 1f;
                    sb.Draw(body, bodyPos, null, Color.White * overexpose, NPC.rotation, body.Size() / 2, NPC.scale * swell, SpriteEffects.None, 0);
                }
            }

            //浮游手(前层)
            for (int k = 0; k < 2; k++)
            {
                int detachTick = k == 0 ? 100 : 115;
                int drift = k == 0 ? -1 : 1;
                (Vector2 deathOfs, float deathRot, float deathAlpha, float crackFlash, bool visible) =
                    deathAnim ? DeathPartLerp(detachTick, dt, drift) : (Vector2.Zero, 0f, 1f, 0f, true);
                if (!visible)
                {
                    continue;
                }
                (Vector2 flyOfs, float partAlpha) = EntrancePartLerp(3 + k);
                if (partAlpha <= 0.01f)
                {
                    continue;
                }
                Texture2D hand = k == 0 ? handL : handR;
                Vector2 pos = HandPos(k) + flyOfs + jitter + deathOfs;
                float rot = (float)Math.Sin(handOrbit * 2f + k * 2f) * 0.2f + castGather * (k == 0 ? 0.5f : -0.5f) + deathRot;
                float ha = opacity * partAlpha * deathAlpha;
                //手底辉光:浮游感的光学锚(预乘 alpha=0 加法)
                Texture2D handGlow = CEExtraAssets.Glow2;
                sb.Draw(handGlow, pos - screenPos, null, new Color(140, 90, 255, 0) * (0.4f * ha * (0.8f + castGather * 0.5f)), 0, handGlow.Size() / 2, 0.5f, SpriteEffects.None, 0);
                sb.Draw(hand, pos - screenPos, null, drawColor * ha, rot, hand.Size() / 2, NPC.scale, k == 1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0);
                float whiteA = Math.Max(overexpose, crackFlash * 0.85f);
                if (whiteA > 0)
                {
                    sb.Draw(hand, pos - screenPos, null, Color.White * (whiteA * partAlpha * deathAlpha), rot, hand.Size() / 2, NPC.scale, k == 1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0);
                }
                if (State == FiendState.Entrance && partAlpha < 0.98f)
                {
                    sb.Draw(hand, pos - screenPos, null, new Color(150, 90, 255, 0) * ((1f - partAlpha) * 0.9f), rot, hand.Size() / 2, NPC.scale, k == 1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0);
                }
            }

            //扇射前摇:胸前凝晶预览(因果拍:玩家看着晶体成形,再在发射拍炸开成扇)
            if (State == FiendState.CrystalFan && attackTimer <= FanWindup + 4 && !deathAnim)
            {
                Texture2D ct = crystalTex.Value;
                float fp = MathHelper.Clamp(attackTimer / (float)FanWindup, 0f, 1f);
                float pop = attackTimer > FanWindup - 4 ? MathHelper.Clamp((attackTimer - (FanWindup - 4)) / 8f, 0f, 1f) : 0f;
                Vector2 chest = NPC.Center + new Vector2(0, 14);
                float cScale = (0.4f + fp * 0.9f) * (1f + pop * 0.6f);
                float cAlpha = fp * 0.85f * (1f - pop) * opacity;
                sb.Draw(ct, chest - screenPos, null, new Color(170, 120, 255, 0) * cAlpha, Main.GlobalTimeWrappedHourly * 3f, ct.Size() / 2, cScale, SpriteEffects.None, 0);
                sb.Draw(ct, chest - screenPos, null, new Color(255, 255, 255, 0) * (cAlpha * 0.6f), -Main.GlobalTimeWrappedHourly * 2.2f, ct.Size() / 2, cScale * 0.7f, SpriteEffects.None, 0);
            }

            DrawStateOverlays(sb, screenPos);
            return false;
        }

        /// <summary>状态叠加层:牢笼前摇的周身法阵、熵爆的安全环与本体聚光(加法批次)。</summary>
        private void DrawStateOverlays(SpriteBatch sb, Vector2 screenPos)
        {
            bool cageGlyph = State == FiendState.CrystalCage && attackTimer <= CageWindup;
            bool burstRings = State == FiendState.EntropyBurst && attackTimer <= BurstWindup + 8;
            if (!cageGlyph && !burstRings)
            {
                return;
            }
            Texture2D glyph = glyphTex.Value;
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);

            if (cageGlyph)
            {
                //周身法阵:随前摇渐大渐亮,双层反旋 + 周期收束的吸入环(把"要收拢"讲在前面)
                float p = attackTimer / (float)CageWindup;
                float spin = attackTimer * 0.05f;
                sb.Draw(glyph, NPC.Center - screenPos, null, new Color(180, 100, 255) * (0.8f * p), spin, glyph.Size() / 2, 0.6f + p * 0.5f, SpriteEffects.None, 0);
                sb.Draw(glyph, NPC.Center - screenPos, null, new Color(110, 40, 210) * (0.6f * p), -spin * 0.7f, glyph.Size() / 2, 0.4f + p * 0.35f, SpriteEffects.None, 0);
                Texture2D suck = CEExtraAssets.HollowCircleSoftEdge;
                float cyc = attackTimer % 22 / 22f;
                float suckR = MathHelper.Lerp(340f, 50f, cyc);
                sb.Draw(suck, NPC.Center - screenPos, null, new Color(150, 100, 255) * (p * 0.55f * (1f - cyc)), 0, suck.Size() / 2, suckR * 2f / suck.Width, SpriteEffects.None, 0);
            }
            if (burstRings)
            {
                float charge = MathHelper.Clamp(attackTimer / (float)BurstWindup, 0f, 1f);
                bool inSilence = attackTimer > BurstWindup - 10 && attackTimer <= BurstWindup;
                float silenceP = inSilence ? (attackTimer - (BurstWindup - 10)) / 10f : 0f;
                Texture2D lb = CEExtraAssets.lightball;
                for (int k = 0; k < 2; k++)
                {
                    //安全环 = 圣所:金白基调与全场紫涨对比,静默拍反而更亮(避难指向拉满)
                    Vector2 pos = SafeRingPos(k) - screenPos;
                    float pulse = 1f + 0.08f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 9f + k * 2f);
                    float holyA = 0.5f + charge * 0.4f + silenceP * 0.35f;
                    sb.Draw(glyph, pos, null, new Color(255, 226, 150) * holyA, attackTimer * 0.03f, glyph.Size() / 2, 0.5f * pulse, SpriteEffects.None, 0);
                    sb.Draw(glyph, pos, null, Color.White * (0.3f + charge * 0.3f + silenceP * 0.25f), -attackTimer * 0.02f, glyph.Size() / 2, 0.36f * pulse, SpriteEffects.None, 0);
                    //环心圣光柱 + 地面光晕:远处也读得到"那里能活"
                    sb.Draw(lb, pos, null, new Color(255, 240, 190) * (0.3f * holyA), 0, lb.Size() / 2, new Vector2(0.75f, 5.4f), SpriteEffects.None, 0);
                    sb.Draw(lb, pos, null, Color.White * (0.22f * holyA), 0, lb.Size() / 2, new Vector2(0.3f, 4.6f), SpriteEffects.None, 0);
                    sb.Draw(lb, pos, null, new Color(255, 236, 170) * (0.35f * holyA * pulse), 0, lb.Size() / 2, new Vector2(2.4f, 0.55f), SpriteEffects.None, 0);
                }
                //本体聚光:渐胀;静默拍坍缩到四成并轻颤(爆前收缩,§3.2 静默拍)
                float collapse = inSilence ? MathHelper.Lerp(1f, 0.4f, silenceP) * (0.93f + 0.07f * (float)Math.Cos(attackTimer * 1.4f)) : 1f;
                sb.Draw(glyph, NPC.Center - screenPos, null, new Color(170, 80, 255) * (0.7f * charge), attackTimer * 0.06f, glyph.Size() / 2, (0.3f + charge * 0.6f) * collapse, SpriteEffects.None, 0);
                sb.Draw(lb, NPC.Center - screenPos, null, new Color(190, 110, 255) * (0.55f * charge), 0, lb.Size() / 2, (0.9f + charge * 1.3f) * collapse, SpriteEffects.None, 0);
                //收束吸入环:半径随蓄力收紧,速率随进度加快
                Texture2D suck = CEExtraAssets.HollowCircleSoftEdge;
                float cyc = attackTimer % 18 / 18f;
                float suckR = MathHelper.Lerp(560f - charge * 180f, 60f, cyc);
                sb.Draw(suck, NPC.Center - screenPos, null, new Color(170, 100, 255) * (charge * 0.6f * (1f - cyc) * (1f - silenceP)), 0, suck.Size() / 2, suckR * 2f / suck.Width, SpriteEffects.None, 0);
            }

            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
        }
    }
}
