using CalamityEntropy.Common;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using CalamityEntropy.Content.Projectiles.AbyssalWraithProjs;
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
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.NPCs.VoidInvasion
{
    /// <summary>
    /// 虚空术士(void-invasion.md §2.2):骚扰层辅助核心,继承教徒状态机(自动计入召唤仪式人数),
    /// attackAI 重写为施法循环:治疗(CD 12s)> 狂暴(CD 18s)> 攻击兜底,优先级仅服务端判定,施法期间停走。
    /// 杀死术士即打断吟唱(NPC 消失,弹幕未生成,状态机天然回收)。
    /// </summary>
    public class VoidCultistWarlock : VoidCultist, IVoidInvasionNPC
    {
        public override string Texture => "CalamityEntropy/Content/NPCs/VoidInvasion/Warlock/bodyIdle";

        //部件贴图只在绘制路径读取(服务器恒 null)
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Warlock/bodyCast")]
        private static Asset<Texture2D> bodyCastTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Warlock/staff")]
        private static Asset<Texture2D> staffTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Warlock/handLeft")]
        private static Asset<Texture2D> warlockHandL;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Warlock/handRight")]
        private static Asset<Texture2D> warlockHandR;
        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light2")]
        private static Asset<Texture2D> spellGlowTex;

        //咒术节拍常量(§2.2)
        private const int HealChant = 45;
        private const int RageChant = 30;
        private const int AtkWindup = 40;
        private const int AtkRecover = 25;
        private const int HealCdMax = 12 * 60;
        private const int RageCdMax = 18 * 60;
        private const float SpellRange = 700f;

        /// <summary>当前咒术:0=未选 1=治疗 2=狂暴 3=攻击(SendExtraAI 同步)</summary>
        public byte spellType = 0;
        public int spellTimer = 0;
        /// <summary>治疗光灵目标(3×NPC whoAmI,255=空位;服务端选取后同步)</summary>
        public byte[] healTargets = new byte[3] { 255, 255, 255 };
        //内置 CD 仅服务端选招时读,不进同步
        private int healCd = 0;
        private int rageCd = 0;
        //行走摇摆相位(纯客户端视觉)
        private float walkPhase = 0;

        public override void SetStaticDefaults()
        {
            NPCID.Sets.MPAllowedEnemies[Type] = true;
        }

        public override void SetBestiary(Terraria.GameContent.Bestiary.BestiaryDatabase database, Terraria.GameContent.Bestiary.BestiaryEntry bestiaryEntry)
        {
            bestiaryEntry.Info.AddRange(new Terraria.GameContent.Bestiary.IBestiaryInfoElement[]
            {
                new Terraria.GameContent.Bestiary.FlavorTextBestiaryInfoElement("Mods.CalamityEntropy.VoidCultistWarlockBestiary")
            });
        }

        public override void SetDefaults()
        {
            base.SetDefaults();
            NPC.lifeMax = 100000;
            NPC.defense = 80;
            NPC.damage = 120;
            if (Main.expertMode)
            {
                NPC.damage += 20;
            }
            if (Main.masterMode)
            {
                NPC.damage += 20;
            }
            //骚扰层金币压低(§5.1);教徒现值已在 M9 对齐本档
            NPC.value = Item.buyPrice(0, 0, 2, 0);
            walking.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/NPCs/VoidInvasion/Warlock/bodyIdle").Value);
        }

        //基类 Summoning 分支的部件挂点直接复用(bodyCast + 双手)
        public override Texture2D BodyTex => bodyCastTex.Value;
        public override Texture2D LeftHandTex => warlockHandL.Value;
        public override Texture2D RightHandTex => warlockHandR.Value;

        /// <summary>杖头世界坐标(弹幕发射点与聚球粒子锚点;不读贴图,服务端可用)</summary>
        public Vector2 StaffTip => NPC.Center + new Vector2(NPC.direction * 16f, -40f);

        public override void SendExtraAI(BinaryWriter writer)
        {
            base.SendExtraAI(writer);
            writer.Write(spellType);
            writer.Write(spellTimer);
            writer.Write(healTargets[0]);
            writer.Write(healTargets[1]);
            writer.Write(healTargets[2]);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            base.ReceiveExtraAI(reader);
            spellType = reader.ReadByte();
            spellTimer = reader.ReadInt32();
            healTargets[0] = reader.ReadByte();
            healTargets[1] = reader.ReadByte();
            healTargets[2] = reader.ReadByte();
        }

        /// <summary>事件家族判定(治疗/狂暴共用):教徒系或挂标记接口的事件怪。</summary>
        //TODO M5/M6:裂隙恶灵/虚熵魔物实装挂 IVoidInvasionNPC 后在此按类型排除(小 Boss 与教皇不吃辅助,§2.2)
        private static bool IsEventNPC(NPC n)
        {
            return n.active && (n.ModNPC is VoidCultist || n.ModNPC is IVoidInvasionNPC);
        }

        public override void attackAI()
        {
            //施法期间停走(§2.2),面向目标
            NPC.velocity.X *= 0.86f;
            if (NPC.HasValidTarget)
            {
                NPC.direction = Target.Center.X > NPC.Center.X ? 1 : -1;
            }

            if (spellType == 0)
            {
                //优先级判定仅服务端,客户端等 netUpdate 同步
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    ChooseSpell();
                }
                return;
            }

            spellTimer++;
            if (spellTimer == 1 && !Main.dedServ)
            {
                SoundEngine.PlaySound(SoundID.Item8, NPC.Center);
            }
            switch (spellType)
            {
                case 1: HealSpellAI(); break;
                case 2: RageSpellAI(); break;
                default: AttackSpellAI(); break;
            }
        }

        /// <summary>服务端选招(§2.2 优先级:治疗 > 狂暴 > 攻击兜底)。</summary>
        private void ChooseSpell()
        {
            spellTimer = 0;
            if (healCd <= 0 && FindHealTargets())
            {
                spellType = 1;
            }
            else if (rageCd <= 0 && CountEventNPCsAround() >= 3)
            {
                spellType = 2;
            }
            else
            {
                spellType = 3;
            }
            NPC.netUpdate = true;
        }

        /// <summary>
        /// 治疗目标选取(仅服务端):触发条件 = 700px 内存在血量<60% 的事件怪;
        /// 目标 = 含自己在内血量百分比最低的三只(§2.2)。
        /// </summary>
        private bool FindHealTargets()
        {
            healTargets[0] = healTargets[1] = healTargets[2] = 255;
            bool trigger = false;
            List<NPC> pool = new();
            foreach (NPC n in Main.npc)
            {
                if (!IsEventNPC(n) || n.Center.Distance(NPC.Center) > SpellRange)
                    continue;
                if (n.life < n.lifeMax)
                    pool.Add(n);
                if (n.life < n.lifeMax * 0.6f)
                    trigger = true;
            }
            if (!trigger)
                return false;
            pool.Sort((a, b) => (a.life / (float)a.lifeMax).CompareTo(b.life / (float)b.lifeMax));
            for (int i = 0; i < 3 && i < pool.Count; i++)
            {
                healTargets[i] = (byte)pool[i].whoAmI;
            }
            return true;
        }

        private int CountEventNPCsAround()
        {
            int count = 0;
            foreach (NPC n in Main.npc)
            {
                if (n.whoAmI != NPC.whoAmI && IsEventNPC(n) && n.Center.Distance(NPC.Center) < SpellRange)
                    count++;
            }
            return count;
        }

        private void HealSpellAI()
        {
            HealChantVisuals();
            if (spellTimer < HealChant)
                return;
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (healTargets[i] == 255)
                        continue;
                    NPC t = Main.npc[healTargets[i]];
                    if (!t.active)
                        continue;
                    //向上散出后再各自追踪目标
                    Vector2 vel = new Vector2(Main.rand.NextFloat(-3f, 3f), -Main.rand.NextFloat(4f, 7f));
                    Projectile.NewProjectile(NPC.GetSource_FromAI(), StaffTip, vel,
                        ModContent.ProjectileType<HealWisp>(), 0, 0, -1, healTargets[i]);
                }
            }
            healCd = HealCdMax;
            if (!Main.dedServ)
            {
                SoundEngine.PlaySound(SoundID.Item29, NPC.Center);
                //放灵拍:杖头绿闪 + 升环
                var flash = PRTLoader.NewParticle<PRT_BloomCal>(StaffTip, Vector2.Zero, new Color(150, 255, 180), 0.25f);
                flash.Configure(1.1f, 12);
                var ring = PRTLoader.NewParticle<PRT_PulseRing>(StaffTip, new Vector2(0, -1.5f), new Color(120, 255, 160), 0.1f);
                ring.Configure(1f, 16);
            }
            FinishSpell();
        }

        private void RageSpellAI()
        {
            RageChantVisuals();
            if (spellTimer < RageChant)
                return;
            ApplyRage();
            rageCd = RageCdMax;
            if (!Main.dedServ)
            {
                SoundEngine.PlaySound(SoundID.Item62, NPC.Center);
                //爆发拍:大红环 + 红闪(咒术出手与波及范围一次讲清)
                var ring = PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, new Color(255, 60, 80), 0.2f);
                ring.Configure(3.4f, 22);
                var flash = PRTLoader.NewParticle<PRT_BloomCal>(NPC.Center, Vector2.Zero, new Color(255, 90, 110), 0.3f);
                flash.Configure(1.5f, 12);
            }
            FinishSpell();
        }

        /// <summary>
        /// 上"虚狂"(§2.2):最近 5 只事件怪 voidRageTime=10s,同名只刷新时长。
        /// 各端确定性执行(位置由原生同步兜底):伤害倍率要在被击玩家本机的 ModifyHitPlayer 里读到,
        /// 单靠服务端置位在联机端不可见,故不做服务端 gate。
        /// </summary>
        private void ApplyRage()
        {
            List<NPC> list = new();
            foreach (NPC n in Main.npc)
            {
                if (IsEventNPC(n) && n.Center.Distance(NPC.Center) < SpellRange)
                    list.Add(n);
            }
            list.Sort((a, b) => a.Center.DistanceSQ(NPC.Center).CompareTo(b.Center.DistanceSQ(NPC.Center)));
            for (int i = 0; i < list.Count && i < 5; i++)
            {
                list[i].Entropy().voidRageTime = 600;
                //上狂拍:被波及者立刻可辨(红环收在本体上 + 上冲红火星),纯表现端
                if (!Main.dedServ)
                {
                    NPC t = list[i];
                    var ring = PRTLoader.NewParticle<PRT_PulseRing>(t.Center, Vector2.Zero, new Color(255, 70, 90), 0.12f);
                    ring.Configure(Math.Max(t.width, t.height) / 55f + 0.6f, 18);
                    for (int j = 0; j < 4; j++)
                    {
                        var ember = PRTLoader.NewParticle<PRT_GlowSparkCal>(t.Center + CEUtils.randomPointInCircle(t.width * 0.4f),
                            new Vector2(Main.rand.NextFloat(-1f, 1f), -Main.rand.NextFloat(2.5f, 5f)),
                            new Color(255, 80, 110), Main.rand.NextFloat(0.3f, 0.5f));
                        ember.Configure(false, 18, new Vector2(0.5f, 1.6f), quickShrink: true);
                    }
                }
            }
        }

        private void AttackSpellAI()
        {
            if (spellTimer <= AtkWindup)
            {
                AttackChantVisuals();
                if (spellTimer == AtkWindup && Main.netMode != NetmodeID.MultiplayerClient && NPC.HasValidTarget)
                {
                    //追踪法球复用深渊亡魂的 HomingLightBall(ai[2]=主人,寻的读主人 target);
                    //140 经典档 = 敌对弹幕命中 ×2,故弹幕伤害取 NPC.damage(120)×0.58≈70
                    Vector2 baseDir = (Target.Center - StaffTip).SafeNormalize(Vector2.UnitX * NPC.direction);
                    for (int i = -1; i <= 1; i++)
                    {
                        Projectile.NewProjectile(NPC.GetSource_FromAI(), StaffTip, baseDir.RotatedBy(i * 0.45f) * 9f,
                            ModContent.ProjectileType<HomingLightBall>(), (int)(NPC.damage * 0.58f), 6, -1, 0, 0, NPC.whoAmI);
                    }
                }
                if (spellTimer == AtkWindup && !Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.Item72, NPC.Center);
                    //出球拍:杖头白闪 + 沿面向的小冲击环
                    var flash = PRTLoader.NewParticle<PRT_BloomCal>(StaffTip, Vector2.Zero, Color.White, 0.25f);
                    flash.Configure(1.2f, 10);
                    var ring = PRTLoader.NewParticle<PRT_DirectionalPulseRing>(StaffTip, Vector2.Zero, new Color(190, 110, 255), 0.12f);
                    ring.Configure(new Vector2(0.5f, 1.1f), NPC.direction > 0 ? 0f : MathHelper.Pi, 1.1f, 13);
                }
                return;
            }
            if (spellTimer >= AtkWindup + AtkRecover)
            {
                FinishSpell();
            }
        }

        /// <summary>收招:清咒术态,退避一段再重新逼近(两端按同一拍到达,无需额外同步)。</summary>
        private void FinishSpell()
        {
            spellType = 0;
            spellTimer = 0;
            aiStyle = AIStyle.Avoid;
            AvoidTime = 50;
        }

        //---- 三咒术吟唱视觉(§2.2 读招区分:柱=治疗 / 环=狂暴 / 聚球=攻击),全部纯客户端 ----

        /// <summary>治疗吟唱:身周绿紫柔光柱(上升光流,DrawCastPose 里另有光柱体)。</summary>
        private void HealChantVisuals()
        {
            if (Main.dedServ)
                return;
            float grow = Math.Min(1f, spellTimer / 18f);
            for (int i = 0; i < 2; i++)
            {
                Vector2 pos = NPC.Center + new Vector2(Main.rand.NextFloat(-24f, 24f), Main.rand.NextFloat(-14f, 34f));
                Color c = Main.rand.NextBool() ? new Color(110, 255, 150) : new Color(190, 110, 255);
                var p = PRTLoader.NewParticle<PRT_Light>(pos, new Vector2(0, -Main.rand.NextFloat(2.2f, 4.2f) * (0.4f + 0.6f * grow)), c, 0.42f);
                p.Configure(0.8f, lifetime: 22);
            }
            if (Main.rand.NextBool(4))
            {
                var sp = PRTLoader.NewParticle<PRT_SparkleCal>(NPC.Center + CEUtils.randomPointInCircle(30f),
                    new Vector2(0, -1.2f), new Color(160, 255, 190), 0.5f);
                sp.Configure(new Color(110, 255, 150), 20, 0.08f, 1.1f);
            }
            Lighting.AddLight(NPC.Center, 0.15f * grow, 0.45f * grow, 0.25f * grow);
        }

        /// <summary>狂暴吟唱:红纹脉冲环 + 体表红火星(与虚狂状态的红纹语言同源)。</summary>
        private void RageChantVisuals()
        {
            if (Main.dedServ)
                return;
            //每 8t 一圈自体心外扩的红环
            if (spellTimer % 8 == 1)
            {
                var ring = PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, new Color(255, 70, 90), 0.14f);
                ring.Configure(1.7f, 15);
            }
            if (Main.rand.NextBool(2))
            {
                var ember = PRTLoader.NewParticle<PRT_GlowSparkCal>(NPC.Center + CEUtils.randomPointInCircle(26f),
                    new Vector2(Main.rand.NextFloat(-0.6f, 0.6f), -Main.rand.NextFloat(1.5f, 3f)),
                    new Color(255, 80, 110), Main.rand.NextFloat(0.25f, 0.4f));
                ember.Configure(false, 15, new Vector2(0.5f, 1.5f), quickShrink: true);
            }
            Lighting.AddLight(NPC.Center, 0.5f, 0.12f, 0.15f);
        }

        /// <summary>攻击吟唱:外圈聚线向杖头汇入,聚球在 DrawCastPose 里长大;末 6t 静默(爆发前吸气)。</summary>
        private void AttackChantVisuals()
        {
            if (Main.dedServ)
                return;
            float grow = Math.Min(1f, spellTimer / (float)AtkWindup);
            bool quiet = spellTimer > AtkWindup - 6;
            if (!quiet && spellTimer % 2 == 0)
            {
                Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(45f, 95f);
                var line = PRTLoader.NewParticle<PRT_LineCal>(StaffTip + offset, -offset * 0.13f,
                    new Color(170, 70, 255), Main.rand.NextFloat(0.4f, 0.65f));
                line.Configure(false, 12);
            }
            Lighting.AddLight(StaffTip, 0.4f * grow, 0.15f * grow, 0.6f * grow);
        }

        public override void PostAI()
        {
            if (healCd > 0)
                healCd--;
            if (rageCd > 0)
                rageCd--;
            //目标丢失等原因被基类踢出 Attack 态时,咒术态跟着清(两端确定性)
            if (aiStyle != AIStyle.Attack && spellType != 0)
            {
                spellType = 0;
                spellTimer = 0;
            }
            if (Math.Abs(NPC.velocity.Y) < 0.5f)
            {
                walkPhase += Math.Abs(NPC.velocity.X) * 0.11f;
            }
        }

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            //仪式姿态沿用基类部件组装(bodyCast + 双手摆动)
            if (aiStyle == AIStyle.Summoning)
            {
                return base.PreDraw(spriteBatch, screenPos, drawColor);
            }
            if (aiStyle == AIStyle.Attack && spellType != 0)
            {
                DrawCastPose(spriteBatch, screenPos, drawColor);
                return false;
            }
            //站立/行走:单帧 bodyIdle + 程序化 ±4° 摇摆与 2px 颠簸(§2.2,无行走帧)
            Texture2D tex = getTex();
            float moveFactor = Math.Min(1f, Math.Abs(NPC.velocity.X) / 2f);
            float sway = (float)Math.Sin(walkPhase) * MathHelper.ToRadians(4f) * moveFactor;
            float bob = Math.Abs((float)Math.Sin(walkPhase)) * 2f * moveFactor;
            Main.EntitySpriteDraw(tex, NPC.Center + drawOffset * NPC.scale - screenPos - new Vector2(0, bob), null,
                drawColor * drawAlpha, NPC.rotation + sway, tex.Size() / 2, NPC.scale,
                NPC.direction > 0 ? SpriteEffects.FlipHorizontally : SpriteEffects.None);
            return false;
        }

        /// <summary>
        /// 施法姿态组装:bodyCast 为锚,双手绕肩点举起,手杖随右手抬升(镜像基类 Summoning 分支挂法)。
        /// 抬手进度 = 前 12t 抬起,攻击咒术收招段放回。
        /// 咒术签名叠层:治疗 = 身周绿紫柔光柱;攻击 = 杖头聚球(末 6t 收缩蓄爆);狂暴的环走粒子。
        /// </summary>
        private void DrawCastPose(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            //咒术签名底层(画在身体之下)
            DrawSpellSignature(spriteBatch, screenPos);

            float p = Math.Min(1f, spellTimer / 12f);
            if (spellType == 3 && spellTimer > AtkWindup)
            {
                p = 1f - Math.Min(1f, (spellTimer - AtkWindup) / 20f);
            }
            float raise = 76f * p;
            float handRotR = NPC.direction * (4f + raise);
            float handRotL = NPC.direction * (26f + raise * 0.85f);
            Vector2 anchor = NPC.Center + drawOffset * NPC.scale - screenPos;
            SpriteEffects fx = NPC.direction > 0 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;

            //手杖:握点在杖身下 0.7 处,随右手从前倾 55° 抬到近直立(杖头即 StaffTip 弹幕点)
            Texture2D staff = staffTex.Value;
            Vector2 staffPos = anchor + new Vector2(8f * NPC.scale * NPC.direction, 6f - 8f * p);
            float staffRot = MathHelper.ToRadians(NPC.direction * (55f - 45f * p));
            spriteBatch.Draw(staff, staffPos, null, drawColor * drawAlpha, staffRot,
                new Vector2(staff.Width / 2f, staff.Height * 0.7f), NPC.scale, fx, 0);

            Texture2D handR = RightHandTex;
            Main.EntitySpriteDraw(handR, anchor + new Vector2(8f * NPC.scale * NPC.direction, 6f), null,
                drawColor * drawAlpha, NPC.rotation + MathHelper.ToRadians(180 + handRotR),
                NPC.direction > 0 ? new Vector2(handR.Width, 0) : Vector2.Zero, NPC.scale, fx);

            Texture2D body = BodyTex;
            Main.EntitySpriteDraw(body, anchor, null, drawColor * drawAlpha, NPC.rotation, body.Size() / 2, NPC.scale, fx);

            Texture2D handL = LeftHandTex;
            Main.EntitySpriteDraw(handL, anchor + new Vector2(-4f * NPC.scale * NPC.direction, 6f), null,
                drawColor * drawAlpha, NPC.rotation + MathHelper.ToRadians(180 + handRotL),
                NPC.direction < 0 ? new Vector2(handL.Width, 0) : Vector2.Zero, NPC.scale, fx);
        }

        /// <summary>
        /// 咒术签名叠层(加色批次,画完还原):
        /// 治疗 = 身周柔光柱(绿底紫芯双条);攻击 = 杖头聚球随吟唱长大、末 6t 收缩(爆发前塌缩);
        /// 狂暴只给杖头红光(环在粒子层)。
        /// </summary>
        private void DrawSpellSignature(SpriteBatch spriteBatch, Vector2 screenPos)
        {
            if (spellType == 0)
                return;
            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            Texture2D glow = spellGlowTex.Value;
            Vector2 tipPos = StaffTip - screenPos;

            if (spellType == 1)
            {
                //绿紫柔光柱:两层纵向拉长的柔光(§2.2 读招 1)
                float grow = Math.Min(1f, spellTimer / 18f);
                Vector2 basePos = NPC.Center - screenPos;
                float breathe = 1f + 0.06f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 9f);
                spriteBatch.Draw(glow, basePos, null, new Color(110, 255, 150) * (0.5f * grow), 0, glow.Size() / 2,
                    new Vector2(0.9f, 2.6f) * breathe * grow, SpriteEffects.None, 0);
                spriteBatch.Draw(glow, basePos + new Vector2(0, -12f), null, new Color(190, 110, 255) * (0.4f * grow), 0, glow.Size() / 2,
                    new Vector2(0.5f, 2f) * breathe * grow, SpriteEffects.None, 0);
            }
            else if (spellType == 2)
            {
                //狂暴:杖头红光心跳
                float grow = Math.Min(1f, spellTimer / (float)RageChant);
                float pulse = 1f + 0.2f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 22f);
                spriteBatch.Draw(glow, tipPos, null, new Color(255, 60, 80) * (0.7f * grow), 0, glow.Size() / 2, 0.55f * pulse * grow, SpriteEffects.None, 0);
            }
            else if (spellTimer <= AtkWindup)
            {
                //攻击聚球:吟唱长大,末 6t 塌缩到 70%(爆发前的吸气拍)
                float grow = Math.Min(1f, spellTimer / (float)AtkWindup);
                bool quiet = spellTimer > AtkWindup - 6;
                float collapse = quiet ? MathHelper.Lerp(1f, 0.68f, (spellTimer - (AtkWindup - 6)) / 6f) : 1f;
                float orb = grow * collapse;
                spriteBatch.Draw(glow, tipPos, null, new Color(150, 60, 255) * (0.85f * grow), 0, glow.Size() / 2, 0.8f * orb, SpriteEffects.None, 0);
                spriteBatch.Draw(glow, tipPos, null, new Color(235, 210, 255) * (0.9f * grow), 0, glow.Size() / 2, 0.38f * orb, SpriteEffects.None, 0);
            }

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
        }
    }
}
