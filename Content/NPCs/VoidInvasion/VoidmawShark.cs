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
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.NPCs.VoidInvasion
{
    /// <summary>
    /// 噬虚鲨(void-invasion.md §2.9):精英层高速威胁,节奏怪:三连冲一歇。
    /// 循环:空中开门 → 探头咆哮 15t(门内红光)→ 30px/t 直线冲刺 0.8s(拉伸残影)→
    /// 减速漂移 0.5s(可打窗口)→ 前方开门钻入 → 1.5s 新角度;三连冲后悬浮喘息 2.5s(大窗口)。
    /// 贴图 1312x2264 竖排 4 帧原样导入,scale=0.6 起步(待进游戏校验);美术朝左。
    /// </summary>
    public class VoidmawShark : ModNPC, IVoidInvasionNPC
    {
        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light")]
        private static Asset<Texture2D> glowTex;

        public enum SharkState : byte
        {
            Cooldown,
            Roar,
            Dash,
            Drift,
            DiveOut,
            Breathe,
        }

        //节拍常量(§2.9)
        private const int CooldownTime = 90;
        private const int PortalLeadTime = 45;
        private const int RoarTime = 15;
        private const int DashTime = 48;
        private const int DriftTime = 30;
        private const int BreatheTime = 150;
        private const float DashSpeed = 30f;
        private const float DiveSpeed = 24f;

        public SharkState state = SharkState.Cooldown;
        public int stateTimer = 0;
        public byte dashCount = 0;
        public Vector2 dashVec = Vector2.UnitX;
        public Vector2 portalPos = Vector2.Zero;
        //冲刺残影,纯客户端视觉
        private readonly List<Vector2> oldPos = new();

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = 4;
            NPCID.Sets.MPAllowedEnemies[Type] = true;
            //原图 4 帧竖排巨幅,图鉴里压回可读尺寸
            NPCID.Sets.NPCBestiaryDrawOffset[Type] = new NPCID.Sets.NPCBestiaryDrawModifiers() { Scale = 0.15f, PortraitScale = 0.2f };
        }

        public override void SetBestiary(Terraria.GameContent.Bestiary.BestiaryDatabase database, Terraria.GameContent.Bestiary.BestiaryEntry bestiaryEntry)
        {
            bestiaryEntry.Info.AddRange(new Terraria.GameContent.Bestiary.IBestiaryInfoElement[]
            {
                new Terraria.GameContent.Bestiary.FlavorTextBestiaryInfoElement("Mods.CalamityEntropy.VoidmawSharkBestiary")
            });
        }

        public override void SetDefaults()
        {
            NPC.width = 200;
            NPC.height = 90;
            NPC.damage = 210;
            if (Main.expertMode)
            {
                NPC.damage += 20;
            }
            if (Main.masterMode)
            {
                NPC.damage += 20;
            }
            NPC.lifeMax = 240000;
            NPC.defense = 95;
            NPC.knockBackResist = 0f;
            NPC.aiStyle = -1;
            NPC.noGravity = true;
            NPC.noTileCollide = true;
            NPC.HitSound = SoundID.NPCHit1;
            NPC.DeathSound = SoundID.NPCDeath1;
            NPC.value = Item.buyPrice(0, 5, 0, 0);
            NPC.scale = 0.6f;
            NPC.Entropy().VoidTouchDR = 0.7f;
        }

        public override bool CheckActive()
        {
            return false;
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write(dashCount);
            writer.Write((byte)state);
            writer.Write(stateTimer);
            writer.WriteVector2(dashVec);
            writer.WriteVector2(portalPos);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            dashCount = reader.ReadByte();
            state = (SharkState)reader.ReadByte();
            stateTimer = reader.ReadInt32();
            dashVec = reader.ReadVector2();
            portalPos = reader.ReadVector2();
        }

        private void SwitchState(SharkState next)
        {
            state = next;
            stateTimer = 0;
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                NPC.netUpdate = true;
            }
        }

        /// <summary>当前可见度(双端由同步字段确定性推导):冷却隐形,咆哮渐显,钻门过平面渐隐。</summary>
        public float VisAlpha()
        {
            switch (state)
            {
                case SharkState.Cooldown:
                    return 0f;
                case SharkState.Roar:
                    return MathHelper.Clamp(stateTimer / (float)RoarTime, 0f, 1f);
                case SharkState.DiveOut:
                    Vector2 dir = dashVec.SafeNormalize(Vector2.UnitX);
                    return MathHelper.Clamp((Vector2.Dot(portalPos - NPC.Center, dir) + 40f) / 80f, 0f, 1f);
                default:
                    return 1f;
            }
        }

        public override bool CanHitPlayer(Player target, ref int cooldownSlot)
        {
            return VisAlpha() > 0.5f;
        }

        public override void AI()
        {
            //冲刺接触 210 / 非冲刺 120(§2.9);defDamage 是 SetDefaults 定稿值
            NPC.damage = state == SharkState.Dash ? NPC.defDamage : NPC.defDamage - 90;
            NPC.dontTakeDamage = VisAlpha() < 0.5f;

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
                case SharkState.Cooldown:
                    {
                        //1.5s 新角度(§2.9):隐身待机,中段选点开门
                        NPC.velocity = Vector2.Zero;
                        if (stateTimer == PortalLeadTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            Vector2 pos = target.Center;
                            for (int attempt = 0; attempt < 6; attempt++)
                            {
                                pos = target.Center + CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(350f, 520f);
                                if (!Collision.SolidCollision(pos - new Vector2(70, 70), 140, 140))
                                    break;
                            }
                            portalPos = pos;
                            NPC.Center = portalPos;
                            NPC.velocity = Vector2.Zero;
                            dashVec = (target.Center - portalPos).SafeNormalize(Vector2.UnitX) * DashSpeed;
                            VoidPortal.Open(NPC.GetSource_FromAI(), portalPos, dashVec, PortalLeadTime + RoarTime + 14, 1f);
                            NPC.netUpdate = true;
                        }
                        if (stateTimer >= CooldownTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            //探头方向锁定在咆哮起手(15t 可读拍,§2.9)
                            dashVec = (target.Center - NPC.Center).SafeNormalize(Vector2.UnitX) * DashSpeed;
                            SwitchState(SharkState.Roar);
                        }
                        break;
                    }
                case SharkState.Roar:
                    {
                        NPC.velocity = Vector2.Zero;
                        if (stateTimer == 1 && !Main.dedServ)
                        {
                            SoundEngine.PlaySound(SoundID.Roar with { Pitch = 0.35f }, NPC.Center);
                        }
                        if (!Main.dedServ)
                        {
                            //门内凶光的"因":红纹向门心倒吸(咆哮 = 蓄力的读法)
                            Vector2 dir = dashVec.SafeNormalize(Vector2.UnitX);
                            if (stateTimer % 2 == 0)
                            {
                                Vector2 offset = dir.RotatedBy(Main.rand.NextFloat(-1.1f, 1.1f)) * Main.rand.NextFloat(90f, 170f);
                                var line = PRTLoader.NewParticle<PRT_LineCal>(portalPos + offset, -offset * 0.11f,
                                    new Color(255, 80, 90), Main.rand.NextFloat(0.45f, 0.8f));
                                line.Configure(false, 12);
                            }
                            //咆哮声压环:两拍红环外扩
                            if (stateTimer % 6 == 1)
                            {
                                var ring = PRTLoader.NewParticle<PRT_DirectionalPulseRing>(portalPos, Vector2.Zero,
                                    new Color(255, 70, 80), 0.2f);
                                ring.Configure(new Vector2(VoidPortal.Squash, 1.1f), dashVec.ToRotation(), 1.7f, 14);
                            }
                        }
                        if (stateTimer >= RoarTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            NPC.velocity = dashVec;
                            dashCount++;
                            SwitchState(SharkState.Dash);
                        }
                        break;
                    }
                case SharkState.Dash:
                    {
                        //30px/t 直线冲刺 0.8s(§2.9)
                        NPC.velocity = dashVec;
                        if (!Main.dedServ && stateTimer == 1)
                        {
                            //出膛拍:门口白闪 + 冲击环 + 速度线 + 轻震屏 + 破门涟漪
                            SoundEngine.PlaySound(SoundID.ForceRoarPitched with { Volume = 0.7f, Pitch = 0.2f }, portalPos);
                            CEUtils.SetShake(portalPos, 4f, 1300);
                            var flash = PRTLoader.NewParticle<PRT_BloomCal>(portalPos, Vector2.Zero, Color.White, 0.4f);
                            flash.Configure(2.1f, 10);
                            Vector2 launchDir = dashVec.SafeNormalize(Vector2.UnitX);
                            for (int i = 0; i < 10; i++)
                            {
                                Vector2 side = launchDir.RotatedBy(MathHelper.PiOver2) * Main.rand.NextFloat(-80f, 80f);
                                var line = PRTLoader.NewParticle<PRT_LineCal>(portalPos + side,
                                    launchDir * Main.rand.NextFloat(12f, 21f), new Color(255, 130, 150), Main.rand.NextFloat(0.5f, 0.95f));
                                line.Configure(false, 12);
                            }
                            VoidPredatorHead.PortalCrossRipple(NPC.Center, portalPos, launchDir, 1.4f);
                        }
                        if (!Main.dedServ && Main.rand.NextBool(2))
                        {
                            var p = PRTLoader.NewParticle<PRT_Void>(NPC.Center + CEUtils.randomPointInCircle(40f),
                                -dashVec * 0.06f, Color.White, 1f);
                            p.Opacity = 0.5f;
                        }
                        if (stateTimer >= DashTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            SwitchState(SharkState.Drift);
                        }
                        break;
                    }
                case SharkState.Drift:
                    {
                        //减速漂移 0.5s:可打窗口(§2.9)
                        NPC.velocity *= 0.92f;
                        if (stateTimer >= DriftTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            if (dashCount % 3 == 0)
                            {
                                SwitchState(SharkState.Breathe);
                            }
                            else
                            {
                                BeginDiveOut(dashVec.SafeNormalize(Vector2.UnitX));
                            }
                        }
                        break;
                    }
                case SharkState.DiveOut:
                    {
                        NPC.velocity = dashVec;
                        Vector2 dir = dashVec.SafeNormalize(Vector2.UnitX);
                        if (Main.netMode != NetmodeID.MultiplayerClient
                            && Vector2.Dot(NPC.Center - portalPos, dir) > 80f)
                        {
                            NPC.velocity = Vector2.Zero;
                            SwitchState(SharkState.Cooldown);
                        }
                        break;
                    }
                case SharkState.Breathe:
                    {
                        //悬浮喘息 2.5s:大可打窗口(§2.9)
                        NPC.velocity *= 0.9f;
                        NPC.velocity.Y += (float)Math.Sin(stateTimer * 0.08f) * 0.06f;
                        if (stateTimer >= BreatheTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            BeginDiveOut((NPC.Center - target.Center).SafeNormalize(Vector2.UnitX));
                        }
                        break;
                    }
            }
        }

        /// <summary>服务端:前方开出口门并转入钻门离场(§2.9)。</summary>
        private void BeginDiveOut(Vector2 dir)
        {
            portalPos = NPC.Center + dir * 280f;
            dashVec = dir * DiveSpeed;
            VoidPortal.Open(NPC.GetSource_FromAI(), portalPos, dir, 70, 1f);
            SwitchState(SharkState.DiveOut);
        }

        //上一帧可见度(客户端钻门涟漪检测,纯视觉不同步)
        private float prevVis = -1f;

        public override void PostAI()
        {
            //拉伸残影镜像教徒 oldPos 写法(§2.9)
            if (state == SharkState.Dash)
            {
                oldPos.Add(NPC.Center);
            }
            if (oldPos.Count > 8 || (state != SharkState.Dash && oldPos.Count > 0))
            {
                oldPos.RemoveAt(0);
            }
            //钻门涟漪(§2.9):钻入出口门过平面的拍,纯客户端
            if (!Main.dedServ)
            {
                float vis = VisAlpha();
                if (state == SharkState.DiveOut && prevVis >= 0f && (prevVis - 0.5f) * (vis - 0.5f) < 0f)
                {
                    VoidPredatorHead.PortalCrossRipple(NPC.Center, portalPos, dashVec.SafeNormalize(Vector2.UnitX), 1.4f);
                }
                prevVis = state == SharkState.DiveOut ? vis : -1f;
            }
        }

        public override void FindFrame(int frameHeight)
        {
            if (Main.dedServ)
                return;
            //竖排 4 帧 6t/帧(§2.9);冲刺/咆哮锁帧 2
            if (state == SharkState.Dash || state == SharkState.Roar)
            {
                NPC.frame.Y = frameHeight;
                return;
            }
            NPC.frameCounter++;
            if (NPC.frameCounter >= 6)
            {
                NPC.frameCounter = 0;
                NPC.frame.Y += frameHeight;
            }
            if (NPC.frame.Y >= frameHeight * 4)
            {
                NPC.frame.Y = 0;
            }
        }

        /// <summary>朝向与旋转:美术朝左,右行时翻转 + 角度加 π(标准鱼形贴图公式)。</summary>
        private void GetOrientation(out float rotation, out SpriteEffects fx)
        {
            Vector2 facing = NPC.velocity.Length() > 1f ? NPC.velocity : dashVec;
            if (state == SharkState.Breathe && NPC.HasValidTarget)
            {
                facing = Main.player[NPC.target].Center - NPC.Center;
            }
            bool right = facing.X >= 0;
            rotation = facing.ToRotation() + (right ? MathHelper.Pi : 0f);
            fx = right ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        }

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (NPC.IsABestiaryIconDummy)
            {
                return true;
            }
            float alpha = VisAlpha() * NPC.Opacity;
            Texture2D tex = TextureAssets.Npc[Type].Value;
            GetOrientation(out float rotation, out SpriteEffects fx);
            Vector2 origin = NPC.frame.Size() / 2;

            //咆哮探头:沿冲刺方向从门里探出(§2.9)
            Vector2 pokeOffset = Vector2.Zero;
            if (state == SharkState.Roar)
            {
                pokeOffset = dashVec.SafeNormalize(Vector2.UnitX) * (46f * stateTimer / RoarTime);
                //门内凶光:双层红辉光,内层脉动(内发光"心跳"越来越急)
                Texture2D glow = glowTex.Value;
                float roarP = stateTimer / (float)RoarTime;
                float pulse = 1f + (0.12f + 0.22f * roarP) * (float)Math.Sin(Main.GlobalTimeWrappedHourly * (24f + 26f * roarP));
                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
                spriteBatch.Draw(glow, portalPos - screenPos, null, new Color(255, 40, 45) * (0.35f + 0.55f * roarP), 0, glow.Size() / 2, (1.6f + roarP * 1.1f) * pulse, SpriteEffects.None, 0);
                spriteBatch.Draw(glow, portalPos - screenPos, null, new Color(255, 130, 110) * (0.5f + 0.5f * roarP), 0, glow.Size() / 2, (0.8f + roarP * 0.5f) * pulse, SpriteEffects.None, 0);
                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            }
            if (alpha <= 0.01f)
            {
                return false;
            }

            //拉伸残影(冲刺):加色鬼影 + 沿运动轴拉伸 + 首尾色散层(速度感三件套)
            if (state == SharkState.Dash && oldPos.Count > 0)
            {
                Vector2 dir = dashVec.SafeNormalize(Vector2.UnitX);
                Vector2 perp = dir.RotatedBy(MathHelper.PiOver2);
                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
                float ap = 1f / oldPos.Count;
                for (int i = 0; i < oldPos.Count; i++)
                {
                    Vector2 ghostPos = oldPos[i] - screenPos;
                    float ga = ap * 0.45f * alpha;
                    //最近两帧鬼影带红/青色散(高速运动的镜头味)
                    if (i >= oldPos.Count - 2)
                    {
                        spriteBatch.Draw(tex, ghostPos + perp * 5f, NPC.frame, new Color(255, 70, 110) * (ga * 0.65f), rotation, origin,
                            new Vector2(1.3f, 0.82f) * NPC.scale, fx, 0);
                        spriteBatch.Draw(tex, ghostPos - perp * 5f, NPC.frame, new Color(90, 160, 255) * (ga * 0.65f), rotation, origin,
                            new Vector2(1.3f, 0.82f) * NPC.scale, fx, 0);
                    }
                    spriteBatch.Draw(tex, ghostPos, NPC.frame, new Color(190, 130, 255) * ga, rotation, origin,
                        new Vector2(1.28f, 0.84f) * NPC.scale, fx, 0);
                    ap += 1f / oldPos.Count;
                }
                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            }
            spriteBatch.Draw(tex, NPC.Center + pokeOffset - screenPos, NPC.frame, drawColor * alpha, rotation, origin, NPC.scale, fx, 0);
            return false;
        }

        public override void HitEffect(NPC.HitInfo hit)
        {
            if (NPC.life > 0 || Main.dedServ)
                return;
            for (int i = 0; i < 44; i++)
            {
                var p = PRTLoader.NewParticle<PRT_Void>(NPC.Center,
                    CEUtils.randomRot().ToRotationVector2() * Main.rand.Next(0, 400) * 0.01f, Color.White, 1f);
                p.Opacity = Main.rand.Next(20, 100) * 0.01f;
            }
        }
    }
}
