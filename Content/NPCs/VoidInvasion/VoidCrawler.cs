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
    /// 虚空爬行者·头(void-invasion.md §2.7):主力层贴地多节压迫怪,统一血池(体节尾 realLife 指头)。
    /// 头部贴地导航:重力 + ≤3 格台阶直接抬升,不穿墙;体节 40px 间距跟随(镜像 Cruiser 链)。
    /// 半血狂暴:移速 ×1.5 + 红紫染色 + 持续 PRT_Void,并解锁喷焰(每 4s 停 20t 抬头 → 复用
    /// VoidFlameBreath scale 1.5 喷 60t)。进度整条只在头死亡结算一次(VoidInvasionGNPC)。
    /// 贴图朝向:本套爬行者美术一律"朝头方向为画布下方",绘制统一 -PiOver2 偏移(进游戏校验,错则改常量)。
    /// </summary>
    public class VoidCrawlerHead : ModNPC, IVoidInvasionNPC
    {
        public override string Texture => "CalamityEntropy/Content/NPCs/VoidInvasion/Crawler/head";

        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Crawler/head")]
        private static Asset<Texture2D> headTex;

        /// <summary>爬行者美术的统一旋转偏移:画布下方 = 朝向</summary>
        public const float TexRotOffset = -MathHelper.PiOver2;
        /// <summary>体节数(不含尾),节序 1-2-3-2 循环(§2.7)</summary>
        public const int SegmentCount = 8;
        public const float SegmentSpacing = 40f;
        /// <summary>狂暴染色(§2.7 红紫)</summary>
        public static readonly Color EnrageTint = new Color(255, 70, 150);

        /// <summary>狂暴染色的流动版(§2.7):强度随时间脉动、相位按节错开,头/体节/尾共用。</summary>
        public static Color EnrageColor(Color drawColor, int whoAmI)
        {
            float flow = 0.38f + 0.14f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 7.3f + whoAmI * 0.9f);
            return Color.Lerp(drawColor, EnrageTint, flow);
        }

        private const float BaseSpeed = 3.5f;
        private const float EnrageSpeedMult = 1.5f;
        private const int BreathInterval = 240;
        private const int WindupTime = 20;
        private const int BreathTime = 60;

        public enum CrawlerState : byte
        {
            Crawl,
            BreathWindup,
            Breathing,
        }

        public CrawlerState state = CrawlerState.Crawl;
        public int stateTimer = 0;
        public bool enraged = false;
        //喷焰内置冷却,只驱动服务端派发,不需同步
        private int breathCD = 0;
        private bool chainSpawned = false;
        private bool enrageBurstPlayed = false;

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = 1;
            NPCID.Sets.MPAllowedEnemies[Type] = true;
        }

        public override void SetBestiary(Terraria.GameContent.Bestiary.BestiaryDatabase database, Terraria.GameContent.Bestiary.BestiaryEntry bestiaryEntry)
        {
            bestiaryEntry.Info.AddRange(new Terraria.GameContent.Bestiary.IBestiaryInfoElement[]
            {
                new Terraria.GameContent.Bestiary.FlavorTextBestiaryInfoElement("Mods.CalamityEntropy.VoidCrawlerBestiary")
            });
        }

        public override void SetDefaults()
        {
            NPC.width = 70;
            NPC.height = 70;
            NPC.damage = 220;
            if (Main.expertMode)
            {
                NPC.damage += 20;
            }
            if (Main.masterMode)
            {
                NPC.damage += 20;
            }
            NPC.lifeMax = 260000;
            NPC.defense = 90;
            NPC.knockBackResist = 0f;
            NPC.aiStyle = -1;
            NPC.noGravity = false;
            NPC.noTileCollide = false;
            NPC.HitSound = SoundID.NPCHit4;
            NPC.DeathSound = SoundID.NPCDeath4;
            NPC.value = Item.buyPrice(0, 5, 0, 0);
            NPC.Entropy().VoidTouchDR = 0.7f;
        }

        public override bool CheckActive()
        {
            return false;
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write((byte)state);
            writer.Write(stateTimer);
            writer.Write(enraged);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            state = (CrawlerState)reader.ReadByte();
            stateTimer = reader.ReadInt32();
            enraged = reader.ReadBoolean();
        }

        private void SwitchState(CrawlerState next)
        {
            state = next;
            stateTimer = 0;
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                NPC.netUpdate = true;
            }
        }

        public override void AI()
        {
            //首帧服务端补全体节链(照 Cruiser 现成写法):ai[1]=前节 ai[2]=序号 ai[3]=头 realLife=头
            if (!chainSpawned)
            {
                chainSpawned = true;
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int prev = NPC.whoAmI;
                    for (int i = 0; i < SegmentCount + 1; i++)
                    {
                        int type = i == SegmentCount ? ModContent.NPCType<VoidCrawlerTail>() : ModContent.NPCType<VoidCrawlerBody>();
                        int index = NPC.NewNPC(NPC.GetSource_FromAI(), (int)NPC.Center.X, (int)NPC.Center.Y, type);
                        Main.npc[index].ai[1] = prev;
                        Main.npc[index].ai[2] = i;
                        Main.npc[index].ai[3] = NPC.whoAmI;
                        Main.npc[index].realLife = NPC.whoAmI;
                        prev = index;
                        if (Main.netMode == NetmodeID.Server)
                        {
                            NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, index);
                            Main.npc[index].netUpdate = true;
                        }
                    }
                }
            }

            //半血狂暴:life 原生同步,双端各自判定;字段仍进 ExtraAI 兜底(§2.7)
            if (!enraged && NPC.life * 2 < NPC.lifeMax)
            {
                enraged = true;
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    NPC.netUpdate = true;
                }
            }
            if (enraged && !enrageBurstPlayed)
            {
                enrageBurstPlayed = true;
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.Roar with { Pitch = 0.5f }, NPC.Center);
                    for (int i = 0; i < 30; i++)
                    {
                        var p = PRTLoader.NewParticle<PRT_Void>(NPC.Center,
                            CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(0f, 5f), Color.White, 1f);
                        p.Opacity = 0.8f;
                    }
                    //狂暴换相拍:红环 + 红闪(半血转性,一眼可读)
                    var ring = PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, EnrageTint, 0.2f);
                    ring.Configure(2.6f, 20);
                    var flash = PRTLoader.NewParticle<PRT_BloomCal>(NPC.Center, Vector2.Zero, new Color(255, 110, 160), 0.35f);
                    flash.Configure(1.6f, 12);
                    CEUtils.SetShake(NPC.Center, 3f, 1100);
                }
            }
            if (enraged && !Main.dedServ && Main.rand.NextBool(4))
            {
                //狂暴持续冒虚空粒子(§2.7)
                var p = PRTLoader.NewParticle<PRT_Void>(NPC.Center + CEUtils.randomPointInCircle(30f),
                    new Vector2(0, -Main.rand.NextFloat(0.5f, 1.5f)), Color.White, 1f);
                p.Opacity = 0.45f;
            }

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
            NPC.direction = target.Center.X > NPC.Center.X ? 1 : -1;

            switch (state)
            {
                case CrawlerState.Crawl:
                    {
                        float speed = BaseSpeed * (enraged ? EnrageSpeedMult : 1f);
                        NPC.velocity.X = MathHelper.Lerp(NPC.velocity.X, speed * NPC.direction, 0.1f);
                        //贴地导航:≤3 格台阶直接抬升,更高的墙尝试小跳(不穿墙,§2.7)
                        if (NPC.collideX && grounded && !ClimbStep(NPC.direction, 3))
                        {
                            NPC.velocity.Y = -7f;
                        }
                        //朝速度方向缓转(体节链的领航角)
                        if (NPC.velocity.Length() > 0.5f)
                        {
                            NPC.rotation = NPC.rotation.AngleLerp(NPC.velocity.ToRotation(), 0.2f);
                        }
                        //狂暴后每 4s 停下喷焰(§2.7);派发仅服务端
                        if (enraged && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            breathCD++;
                            if (breathCD >= BreathInterval && NPC.Center.Distance(target.Center) < 420f)
                            {
                                breathCD = 0;
                                SwitchState(CrawlerState.BreathWindup);
                            }
                        }
                        break;
                    }
                case CrawlerState.BreathWindup:
                    {
                        //停 20t 抬头(前摇,§2.7):口部聚火(热气向口心汇入,喷焰的"因")
                        NPC.velocity.X *= 0.8f;
                        NPC.rotation = NPC.rotation.AngleLerp(-0.55f * NPC.direction + (NPC.direction == -1 ? MathHelper.Pi : 0f), 0.15f);
                        if (!Main.dedServ && stateTimer % 2 == 0 && stateTimer < WindupTime - 4)
                        {
                            Vector2 mouth = NPC.Center + NPC.rotation.ToRotationVector2() * 34f;
                            Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(30f, 65f);
                            var line = PRTLoader.NewParticle<PRT_LineCal>(mouth + offset, -offset * 0.14f,
                                new Color(200, 110, 255), Main.rand.NextFloat(0.4f, 0.65f));
                            line.Configure(false, 11);
                        }
                        if (stateTimer == WindupTime && !Main.dedServ)
                        {
                            SoundEngine.PlaySound(SoundID.Item34, NPC.Center);
                        }
                        if (stateTimer >= WindupTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            //喷焰通用件复用:scale 走 ai[1](净同步安全),110 档 = NPC.damage(220)×0.25×2
                            Vector2 dir = (target.Center - NPC.Center).SafeNormalize(Vector2.UnitX * NPC.direction);
                            var proj = Projectile.NewProjectileDirect(NPC.GetSource_FromAI(), NPC.Center, dir,
                                ModContent.ProjectileType<VoidFlameBreath>(), (int)(NPC.damage * 0.25f), 1f, -1, NPC.whoAmI, 1.5f);
                            proj.timeLeft = BreathTime;
                            proj.netUpdate = true;
                            SwitchState(CrawlerState.Breathing);
                        }
                        break;
                    }
                case CrawlerState.Breathing:
                    {
                        //喷焰 60t:定身,弹幕吸附自身(§2.7)
                        NPC.velocity.X *= 0.85f;
                        if (stateTimer >= BreathTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            SwitchState(CrawlerState.Crawl);
                        }
                        break;
                    }
            }
        }

        /// <summary>≤maxTiles 格台阶直接抬升(§2.7 贴地导航),双端各自推进。</summary>
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

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (NPC.IsABestiaryIconDummy)
            {
                return true;
            }
            Texture2D tex = headTex.Value;
            Color color = enraged ? EnrageColor(drawColor, NPC.whoAmI) : drawColor;
            spriteBatch.Draw(tex, NPC.Center - screenPos, null, color * NPC.Opacity, NPC.rotation + TexRotOffset,
                tex.Size() / 2, NPC.scale, SpriteEffects.None, 0);
            return false;
        }

        public override void HitEffect(NPC.HitInfo hit)
        {
            if (NPC.life > 0 || Main.dedServ)
                return;
            for (int i = 0; i < 30; i++)
            {
                var p = PRTLoader.NewParticle<PRT_Void>(NPC.Center,
                    CEUtils.randomRot().ToRotationVector2() * Main.rand.Next(0, 400) * 0.01f, Color.White, 1f);
                p.Opacity = Main.rand.Next(20, 100) * 0.01f;
            }
        }
    }

    /// <summary>
    /// 虚空爬行者·体节(§2.7):40px 间距跟随链,节序 1-2-3-2 由 ai[2] 决定贴图;
    /// 血池镜像头(realLife)。臂/手是纯装饰程序化腿:隔节一对,只在绘制路径,不判定不同步。
    /// </summary>
    public class VoidCrawlerBody : ModNPC, IVoidInvasionNPC
    {
        public override string Texture => "CalamityEntropy/Content/NPCs/VoidInvasion/Crawler/body1";

        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Crawler/body", 1, 3, AssetMode = AssetMode.TextureValueArray)]
        private static Texture2D[] bodyTextures;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Crawler/arm", 1, 3, AssetMode = AssetMode.TextureValueArray)]
        private static Texture2D[] armTextures;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Crawler/hand")]
        private static Asset<Texture2D> handTex;

        private const float ArmScale = 0.7f;

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = 1;
            //图鉴隐藏:镜像 CruiserBody 写法
            NPCID.Sets.NPCBestiaryDrawOffset[Type] = new NPCID.Sets.NPCBestiaryDrawModifiers() { Hide = true };
            NPCID.Sets.MPAllowedEnemies[Type] = true;
        }

        public override void SetDefaults()
        {
            NPC.width = 44;
            NPC.height = 44;
            NPC.damage = 140;
            if (Main.expertMode)
            {
                NPC.damage += 20;
            }
            if (Main.masterMode)
            {
                NPC.damage += 20;
            }
            NPC.lifeMax = 260000;
            NPC.defense = 90;
            NPC.knockBackResist = 0f;
            NPC.aiStyle = -1;
            NPC.noGravity = true;
            NPC.noTileCollide = true;
            NPC.dontCountMe = true;
            NPC.HitSound = SoundID.NPCHit4;
            NPC.DeathSound = SoundID.NPCDeath4;
            NPC.value = 0;
            NPC.Entropy().VoidTouchDR = 0.7f;
        }

        public override bool CheckActive()
        {
            return false;
        }

        /// <summary>头 NPC(ai[3]);链断裂时为 null。</summary>
        protected VoidCrawlerHead Head => Main.npc[(int)NPC.ai[3]].ModNPC as VoidCrawlerHead;

        public override void AI()
        {
            NPC head = Main.npc[(int)NPC.ai[3]];
            if (!head.active || head.ModNPC is not VoidCrawlerHead)
            {
                NPC.active = false;
                return;
            }
            //统一血池镜像(照 Cruiser)
            NPC.life = head.life;
            NPC.lifeMax = head.lifeMax;
            NPC.dontTakeDamage = head.dontTakeDamage;
            NPC.ai[0]++;

            int leader = (int)NPC.ai[1];
            if (leader >= Main.maxNPCs || !Main.npc[leader].active)
            {
                NPC.active = false;
                return;
            }
            CEUtils.wormFollow(NPC.whoAmI, leader, (int)(VoidCrawlerHead.SegmentSpacing * NPC.scale), false);
            if (NPC.ai[0] > 120)
            {
                CEUtils.wormFollow(NPC.whoAmI, leader, (int)(VoidCrawlerHead.SegmentSpacing * NPC.scale), true, 0.12f);
            }
        }

        public override bool? DrawHealthBar(byte hbPosition, ref float scale, ref Vector2 position)
        {
            return false;
        }

        /// <summary>节序 1-2-3-2 混排(§2.7)</summary>
        private Texture2D SegmentTexture()
        {
            int[] pattern = { 0, 1, 2, 1 };
            return bodyTextures[pattern[(int)NPC.ai[2] % 4]];
        }

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (NPC.IsABestiaryIconDummy)
            {
                return true;
            }
            bool enraged = Head?.enraged ?? false;
            Color color = enraged ? VoidCrawlerHead.EnrageColor(drawColor, NPC.whoAmI) : drawColor;

            //装饰程序化腿:隔节一对(骨骼蜈蚣观感,§2.7),先画腿再画节(腿在后层)
            if ((int)NPC.ai[2] % 2 == 0)
            {
                DrawLegs(spriteBatch, screenPos, color);
            }
            Texture2D tex = SegmentTexture();
            spriteBatch.Draw(tex, NPC.Center - screenPos, null, color * NPC.Opacity, NPC.rotation + VoidCrawlerHead.TexRotOffset,
                tex.Size() / 2, NPC.scale, SpriteEffects.None, 0);
            return false;
        }

        /// <summary>
        /// 两段旋转装饰腿:臂根锚体节中心,朝地面法线(向下)摆动,爬得越快摆幅越大;
        /// 手挂臂尖随摆。纯绘制,不参与判定,不同步。
        /// </summary>
        private void DrawLegs(SpriteBatch sb, Vector2 screenPos, Color color)
        {
            Texture2D arm = armTextures[((int)NPC.ai[2] / 2) % 3];
            Texture2D hand = handTex.Value;
            NPC headNPC = Main.npc[(int)NPC.ai[3]];
            float speedFactor = MathHelper.Clamp(Math.Abs(headNPC.velocity.X) / 3.5f, 0f, 1.4f);
            float armLen = (arm.Height - 14) * ArmScale * NPC.scale;

            for (int leg = 0; leg < 2; leg++)
            {
                float spread = leg == 0 ? -0.4f : 0.4f;
                float swing = (float)Math.Sin(Main.GlobalTimeWrappedHourly * 7f + NPC.whoAmI * 1.31f + leg * MathHelper.Pi)
                    * (0.12f + 0.24f * speedFactor);
                //腿在世界空间恒朝下(地面法线),rotation 0 = 臂贴图自然下垂
                float armRot = spread + swing;
                Vector2 root = NPC.Center;
                Vector2 tip = root + (armRot + MathHelper.PiOver2).ToRotationVector2() * armLen;
                sb.Draw(arm, root - screenPos, null, color * NPC.Opacity * 0.9f, armRot,
                    new Vector2(arm.Width / 2, 7), ArmScale * NPC.scale, SpriteEffects.None, 0);
                sb.Draw(hand, tip - screenPos, null, color * NPC.Opacity * 0.9f, armRot * 1.6f + swing * 0.8f,
                    new Vector2(hand.Width / 2, 8), ArmScale * NPC.scale, leg == 0 ? SpriteEffects.None : SpriteEffects.FlipHorizontally, 0);
            }
        }

        public override void HitEffect(NPC.HitInfo hit)
        {
            if (NPC.life > 0 || Main.dedServ)
                return;
            for (int i = 0; i < 14; i++)
            {
                var p = PRTLoader.NewParticle<PRT_Void>(NPC.Center,
                    CEUtils.randomRot().ToRotationVector2() * Main.rand.Next(0, 400) * 0.01f, Color.White, 1f);
                p.Opacity = Main.rand.Next(20, 100) * 0.01f;
            }
        }
    }

    /// <summary>虚空爬行者·尾(§2.7):跟随链末节,逻辑同体节,无装饰腿。</summary>
    public class VoidCrawlerTail : VoidCrawlerBody
    {
        public override string Texture => "CalamityEntropy/Content/NPCs/VoidInvasion/Crawler/tail";

        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Crawler/tail")]
        private static Asset<Texture2D> tailTex;

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (NPC.IsABestiaryIconDummy)
            {
                return true;
            }
            bool enraged = Head?.enraged ?? false;
            Color color = enraged ? VoidCrawlerHead.EnrageColor(drawColor, NPC.whoAmI) : drawColor;
            Texture2D tex = tailTex.Value;
            spriteBatch.Draw(tex, NPC.Center - screenPos, null, color * NPC.Opacity, NPC.rotation + VoidCrawlerHead.TexRotOffset,
                tex.Size() / 2, NPC.scale, SpriteEffects.None, 0);
            return false;
        }
    }
}
