using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using CalamityEntropy.Content.Projectiles.VoidDestroyer;
using InnoVault.StateMachines;
using System;
using Terraria;

namespace CalamityEntropy.Content.NPCs.VoidDestroyer.States
{
    /// <summary>
    /// 玩家四周开四扇门,真身占其中一扇(门环更亮、核心与描边烧起),其余是幻影舰。
    /// 四舰的航道跟着玩家预测点瞄,出手前 12 帧把交汇点锁进 RolledPoints[4],之后四条航道不再动;第二波门位转 45°
    /// 门位在 RolledPoints[0..3],真身门号在 RandCount
    /// </summary>
    [VaultState((int)VDStateIndex.PhantomFleet, typeof(VDStateContext))]
    public class VDPhantomFleetState : VDStateBase
    {
        public override string StateName => "PhantomFleet";
        public override VDStateIndex StateIndex => VDStateIndex.PhantomFleet;
        public override bool ContactByDefault => false;

        private enum Beat { FadeOut, Aim, Dash, WaveHold, End }
        private Beat beat;
        private int wave;

        public override void OnEnter(VDStateContext ctx) {
            base.OnEnter(ctx);
            beat = Beat.FadeOut;
            wave = 0;
        }

        /// <summary>交汇点是否已锁(出手前 FleetLockLead 帧起)</summary>
        public static bool CrossLocked(int aimTimer) => aimTimer >= VDDirector.FleetAimFrames - VDDirector.FleetLockLead;

        public override IVDState OnUpdate(VDStateContext ctx) {
            Timer++;
            NPC npc = ctx.Npc;
            DeclareDirect(ctx);
            switch (beat) {
                case Beat.FadeOut:
                    npc.velocity *= 0.6f;
                    DeclareAlpha(ctx, MathHelper.Clamp(1f - Timer / (float)VDDirector.FleetFadeFrames, 0f, 1f), 1f);
                    if (Timer == 1 && IsServer) {
                        //首帧就掷:门位与真身位要在淡出的 8 帧里过线到客户端
                        RollFormation(ctx);
                        npc.netUpdate = true;
                    }
                    if (Timer >= VDDirector.FleetFadeFrames) {
                        OpenPortals(ctx);
                        SwitchBeat(Beat.Aim);
                    }
                    break;
                case Beat.Aim: {
                    npc.Center = ctx.RolledPoints[Math.Clamp(ctx.RandCount, 0, 3)];
                    npc.velocity = Vector2.Zero;
                    DeclareAlpha(ctx, MathHelper.Clamp(Timer / 8f, 0f, 1f), 1f);
                    //真身破绽:核心拉满、翼随瞄准进度张开、描边烧起(幻影没有这三样)
                    float aimP = MathHelper.Clamp(Timer / (float)VDDirector.FleetAimFrames, 0f, 1f);
                    ctx.CoreGlow = 1f;
                    ctx.WingPulse = Math.Max(ctx.WingPulse, aimP);
                    ctx.RimCharge = aimP;
                    if (Timer % 3 == 0) {
                        ConvergeSparks(ctx, VDVfx.VoidWhite, 50f, 120f, 0.14f);
                    }
                    if (Timer == VDDirector.FleetAimFrames - VDDirector.FleetLockLead && IsServer) {
                        ctx.RolledPoints[4] = PredictTarget(ctx, VDDirector.FleetCrossLead);
                        MarkNetUpdate(ctx);
                    }
                    bool locked = CrossLocked(Timer);
                    Vector2 cross = locked ? ctx.RolledPoints[4] : PredictTarget(ctx, VDDirector.FleetCrossLead);
                    ctx.DiveMarkerPos = cross;
                    ctx.DiveMarkerProgress = aimP;
                    Vector2 toCross = cross - npc.Center;
                    DeclareLane(ctx, npc.Center, toCross, toCross.Length() + 500f, VDDirector.FleetLaneWidth, locked ? 1f : 0.2f + 0.5f * aimP, VDDirector.RimDashIce);
                    if (Timer >= VDDirector.FleetAimFrames) {
                        npc.velocity = (ctx.RolledPoints[4] - npc.Center) / VDDirector.FleetCrossFrame;
                        ctx.WingPulse = 1f;
                        ctx.RimFlash = 1f;
                        VDVfx.Sound("CruiserDash", 0.9f, npc.Center, 3);
                        VDVfx.Shake(npc.Center, 5f, 1800f);
                        SwitchBeat(Beat.Dash);
                        MarkNetUpdate(ctx);
                    }
                    break;
                }
                case Beat.Dash: {
                    DeclareAlpha(ctx, 1f, 1f);
                    ctx.ContactWindow = npc.velocity.Length() > VDDirector.PhantomContactSpeed * 0.9f;
                    ctx.RimCharge = 1f;
                    if (Timer <= VDDirector.FleetCrossFrame) {
                        ctx.DiveMarkerPos = ctx.RolledPoints[4];
                        ctx.DiveMarkerProgress = Timer / (float)VDDirector.FleetCrossFrame;
                    }
                    if (Timer == VDDirector.FleetCrossFrame) {
                        //四舰同帧交汇的一记
                        VDVfx.Shake(npc.Center, 6f, 1800f);
                        VDVfx.SparkBurst(npc.Center, VDVfx.VoidWhite, 20, 4f, 12f, 22, 0.5f, 1f);
                        VDVfx.Sound("VoidAttack", 1.1f, npc.Center, 3, 0.7f);
                    }
                    if (ctx.Phase >= 3 && Timer % VDDirector.FleetTrailInterval == 3) {
                        Shoot<VDVoidBolt>(ctx, npc.Center, npc.velocity.SafeNormalize(Vector2.UnitY) * VDDirector.PhantomTrailSpeed, VDDirector.DmgVoidBolt, VDVoidBolt.ModeAccelerate);
                    }
                    if (Timer >= VDDirector.FleetDashFrames) {
                        wave++;
                        SwitchBeat(wave >= VDDirector.FleetWaves(ctx.Phase) ? Beat.End : Beat.WaveHold);
                        MarkNetUpdate(ctx);
                    }
                    break;
                }
                case Beat.WaveHold:
                    //两波之间全员静止:刹停、核心熄
                    DeclareAlpha(ctx, 1f, 1f);
                    npc.velocity *= 0.75f;
                    ctx.CoreGlow = 0f;
                    if (Timer >= VDDirector.FleetWaveHold) {
                        SwitchBeat(Beat.FadeOut);
                    }
                    break;
                default: {
                    float p = MathHelper.Clamp(Timer / (float)VDDirector.FleetEndFade, 0f, 1f);
                    npc.velocity *= 0.85f;
                    DeclareAlpha(ctx, 1f - p, 1f);
                    if (Timer >= VDDirector.FleetEndFade) {
                        npc.velocity *= 0.5f;
                        return EndAttack(ctx);
                    }
                    break;
                }
            }
            return null;
        }

