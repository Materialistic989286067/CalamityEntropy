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
    /// 虚空掠食者·头(void-invasion.md §2.8):精英层传送门蠕虫,统一血池,镜像 Cruiser 头身尾结构。
    /// 循环:玩家附近 300~500px 开 VoidPortal(40t 前摇)→ 整条 26px/t 冲出弧线掠过 → 1.2s 后
    /// 前方开出口门钻入 → 冷却 2s 换角度;每第 3 次门袭改盘缠(绕玩家 260px 盘旋 4s,环上恒留
    /// 缺口 + 闪光标记,结束从缺口离场)。
    /// 可见性走"门平面"法:以 portalPos+dashDir 定义平面,门后段落隐形(SegmentAlpha),
    /// 视觉上整条从门里钻出/钻入;体节位置由跟随链确定性推导,不同步坐标。
    /// 贴图朝向:本套掠食者美术一律"朝头方向为画布上方",绘制统一 +PiOver2 偏移(进游戏校验)。
    /// </summary>
    public class VoidPredatorHead : ModNPC, IVoidInvasionNPC
    {
        public override string Texture => "CalamityEntropy/Content/NPCs/VoidInvasion/Predator/head";

        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Predator/head")]
        private static Asset<Texture2D> headTex;
        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light")]
        private static Asset<Texture2D> glowTex;

        /// <summary>掠食者美术的统一旋转偏移:画布上方 = 朝向</summary>
        public const float TexRotOffset = MathHelper.PiOver2;
        /// <summary>体节数(不含尾),一二交替(§2.8 取 12,常量可调)</summary>
        public const int SegmentCount = 12;
        public const float SegmentSpacing = 56f;
        /// <summary>整条链长(门平面判定与盘缠缺口用)</summary>
        public const float ChainLength = (SegmentCount + 1) * SegmentSpacing;

        private const float DashSpeed = 26f;
        private const int CooldownTime = 120;
        private const int WindupTime = 40;
        private const int EmergeTime = 72;
        private const int CoilTime = 240;
        private const float CoilRadius = 260f;
        private const float CoilAngularSpeed = 0.045f;

        public enum PredatorState : byte
        {
            Cooldown,
            Windup,
            Emerge,
            Exit,
            Coil,
            CoilExit,
        }

        public PredatorState state = PredatorState.Cooldown;
        public int stateTimer = 0;
        public byte dashCount = 0;
        public Vector2 portalPos = Vector2.Zero;
        public Vector2 dashDir = Vector2.UnitX;
        public float coilAngle = 0;
        public sbyte coilDir = 1;
        private bool chainSpawned = false;
        //上一帧的门平面可见度(客户端过门涟漪检测,纯视觉不同步)
        private float prevSegAlpha = -1f;

        /// <summary>
        /// 过门涟漪(§2.8 "入水式"演出,头/体节/噬虚鲨共用):
        /// 段落穿过门平面的瞬间,在其平面投影点甩出一道垂直于行进轴的冲击环 + 切向水花。
        /// 纯客户端调用。
        /// </summary>
        public static void PortalCrossRipple(Vector2 pos, Vector2 portalPos, Vector2 dir, float strength)
        {
            Vector2 planePos = pos - dir * Vector2.Dot(pos - portalPos, dir);
            var ring = PRTLoader.NewParticle<PRT_DirectionalPulseRing>(planePos, Vector2.Zero,
                new Color(190, 120, 255), 0.16f * strength);
            ring.Configure(new Vector2(VoidPortal.Squash, 1.15f), dir.ToRotation(), 1.5f * strength, 13);
            for (int i = 0; i < 3; i++)
            {
                Vector2 side = dir.RotatedBy(MathHelper.PiOver2) * (Main.rand.NextBool() ? 1 : -1);
                var line = PRTLoader.NewParticle<PRT_LineCal>(planePos + side * Main.rand.NextFloat(10f, 40f) * strength,
                    side * Main.rand.NextFloat(4f, 9f) + dir * Main.rand.NextFloat(-1.5f, 1.5f),
                    new Color(220, 160, 255), Main.rand.NextFloat(0.4f, 0.7f) * strength);
                line.Configure(false, 11);
            }
            var splash = PRTLoader.NewParticle<PRT_Void>(planePos + CEUtils.randomPointInCircle(20f * strength),
                dir.RotatedBy(Main.rand.NextFloat(-1f, 1f)) * 2f, Color.White, 1f);
            splash.Opacity = 0.55f;
        }

        /// <summary>客户端:检测本段落穿越门平面(可见度过 0.5)并甩涟漪。</summary>
        private void DetectPlaneCross()
        {
            if (Main.dedServ)
                return;
            float a = SegmentAlpha(NPC.Center);
            bool tracked = state == PredatorState.Emerge || state == PredatorState.Exit || state == PredatorState.CoilExit;
            if (tracked && prevSegAlpha >= 0f && (prevSegAlpha - 0.5f) * (a - 0.5f) < 0f)
            {
                PortalCrossRipple(NPC.Center, portalPos, dashDir, 1.1f);
            }
            prevSegAlpha = tracked ? a : -1f;
        }

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = 1;
            NPCID.Sets.MPAllowedEnemies[Type] = true;
        }

        public override void SetBestiary(Terraria.GameContent.Bestiary.BestiaryDatabase database, Terraria.GameContent.Bestiary.BestiaryEntry bestiaryEntry)
        {
            bestiaryEntry.Info.AddRange(new Terraria.GameContent.Bestiary.IBestiaryInfoElement[]
            {
                new Terraria.GameContent.Bestiary.FlavorTextBestiaryInfoElement("Mods.CalamityEntropy.VoidPredatorBestiary")
            });
        }

        public override void SetDefaults()
        {
            NPC.width = 70;
            NPC.height = 90;
            NPC.damage = 200;
            if (Main.expertMode)
            {
                NPC.damage += 20;
            }
            if (Main.masterMode)
            {
                NPC.damage += 20;
            }
            NPC.lifeMax = 280000;
            NPC.defense = 100;
            NPC.knockBackResist = 0f;
            NPC.aiStyle = -1;
            NPC.noGravity = true;
            NPC.noTileCollide = true;
            NPC.HitSound = SoundID.NPCHit4;
            NPC.DeathSound = SoundID.NPCDeath4;
            NPC.value = Item.buyPrice(0, 5, 0, 0);
            NPC.Entropy().VoidTouchDR = 0.7f;
        }

        //掉落(M9 已落):魂髓 1~2 @40% 与召唤杖 5% 统一挂在 VoidInvasionGNPC.ModifyNPCLoot(§5.1/§5.4)

        public override bool CheckActive()
        {
            return false;
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write((byte)state);
            writer.Write(stateTimer);
            writer.Write(dashCount);
            writer.WriteVector2(portalPos);
            writer.WriteVector2(dashDir);
            writer.Write(coilAngle);
            writer.Write(coilDir);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            state = (PredatorState)reader.ReadByte();
            stateTimer = reader.ReadInt32();
            dashCount = reader.ReadByte();
            portalPos = reader.ReadVector2();
            dashDir = reader.ReadVector2();
            coilAngle = reader.ReadSingle();
            coilDir = reader.ReadSByte();
        }

        private void SwitchState(PredatorState next)
        {
            state = next;
            stateTimer = 0;
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                NPC.netUpdate = true;
            }
        }

        /// <summary>
        /// 段落可见度(头与体节共用,各端由同步字段确定性推导):
        /// 冷却/前摇全隐;冲出时门平面后隐形(钻出观感);钻入时过平面即隐(钻入观感);盘缠全显。
        /// </summary>
        public float SegmentAlpha(Vector2 pos)
        {
            switch (state)
            {
                case PredatorState.Cooldown:
                case PredatorState.Windup:
                    return 0f;
                case PredatorState.Emerge:
                    return MathHelper.Clamp((Vector2.Dot(pos - portalPos, dashDir) + 24f) / 48f, 0f, 1f);
                case PredatorState.Exit:
                case PredatorState.CoilExit:
                    return MathHelper.Clamp((Vector2.Dot(portalPos - pos, dashDir) + 24f) / 48f, 0f, 1f);
                default:
                    return 1f;
            }
        }

        public bool Hidden => state == PredatorState.Cooldown || state == PredatorState.Windup;

        public override bool CanHitPlayer(Player target, ref int cooldownSlot)
        {
            return SegmentAlpha(NPC.Center) > 0.5f;
        }

        public override void AI()
        {
            //首帧服务端补全体节链(照 Cruiser 现成写法)
            if (!chainSpawned)
            {
                chainSpawned = true;
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    int prev = NPC.whoAmI;
                    for (int i = 0; i < SegmentCount + 1; i++)
                    {
                        int type = i == SegmentCount ? ModContent.NPCType<VoidPredatorTail>() : ModContent.NPCType<VoidPredatorBody>();
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

            NPC.dontTakeDamage = Hidden;
            NPC.damage = SegmentAlpha(NPC.Center) > 0.5f ? NPC.defDamage : 0;

            if (!NPC.HasValidTarget)
            {
                NPC.TargetClosest(false);
                if (!NPC.HasValidTarget)
                {
                    NPC.velocity *= 0.95f;
                    return;
                }
            }
            Player target = Main.player[NPC.target];
            stateTimer++;

            switch (state)
            {
                case PredatorState.Cooldown:
                    {
                        //冷却 2s 换角度(§2.8),隐身待机
                        NPC.velocity = Vector2.Zero;
                        if (stateTimer >= CooldownTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            OpenEntryPortal(target);
                        }
                        break;
                    }
                case PredatorState.Windup:
                    {
                        //门张开 40t:头沿门轴反向后撤,把跟随链拉直排到门平面后(双端确定性推导)
                        NPC.velocity = Vector2.Zero;
                        NPC.Center = portalPos - dashDir * (8f + stateTimer * 18f);
                        NPC.rotation = dashDir.ToRotation();
                        if (stateTimer >= WindupTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            NPC.Center = portalPos - dashDir * 8f;
                            NPC.velocity = dashDir * DashSpeed;
                            dashCount++;
                            SwitchState(PredatorState.Emerge);
                        }
                        break;
                    }
                case PredatorState.Emerge:
                    {
                        //整条 26px/t 冲出,前 45t 缓转出弧线掠过(§2.8)
                        if (stateTimer == 1 && !Main.dedServ)
                        {
                            SoundEngine.PlaySound(SoundID.ForceRoarPitched with { Volume = 0.6f }, portalPos);
                            //破门拍:门口白闪 + 沿冲刺轴的速度线 + 轻震屏(头带整条链破水而出)
                            var flash = PRTLoader.NewParticle<PRT_BloomCal>(portalPos, Vector2.Zero, Color.White, 0.4f);
                            flash.Configure(2f, 10);
                            CEUtils.SetShake(portalPos, 3.5f, 1200);
                            for (int i = 0; i < 8; i++)
                            {
                                Vector2 side = dashDir.RotatedBy(MathHelper.PiOver2) * Main.rand.NextFloat(-70f, 70f);
                                var line = PRTLoader.NewParticle<PRT_LineCal>(portalPos + side,
                                    dashDir * Main.rand.NextFloat(11f, 19f), new Color(220, 160, 255), Main.rand.NextFloat(0.5f, 0.9f));
                                line.Configure(false, 12);
                            }
                        }
                        if (stateTimer < 45)
                        {
                            float aim = (target.Center - NPC.Center).ToRotation();
                            NPC.velocity = NPC.velocity.ToRotation().AngleTowards(aim, 0.012f).ToRotationVector2() * DashSpeed;
                        }
                        NPC.rotation = NPC.velocity.ToRotation();
                        if (stateTimer >= EmergeTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            if (dashCount % 3 == 0)
                            {
                                //每第 3 次门袭改盘缠(§2.8)
                                Vector2 radial = NPC.Center - target.Center;
                                coilAngle = radial.ToRotation();
                                coilDir = (sbyte)(radial.X * NPC.velocity.Y - radial.Y * NPC.velocity.X >= 0 ? 1 : -1);
                                SwitchState(PredatorState.Coil);
                            }
                            else
                            {
                                OpenExitPortal(NPC.velocity.SafeNormalize(dashDir));
                                SwitchState(PredatorState.Exit);
                            }
                        }
                        break;
                    }
                case PredatorState.Exit:
                case PredatorState.CoilExit:
                    {
                        //直线钻入出口门,尾节过平面后收进冷却
                        NPC.velocity = Vector2.Lerp(NPC.velocity, dashDir * DashSpeed, 0.1f);
                        NPC.rotation = NPC.velocity.ToRotation();
                        if (Main.netMode != NetmodeID.MultiplayerClient
                            && Vector2.Dot(NPC.Center - portalPos, dashDir) > ChainLength + 80f)
                        {
                            NPC.velocity = Vector2.Zero;
                            SwitchState(PredatorState.Cooldown);
                        }
                        break;
                    }
                case PredatorState.Coil:
                    {
                        //绕玩家 260px 匀速盘旋 4s,头领航 0.045rad/t,身体自然成环(§2.8)
                        coilAngle += CoilAngularSpeed * coilDir;
                        Vector2 desired = target.Center + coilAngle.ToRotationVector2() * CoilRadius;
                        Vector2 want = desired - NPC.Center;
                        NPC.velocity = want.Length() > 30f ? want.SafeNormalize(Vector2.UnitX) * 30f : want;
                        NPC.rotation = NPC.velocity.Length() > 0.5f ? NPC.velocity.ToRotation() : NPC.rotation;
                        //缺口闪光标记(公平阀,§2.8):粒子在缺口中心持续脉动
                        if (!Main.dedServ && Main.rand.NextBool(2))
                        {
                            Vector2 gapPos = target.Center + GapAngle().ToRotationVector2() * CoilRadius;
                            var p = PRTLoader.NewParticle<PRT_Light>(gapPos + CEUtils.randomPointInCircle(18f),
                                CEUtils.randomRot().ToRotationVector2() * 1.5f, new Color(255, 220, 140), 0.5f);
                            p.Configure(0.9f, lifetime: 16);
                        }
                        if (stateTimer >= CoilTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            //从缺口方向离场钻门(§2.8)
                            dashDir = GapAngle().ToRotationVector2();
                            portalPos = target.Center + dashDir * (CoilRadius + 360f);
                            VoidPortal.Open(NPC.GetSource_FromAI(), portalPos, dashDir, 40 + (int)(ChainLength / DashSpeed) + 40, 1.15f);
                            SwitchState(PredatorState.CoilExit);
                        }
                        break;
                    }
            }
            DetectPlaneCross();
        }

        /// <summary>盘缠缺口中心角:蠕虫环带占 ChainLength/半径 弧度,缺口在头前方剩余弧段的中点。</summary>
        public float GapAngle()
        {
            float chainSpan = ChainLength / CoilRadius;
            return coilAngle + coilDir * (MathHelper.TwoPi - chainSpan) / 2f;
        }

        /// <summary>服务端:选门点(玩家 300~500px,避实心)、定弧线掠过方向、开门并把头挂到门后。</summary>
        private void OpenEntryPortal(Player target)
        {
            Vector2 pos = target.Center;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                pos = target.Center + CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(300f, 500f);
                if (!Collision.SolidCollision(pos - new Vector2(60, 60), 120, 120))
                    break;
            }
            portalPos = pos;
            //弧线掠过:瞄准玩家侧向 90~150px 的偏移点,冲出即擦身而过
            Vector2 toPlayer = (target.Center - portalPos).SafeNormalize(Vector2.UnitX);
            Vector2 perp = new Vector2(-toPlayer.Y, toPlayer.X) * (Main.rand.NextBool() ? 1 : -1) * Main.rand.NextFloat(90f, 150f);
            dashDir = (target.Center + perp - portalPos).SafeNormalize(Vector2.UnitX);
            VoidPortal.Open(NPC.GetSource_FromAI(), portalPos, dashDir, WindupTime + EmergeTime + 20, 1.15f);
            NPC.Center = portalPos - dashDir * 8f;
            SwitchState(PredatorState.Windup);
        }

        /// <summary>服务端:飞行前方开出口门(§2.8 "1.2s 后前方开出口门钻入")。</summary>
        private void OpenExitPortal(Vector2 dir)
        {
            dashDir = dir;
            portalPos = NPC.Center + dir * 320f;
            VoidPortal.Open(NPC.GetSource_FromAI(), portalPos, dir, 40 + (int)(ChainLength / DashSpeed) + 40, 1.15f);
        }

        public override bool? DrawHealthBar(byte hbPosition, ref float scale, ref Vector2 position)
        {
            return SegmentAlpha(NPC.Center) > 0.5f ? null : false;
        }

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (NPC.IsABestiaryIconDummy)
            {
                return true;
            }
            float alpha = SegmentAlpha(NPC.Center) * NPC.Opacity;
            if (alpha > 0.01f)
            {
                Texture2D tex = headTex.Value;
                spriteBatch.Draw(tex, NPC.Center - screenPos, null, drawColor * alpha, NPC.rotation + TexRotOffset,
                    tex.Size() / 2, NPC.scale, SpriteEffects.None, 0);
            }
            //盘缠缺口的常亮标记(与粒子叠加,保证低粒子设置下也可读)
            if (state == PredatorState.Coil && NPC.HasValidTarget)
            {
                Texture2D glow = glowTex.Value;
                Vector2 gapPos = Main.player[NPC.target].Center + GapAngle().ToRotationVector2() * CoilRadius;
                float pulse = 0.8f + 0.3f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 12f);
                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
                spriteBatch.Draw(glow, gapPos - screenPos, null, new Color(255, 220, 140) * 0.85f, 0, glow.Size() / 2, pulse, SpriteEffects.None, 0);
                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            }
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
    /// 虚空掠食者·体节(§2.8):体节一/二交替(ai[2] 奇偶),56px 跟随链,血池镜像头;
    /// 可见性与可击中性从头的门平面推导。
    /// </summary>
    public class VoidPredatorBody : ModNPC, IVoidInvasionNPC
    {
        public override string Texture => "CalamityEntropy/Content/NPCs/VoidInvasion/Predator/body1";

        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Predator/body", 1, 2, AssetMode = AssetMode.TextureValueArray)]
        private static Texture2D[] bodyTextures;

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = 1;
            NPCID.Sets.NPCBestiaryDrawOffset[Type] = new NPCID.Sets.NPCBestiaryDrawModifiers() { Hide = true };
            NPCID.Sets.MPAllowedEnemies[Type] = true;
        }

        public override void SetDefaults()
        {
            NPC.width = 70;
            NPC.height = 56;
            NPC.damage = 130;
            if (Main.expertMode)
            {
                NPC.damage += 20;
            }
            if (Main.masterMode)
            {
                NPC.damage += 20;
            }
            NPC.lifeMax = 280000;
            NPC.defense = 100;
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

        protected VoidPredatorHead Head => Main.npc[(int)NPC.ai[3]].ModNPC as VoidPredatorHead;

        //上一帧的门平面可见度(客户端过门涟漪检测,纯视觉不同步)
        private float prevSegAlpha = -1f;

        public override bool CanHitPlayer(Player target, ref int cooldownSlot)
        {
            return (Head?.SegmentAlpha(NPC.Center) ?? 1f) > 0.5f;
        }

        public override void AI()
        {
            NPC head = Main.npc[(int)NPC.ai[3]];
            if (!head.active || head.ModNPC is not VoidPredatorHead)
            {
                NPC.active = false;
                return;
            }
            NPC.life = head.life;
            NPC.lifeMax = head.lifeMax;
            NPC.dontTakeDamage = head.dontTakeDamage;
            NPC.damage = (Head?.SegmentAlpha(NPC.Center) ?? 1f) > 0.5f ? NPC.defDamage : 0;
            NPC.ai[0]++;

            int leader = (int)NPC.ai[1];
            if (leader >= Main.maxNPCs || !Main.npc[leader].active)
            {
                NPC.active = false;
                return;
            }
            CEUtils.wormFollow(NPC.whoAmI, leader, (int)(VoidPredatorHead.SegmentSpacing * NPC.scale), false);
            if (NPC.ai[0] > 60)
            {
                CEUtils.wormFollow(NPC.whoAmI, leader, (int)(VoidPredatorHead.SegmentSpacing * NPC.scale), true, 0.15f);
            }

            //过门涟漪(§2.8 入水式):本节穿过门平面的拍,纯客户端
            if (!Main.dedServ && Head is VoidPredatorHead h)
            {
                float a = h.SegmentAlpha(NPC.Center);
                bool tracked = h.state == VoidPredatorHead.PredatorState.Emerge
                    || h.state == VoidPredatorHead.PredatorState.Exit
                    || h.state == VoidPredatorHead.PredatorState.CoilExit;
                if (tracked && prevSegAlpha >= 0f && (prevSegAlpha - 0.5f) * (a - 0.5f) < 0f)
                {
                    VoidPredatorHead.PortalCrossRipple(NPC.Center, h.portalPos, h.dashDir, 0.85f);
                }
                prevSegAlpha = tracked ? a : -1f;
            }
        }

        public override bool? DrawHealthBar(byte hbPosition, ref float scale, ref Vector2 position)
        {
            return false;
        }

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (NPC.IsABestiaryIconDummy)
            {
                return true;
            }
            float alpha = (Head?.SegmentAlpha(NPC.Center) ?? 1f) * NPC.Opacity;
            if (alpha <= 0.01f)
            {
                return false;
            }
            Texture2D tex = bodyTextures[(int)NPC.ai[2] % 2];
            spriteBatch.Draw(tex, NPC.Center - screenPos, null, drawColor * alpha, NPC.rotation + VoidPredatorHead.TexRotOffset,
                tex.Size() / 2, NPC.scale, SpriteEffects.None, 0);
            return false;
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

    /// <summary>虚空掠食者·尾(§2.8):跟随链末节,逻辑同体节。</summary>
    public class VoidPredatorTail : VoidPredatorBody
    {
        public override string Texture => "CalamityEntropy/Content/NPCs/VoidInvasion/Predator/tail";

        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Predator/tail")]
        private static Asset<Texture2D> tailTex;

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (NPC.IsABestiaryIconDummy)
            {
                return true;
            }
            float alpha = (Head?.SegmentAlpha(NPC.Center) ?? 1f) * NPC.Opacity;
            if (alpha <= 0.01f)
            {
                return false;
            }
            Texture2D tex = tailTex.Value;
            spriteBatch.Draw(tex, NPC.Center - screenPos, null, drawColor * alpha, NPC.rotation + VoidPredatorHead.TexRotOffset,
                tex.Size() / 2, NPC.scale, SpriteEffects.None, 0);
            return false;
        }
    }
}
