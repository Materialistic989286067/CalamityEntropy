using CalamityEntropy.Common;
using CalamityEntropy.Content.Buffs.PortsDoT;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using InnoVault;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
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
    /// 虚空护教骑士(void-invasion.md §2.4):主力层刺杀者,教"看瞬移读冲刺"。
    /// 循环:半透明贴近 2s → 渐隐 20t(虚空倒吸)→ 玩家侧向 260px 浮现 20t(汇聚成形)
    /// → 蓄势 25t(锁定后不修正,冲刺线渐亮 + 刃光)→ 冲刺 12t@34px/t(白闪 + 色散拉伸残影)
    /// → 硬刹 10t(刹车尘)→ 收招 40t。非冲刺期间无接触伤害(公平阀),命中上 ArmorCrunch 破防 5s。
    /// </summary>
    public class VoidTemplar : ModNPC, IVoidInvasionNPC
    {
        public override string Texture => "CalamityEntropy/Content/NPCs/VoidInvasion/Templar/1";

        //5 帧悬浮循环,只在绘制路径读取(服务器恒 null)
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Templar/", 1, 5, AssetMode = AssetMode.TextureValueArray)]
        private static Texture2D[] hoverFrames;

        public enum TemplarState : byte
        {
            Stalk,
            FadeOut,
            FadeIn,
            Windup,
            Dash,
            Brake,
            Recover,
        }

        //节拍常量(§2.4)
        private const int StalkTime = 120;
        private const int FadeTime = 20;
        private const int WindupTime = 25;
        private const int DashTime = 12;
        private const int BrakeTime = 10;
        private const int RecoverTime = 40;
        private const float DashSpeed = 34f;

        public TemplarState state = TemplarState.Stalk;
        public int stateTimer = 0;
        public Vector2 dashVec = Vector2.Zero;
        //残影与透明度均为客户端视觉,不同步
        public readonly List<Vector2> oldPos = new();
        public float drawAlpha = 0.55f;

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = 1;
            NPCID.Sets.MPAllowedEnemies[Type] = true;
        }

        public override void SetBestiary(Terraria.GameContent.Bestiary.BestiaryDatabase database, Terraria.GameContent.Bestiary.BestiaryEntry bestiaryEntry)
        {
            bestiaryEntry.Info.AddRange(new Terraria.GameContent.Bestiary.IBestiaryInfoElement[]
            {
                new Terraria.GameContent.Bestiary.FlavorTextBestiaryInfoElement("Mods.CalamityEntropy.VoidTemplarBestiary")
            });
        }

        public override void SetDefaults()
        {
            NPC.width = 90;
            NPC.height = 140;
            NPC.damage = 190;
            if (Main.expertMode)
            {
                NPC.damage += 20;
            }
            if (Main.masterMode)
            {
                NPC.damage += 20;
            }
            NPC.lifeMax = 200000;
            NPC.defense = 110;
            NPC.knockBackResist = 0f;
            NPC.aiStyle = -1;
            NPC.noGravity = true;
            NPC.noTileCollide = true;
            NPC.HitSound = SoundID.NPCHit4;
            NPC.DeathSound = SoundID.NPCDeath14;
            //主力层金币档:骚扰 2 银与精英 5 金之间取 30 银(§5.1 未列,自由裁量)
            NPC.value = Item.buyPrice(0, 0, 30, 0);
            NPC.Entropy().VoidTouchDR = 0.6f;
        }

        public override bool CheckActive()
        {
            return false;
        }

        /// <summary>非冲刺期间不造成接触伤害(§2.4 公平阀,与 AI 里 NPC.damage=0 双保险)。</summary>
        public override bool CanHitPlayer(Player target, ref int cooldownSlot)
        {
            return state == TemplarState.Dash;
        }

        public override void OnHitPlayer(Player target, Player.HurtInfo hurtInfo)
        {
            //命中上破防 5s(PortsDoT ArmorCrunch,玩家侧防御结算在 buff 类里)
            target.AddBuff(ModContent.BuffType<ArmorCrunch>(), 300);
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write((byte)state);
            writer.Write(stateTimer);
            writer.WriteVector2(dashVec);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            state = (TemplarState)reader.ReadByte();
            stateTimer = reader.ReadInt32();
            dashVec = reader.ReadVector2();
        }

        private void SwitchState(TemplarState next)
        {
            state = next;
            stateTimer = 0;
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                NPC.netUpdate = true;
            }
        }

        /// <summary>刃尖位置(双刃臂视觉锚点,刃光与蓄势粒子挂它)</summary>
        private Vector2 BladeAnchor => NPC.Center + new Vector2(NPC.direction * 52f, -14f);

        public override void AI()
        {
            //冲刺外接触伤害清零(双保险之二);defDamage 存的是 SetDefaults 定稿值
            NPC.damage = state == TemplarState.Dash ? NPC.defDamage : 0;

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
            if (state != TemplarState.Windup && state != TemplarState.Dash && state != TemplarState.Brake)
            {
                NPC.direction = NPC.spriteDirection = target.Center.X > NPC.Center.X ? 1 : -1;
            }
            stateTimer++;
            NPC.ai[0]++; //全局拍,悬浮颠簸用

            switch (state)
            {
                case TemplarState.Stalk:
                    {
                        //半透明缓慢贴近 2s(§2.4)
                        drawAlpha = MathHelper.Lerp(drawAlpha, 0.55f, 0.1f);
                        Vector2 want = (target.Center + new Vector2(0, -40f) - NPC.Center).SafeNormalize(Vector2.UnitX) * 4.5f;
                        want.Y += (float)Math.Sin(NPC.ai[0] * 0.06f) * 0.8f;
                        NPC.velocity = Vector2.Lerp(NPC.velocity, want, 0.05f);
                        if (stateTimer >= StalkTime)
                        {
                            SwitchState(TemplarState.FadeOut);
                        }
                        break;
                    }
                case TemplarState.FadeOut:
                    {
                        //原地渐隐 20t(客户端等服务端落点同步期间钳到 0,防负透明度)
                        drawAlpha = 0.55f * MathHelper.Clamp(1f - stateTimer / (float)FadeTime, 0f, 1f);
                        NPC.velocity *= 0.88f;
                        if (!Main.dedServ)
                        {
                            //倒吸消隐:塌缩环起拍 + 向体心倒吸的虚空流(密度撑满"被虚空收走"的读感)
                            if (stateTimer == 1)
                            {
                                var ring = PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, new Color(150, 70, 230), 1.5f);
                                ring.Configure(0.1f, FadeTime);
                                SoundEngine.PlaySound(SoundID.Item8 with { Pitch = 0.3f, Volume = 0.6f }, NPC.Center);
                            }
                            for (int i = 0; i < 3; i++)
                            {
                                Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(30f, 90f);
                                var p = PRTLoader.NewParticle<PRT_Void>(NPC.Center + offset, -offset * 0.12f, Color.White, 1f);
                                p.Opacity = 0.6f;
                            }
                        }
                        if (stateTimer >= FadeTime)
                        {
                            //瞬移落点服务端定:玩家侧向 260px(随机取侧),netUpdate 带位置同步
                            if (Main.netMode != NetmodeID.MultiplayerClient)
                            {
                                int side = Main.rand.NextBool() ? 1 : -1;
                                NPC.Center = target.Center + new Vector2(260f * side, Main.rand.NextFloat(-60f, 10f));
                                NPC.velocity = Vector2.Zero;
                                SwitchState(TemplarState.FadeIn);
                            }
                        }
                        break;
                    }
                case TemplarState.FadeIn:
                    {
                        //浮现 20t:虚空外爆起拍 + 持续汇聚成形(§2.4)
                        drawAlpha = 0.9f * (stateTimer / (float)FadeTime);
                        NPC.velocity = Vector2.Zero;
                        if (!Main.dedServ)
                        {
                            if (stateTimer == 1)
                            {
                                //浮现拍:外扩环 + 散射微粒(空间被顶开)
                                var ring = PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, new Color(170, 90, 255), 0.12f);
                                ring.Configure(1.5f, 16);
                                for (int i = 0; i < 8; i++)
                                {
                                    Vector2 dir = CEUtils.randomRot().ToRotationVector2();
                                    var s = PRTLoader.NewParticle<PRT_GlowSparkCal>(NPC.Center + dir * 20f, dir * Main.rand.NextFloat(4f, 8f),
                                        new Color(190, 110, 255), Main.rand.NextFloat(0.3f, 0.5f));
                                    s.Configure(false, 14, new Vector2(0.5f, 1.6f), quickShrink: true);
                                }
                            }
                            for (int i = 0; i < 3; i++)
                            {
                                Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(50f, 100f);
                                var p = PRTLoader.NewParticle<PRT_Void>(NPC.Center + offset, -offset * 0.09f, Color.White, 1f);
                                p.Opacity = 0.7f;
                            }
                        }
                        if (stateTimer >= FadeTime)
                        {
                            SwitchState(TemplarState.Windup);
                        }
                        break;
                    }
                case TemplarState.Windup:
                    {
                        drawAlpha = MathHelper.Lerp(drawAlpha, 1f, 0.2f);
                        //蓄势起手锁定玩家此刻位置,此后不修正(§2.4 可预读)
                        if (stateTimer == 1 && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            dashVec = (target.Center - NPC.Center).SafeNormalize(Vector2.UnitX * NPC.direction) * DashSpeed;
                            NPC.netUpdate = true;
                        }
                        //双刃臂后拉:向冲刺反方向小幅蓄力位移
                        NPC.velocity = -dashVec * 0.045f;
                        if (!Main.dedServ && dashVec != Vector2.Zero)
                        {
                            //刃光起拍 + 蓄势聚线(从冲刺反向汇入体心,张力方向 = 冲刺方向的"因")
                            if (stateTimer == 2)
                            {
                                var glint = PRTLoader.NewParticle<PRT_SparkleCal>(BladeAnchor, Vector2.Zero, new Color(235, 200, 255), 0.9f);
                                glint.Configure(new Color(190, 110, 255), 18, 0.12f, 1.4f);
                                SoundEngine.PlaySound(SoundID.Item29 with { Pitch = 0.55f, Volume = 0.5f }, NPC.Center);
                            }
                            if (stateTimer % 2 == 0 && stateTimer < WindupTime - 4)
                            {
                                Vector2 dir = dashVec.SafeNormalize(Vector2.UnitX);
                                Vector2 side = dir.RotatedBy(MathHelper.PiOver2) * Main.rand.NextFloat(-50f, 50f);
                                Vector2 from = NPC.Center - dir * Main.rand.NextFloat(90f, 160f) + side;
                                var line = PRTLoader.NewParticle<PRT_LineCal>(from, (NPC.Center - from) * 0.11f,
                                    new Color(190, 110, 255), Main.rand.NextFloat(0.45f, 0.75f));
                                line.Configure(false, 12);
                            }
                        }
                        if (stateTimer >= WindupTime)
                        {
                            SwitchState(TemplarState.Dash);
                            if (!Main.dedServ)
                            {
                                SoundEngine.PlaySound(new SoundStyle("CalamityEntropy/Assets/Sounds/SwiftSlice"), NPC.Center);
                            }
                        }
                        break;
                    }
                case TemplarState.Dash:
                    {
                        drawAlpha = 1f;
                        NPC.velocity = dashVec;
                        if (!Main.dedServ)
                        {
                            //出鞘拍:白闪 + 沿冲刺轴的冲击环 + 速度线 + 轻震屏(仅首拍)
                            if (stateTimer == 1)
                            {
                                var flash = PRTLoader.NewParticle<PRT_BloomCal>(NPC.Center, Vector2.Zero, Color.White, 0.35f);
                                flash.Configure(1.7f, 9);
                                var ring = PRTLoader.NewParticle<PRT_DirectionalPulseRing>(NPC.Center, Vector2.Zero,
                                    new Color(210, 140, 255), 0.2f);
                                ring.Configure(new Vector2(0.45f, 1.25f), dashVec.ToRotation(), 2.2f, 14);
                                CEUtils.SetShake(NPC.Center, 3f, 1000);
                                Vector2 dir = dashVec.SafeNormalize(Vector2.UnitX);
                                for (int i = 0; i < 10; i++)
                                {
                                    Vector2 side = dir.RotatedBy(MathHelper.PiOver2) * Main.rand.NextFloat(-60f, 60f);
                                    var line = PRTLoader.NewParticle<PRT_LineCal>(NPC.Center + side - dir * 30f,
                                        -dir * Main.rand.NextFloat(8f, 15f), new Color(220, 160, 255), Main.rand.NextFloat(0.5f, 0.9f));
                                    line.Configure(false, 10);
                                }
                            }
                            //冲刺途中的贴身虚空流(速度换粒子,§0.3 冲刺可读)
                            var p = PRTLoader.NewParticle<PRT_Void>(NPC.Center + CEUtils.randomPointInCircle(30f), -dashVec * 0.08f, Color.White, 1f);
                            p.Opacity = 0.5f;
                        }
                        if (stateTimer >= DashTime)
                        {
                            SwitchState(TemplarState.Brake);
                        }
                        break;
                    }
                case TemplarState.Brake:
                    {
                        //硬刹漂移 10t:首拍甩出刹车尘(动量被地脉吃掉的读感)
                        if (stateTimer == 1 && !Main.dedServ)
                        {
                            Vector2 dir = dashVec.SafeNormalize(Vector2.UnitX);
                            for (int i = 0; i < 6; i++)
                            {
                                var s = PRTLoader.NewParticle<PRT_GlowSparkCal>(NPC.Center + CEUtils.randomPointInCircle(24f),
                                    dir.RotatedBy(Main.rand.NextFloat(-0.7f, 0.7f)) * Main.rand.NextFloat(3f, 7f),
                                    new Color(180, 100, 255), Main.rand.NextFloat(0.3f, 0.5f));
                                s.Configure(false, 15, new Vector2(0.5f, 1.5f), quickShrink: true);
                            }
                        }
                        NPC.velocity *= 0.72f;
                        if (stateTimer >= BrakeTime)
                        {
                            SwitchState(TemplarState.Recover);
                        }
                        break;
                    }
                case TemplarState.Recover:
                    {
                        //收招 40t:输出窗口,缓慢漂浮
                        NPC.velocity *= 0.95f;
                        NPC.velocity.Y += (float)Math.Sin(NPC.ai[0] * 0.08f) * 0.04f;
                        if (stateTimer >= RecoverTime)
                        {
                            SwitchState(TemplarState.Stalk);
                        }
                        break;
                    }
            }
        }

        public override void PostAI()
        {
            //冲刺残影镜像教徒 oldPos 写法
            if (state == TemplarState.Dash)
            {
                oldPos.Add(NPC.Center);
            }
            if (oldPos.Count > 10 || (state != TemplarState.Dash && oldPos.Count > 0))
            {
                oldPos.RemoveAt(0);
            }
        }

        /// <summary>悬浮帧:8t/帧 1-2-3-4-5-4-3-2 往返;蓄势与冲刺锁帧 3(§2.4)。</summary>
        private Texture2D CurrentFrame()
        {
            if (state == TemplarState.Windup || state == TemplarState.Dash || state == TemplarState.Brake)
            {
                return hoverFrames[2];
            }
            int[] seq = { 0, 1, 2, 3, 4, 3, 2, 1 };
            return hoverFrames[seq[(int)(NPC.ai[0] / 8) % seq.Length]];
        }

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            Texture2D tex = CurrentFrame();
            //300x300 画布大留白,躯干对准 90x140 判定框;偏移量进游戏校(自由裁量)
            Vector2 drawOffset = new Vector2(0, 0);
            Vector2 drawPos = NPC.Center + drawOffset - screenPos;
            SpriteEffects fx = NPC.spriteDirection > 0 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            float alpha = drawAlpha * NPC.Opacity;

            //蓄势读招线:锁定的冲刺方向渐亮(公平阀强化,§2.4 "看瞬移读冲刺")
            if (state == TemplarState.Windup && dashVec != Vector2.Zero)
            {
                Texture2D warn = CEUtils.getExtraTex("vlbw");
                float p = stateTimer / (float)WindupTime;
                Color wc = new Color(200, 120, 255) * (0.12f + 0.4f * p * p);
                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
                spriteBatch.Draw(warn, NPC.Center - screenPos, null, wc, dashVec.ToRotation(),
                    warn.Size() / 2 * new Vector2(0, 1), new Vector2(560f / warn.Width, 0.3f + 0.25f * p), SpriteEffects.None, 0);
                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            }

            //冲刺:色散拉伸残影(加色批次;红/青两版错位 + 白芯,双份错位模拟运动模糊)
            if ((state == TemplarState.Dash || state == TemplarState.Brake) && oldPos.Count > 0)
            {
                Vector2 dir = dashVec.SafeNormalize(Vector2.UnitX);
                Vector2 perp = dir.RotatedBy(MathHelper.PiOver2);
                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
                float ap = 1f / oldPos.Count;
                for (int i = 0; i < oldPos.Count; i++)
                {
                    Vector2 ghostPos = oldPos[i] + drawOffset - screenPos;
                    float ga = ap * 0.55f * alpha;
                    //运动模糊:同一帧沿速度向前后各错位一份
                    Vector2 blur = dir * 9f * ap;
                    //色散:垂直冲刺轴的红/青偏移层
                    spriteBatch.Draw(tex, ghostPos + perp * 3.5f - blur, null, new Color(255, 70, 130) * (ga * 0.7f), NPC.rotation, tex.Size() / 2, NPC.scale, fx, 0);
                    spriteBatch.Draw(tex, ghostPos - perp * 3.5f + blur, null, new Color(90, 160, 255) * (ga * 0.7f), NPC.rotation, tex.Size() / 2, NPC.scale, fx, 0);
                    spriteBatch.Draw(tex, ghostPos, null, new Color(200, 150, 255) * ga, NPC.rotation, tex.Size() / 2, NPC.scale, fx, 0);
                    ap += 1f / oldPos.Count;
                }
                //出鞘白闪:冲刺首 3t 本体过曝白(一帧白闪语义)
                if (state == TemplarState.Dash && stateTimer <= 3)
                {
                    float flash = 1f - (stateTimer - 1) / 3f;
                    spriteBatch.Draw(tex, drawPos, null, Color.White * (0.9f * flash), NPC.rotation, tex.Size() / 2, NPC.scale * (1f + 0.08f * flash), fx, 0);
                }
                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            }
            spriteBatch.Draw(tex, drawPos, null, drawColor * alpha, NPC.rotation, tex.Size() / 2, NPC.scale, fx, 0);
            return false;
        }

        public override void HitEffect(NPC.HitInfo hit)
        {
            if (NPC.life > 0 || Main.dedServ)
                return;
            for (int i = 0; i < 48; i++)
            {
                var p = PRTLoader.NewParticle<PRT_Void>(NPC.Center,
                    CEUtils.randomRot().ToRotationVector2() * Main.rand.Next(0, 400) * 0.01f, Color.White, 1f);
                p.Opacity = Main.rand.Next(20, 100) * 0.01f;
            }
        }
    }
}