        /// <summary>四门均布在玩家周围 520px 的圆上,第二波转 45°;真身门号服务端掷</summary>
        private void RollFormation(VDStateContext ctx) {
            float baseAng = (wave % 2 == 0 ? 0f : MathHelper.PiOver4) + MathHelper.PiOver4 * 0.5f;
            ctx.RandCount = Main.rand.Next(VDDirector.FleetShipCount);
            for (int i = 0; i < VDDirector.FleetShipCount; i++) {
                float ang = baseAng + MathHelper.TwoPi * i / VDDirector.FleetShipCount;
                ctx.RolledPoints[i] = ctx.Target.Center + ang.ToRotationVector2() * VDDirector.FleetPortalRadius;
            }
            ctx.RolledPoints[4] = PredictTarget(ctx, VDDirector.FleetCrossLead);
        }

        /// <summary>开门 + 放幻影:门朝玩家,真身门带更亮的环(ai[2] 亮度倍率)</summary>
        private void OpenPortals(VDStateContext ctx) {
            if (!IsServer) {
                return;
            }
            for (int i = 0; i < VDDirector.FleetShipCount; i++) {
                Vector2 pos = ctx.RolledPoints[i];
                Vector2 dir = (ctx.Target.Center - pos).SafeNormalize(Vector2.UnitY);
                bool real = i == ctx.RandCount;
                SpawnVisual<VDPortal>(ctx, pos, dir, VDPortal.ModeDash, VDDirector.FleetPortalLife, real ? VDDirector.FleetRealPortalGlow : 1f);
                if (!real) {
                    Shoot<VDPhantomShip>(ctx, pos, Vector2.Zero, VDDirector.DmgPhantomShip, ctx.Npc.whoAmI, VDDirector.FleetAimFrames, ctx.Npc.target);
                }
            }
        }

        private void SwitchBeat(Beat next) {
            beat = next;
            ResetTimer();
        }
    }
}
