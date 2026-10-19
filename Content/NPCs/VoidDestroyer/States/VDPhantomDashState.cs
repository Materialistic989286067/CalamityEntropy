using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using CalamityEntropy.Content.Projectiles.VoidDestroyer;
using InnoVault.StateMachines;
using System;
using Terraria;

namespace CalamityEntropy.Content.NPCs.VoidDestroyer.States
{
    /// <summary>
    /// 每冲:淡出 → 门后待机(开门那帧锁死航向并画航道,36 帧前摇)→ 沿航道定速冲刺 → 硬刹。
    /// 航向存 RolledAngles[1] 随包过线,出手不再重新瞄准;伤害窗是速度门槛
    /// </summary>
    [VaultState((int)VDStateIndex.PhantomDash, typeof(VDStateContext))]
    public class VDPhantomDashState : VDStateBase
    {
        public override string StateName => "PhantomDash";
        public override VDStateIndex StateIndex => VDStateIndex.PhantomDash;
        public override bool ContactByDefault => false;

        private enum Beat { FadeOut, Wait, Dash, Brake, End }
        private Beat beat;
        private int dashes;

        public override void OnEnter(VDStateContext ctx) {
            base.OnEnter(ctx);
            beat = Beat.FadeOut;
            dashes = 0;
        }

        private static Vector2 LaneDir(VDStateContext ctx) => ctx.RolledAngles[1].ToRotationVector2();

        public override IVDState OnUpdate(VDStateContext ctx) {
            Timer++;
            NPC npc = ctx.Npc;
            DeclareDirect(ctx);
            switch (beat) {
                case Beat.FadeOut:
                    npc.velocity *= 0.5f;
                    DeclareAlpha(ctx, MathHelper.Clamp(1f - Timer / (float)VDDirector.PhantomFadeFrames, 0f, 1f), 1f);
                    if (Timer >= VDDirector.PhantomFadeFrames) {
                        if (IsServer) {
                            RollDash(ctx);
                            npc.netUpdate = true;
                        }
                        SwitchBeat(Beat.Wait);
                    }
                    break;
                case Beat.Wait: {
                    npc.Center = ctx.AnchorPos;
                    npc.velocity = Vector2.Zero;
                    DeclareAlpha(ctx, 0f, 1f);
                    //门内隐身时描边先蓄满(本体透明看不见,但强度已到位),冲出的第一帧就是白热整圈
                    float p = MathHelper.Clamp(Timer / (float)VDDirector.PhantomWaitFrames, 0f, 1f);
                    ctx.RimCharge = p;
                    DeclareLane(ctx, ctx.AnchorPos, LaneDir(ctx), VDDirector.PhantomLaneLength, VDDirector.PhantomLaneWidth, 0.25f + 0.75f * p, VDDirector.RimDashIce);
                    if (Timer >= VDDirector.PhantomWaitFrames) {
                        Launch(ctx);
                        SwitchBeat(Beat.Dash);
                        MarkNetUpdate(ctx);
                    }
                    break;
                }
                case Beat.Dash: {
                    DeclareAlpha(ctx, 1f, 1f);
                    ctx.ContactWindow = npc.velocity.Length() > VDDirector.PhantomContactSpeed;
                    //冲刺全程满亮,拖尾风格沿速度反向抹开;航道在前 8 帧收掉
                    ctx.RimCharge = 1f;
                    DeclareLane(ctx, ctx.AnchorPos, LaneDir(ctx), VDDirector.PhantomLaneLength, VDDirector.PhantomLaneWidth, 1f - Timer / 8f, VDDirector.RimDashIce);
                    if (ctx.Phase >= 2 && Timer % VDDirector.PhantomTrailInterval == 3) {
                        Shoot<VDVoidBolt>(ctx, npc.Center, npc.velocity.SafeNormalize(Vector2.UnitY) * VDDirector.PhantomTrailSpeed, VDDirector.DmgVoidBolt, VDVoidBolt.ModeStraight);
                    }
                    if (Timer >= VDDirector.PhantomDashFrames) {
                        dashes++;
                        SwitchBeat(Beat.Brake);
                        MarkNetUpdate(ctx);
                    }
                    break;
                }
                case Beat.Brake:
                    //硬刹:×0.7/帧,伤害窗随速度关掉,刹车火花
                    DeclareAlpha(ctx, 1f, 1f);
                    npc.velocity *= 0.7f;
                    ctx.ContactWindow = npc.velocity.Length() > VDDirector.PhantomContactSpeed;
                    if (Timer == 1) {
                        VDVfx.SparkBurst(npc.Center, VDVfx.VoidPurple, 12, 3f, 9f, 20, 0.5f, 1f);
                    }
                    if (Timer >= VDDirector.PhantomBrakeFrames) {
                        SwitchBeat(dashes >= VDDirector.PhantomDashes(ctx.Phase) ? Beat.End : Beat.FadeOut);
                    }
                    break;
                default: {
                    float p = MathHelper.Clamp(Timer / (float)VDDirector.PhantomEndFade, 0f, 1f);
                    npc.velocity *= 0.85f;
                    DeclareAlpha(ctx, 1f - p, 1f);
                    if (Timer >= VDDirector.PhantomEndFade) {
                        npc.velocity *= 0.5f;
                        return EndAttack(ctx);
                    }
                    break;
                }
            }
            return null;
        }

        /// <summary>门开在玩家某个斜角 480px 外,航向在这一帧锁死:门 → 玩家此刻的位置</summary>
        private static void RollDash(VDStateContext ctx) {
            //四角按传送火弹同一圈序轮转,开门位置可预判
            int at = Array.IndexOf(VDDirector.TeleportFireOrder, ctx.CornerIndex);
            ctx.CornerIndex = VDDirector.TeleportFireOrder[(at + 1) % VDDirector.TeleportFireOrder.Length];
            ctx.AnchorPos = ctx.Target.Center + VDVfx.CornerDirs[ctx.CornerIndex] * VDDirector.PhantomPortalOffset;
            Vector2 dir = (ctx.Target.Center - ctx.AnchorPos).SafeNormalize(Vector2.UnitY);
            ctx.RolledAngles[1] = dir.ToRotation();
            SpawnVisual<VDPortal>(ctx, ctx.AnchorPos, dir, VDPortal.ModeDash, VDDirector.PhantomPortalLife);
        }

        /// <summary>出手:沿锁死的航向一帧定速 40</summary>
        private static void Launch(VDStateContext ctx) {
            NPC npc = ctx.Npc;
            npc.velocity = LaneDir(ctx) * VDDirector.PhantomDashSpeed;
            DeclareAlpha(ctx, 1f, 1f);
            ctx.ContactWindow = true;
            ctx.WingPulse = 1f;
            ctx.RimFlash = 1f;
            VDVfx.Sound("CruiserDash", 1f, npc.Center, 3);
            VDVfx.Shake(npc.Center, 4f, 1600f);
        }

        private void SwitchBeat(Beat next) {
            beat = next;
            ResetTimer();
        }
    }
}
