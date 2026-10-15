using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using CalamityEntropy.Content.Projectiles.VoidDestroyer;
using InnoVault.StateMachines;
using System;
using Terraria;

namespace CalamityEntropy.Content.NPCs.VoidDestroyer.States
{
    /// <summary>
    /// 本体悬在玩家侧上方(平面,可被打),逐个推出带缺口的能量环:环心 = 玩家预测点,先在起始半径亮出缺口再收拢。
    /// 每环缺口转一个角度,玩家要在环收到自己之前挪进新缺口;站在环心不动必中
    /// </summary>
    [VaultState((int)VDStateIndex.ClosingGates, typeof(VDStateContext))]
    public class VDClosingGatesState : VDStateBase
    {
        public override string StateName => "ClosingGates";
        public override VDStateIndex StateIndex => VDStateIndex.ClosingGates;
        public override Vector2 AnchorFor(VDStateContext ctx)
            => ctx.Target.Center + new Vector2(ctx.SideDir * VDDirector.GateHoverOffset.X, VDDirector.GateHoverOffset.Y);

        private enum Beat { Windup, Push }
        private Beat beat;
        private int ringsPushed;

        public override void OnEnter(VDStateContext ctx) {
            base.OnEnter(ctx);
            beat = Beat.Windup;
            ringsPushed = 0;
            if (IsServer) {
                ctx.RolledAngles[0] = Main.rand.NextFloat(MathHelper.TwoPi);
                MarkNetUpdate(ctx);
            }
        }

        public override IVDState OnUpdate(VDStateContext ctx) {
            Timer++;
            int rings = VDDirector.GateRings(ctx.Phase);
            Vector2 hover = new Vector2(ctx.SideDir * VDDirector.GateHoverOffset.X, VDDirector.GateHoverOffset.Y);
            DeclareHoldRelative(ctx, hover, 0.08f, 0.25f, 30f);
            switch (beat) {
                case Beat.Windup: {
                    float p = Timer / (float)VDDirector.GateWindup;
                    ctx.CoreGlow = Math.Max(ctx.CoreGlow, p);
                    ctx.WingPulse = Math.Max(ctx.WingPulse, p);
                    ctx.RimCharge = p;
                    if (Timer == 1) {
                        VDVfx.Sound("VoidAnticipation", 0.8f, ctx.Owner.CorePos, 3, 0.9f);
                    }
                    if (Timer % 3 == 0) {
                        ConvergeSparks(ctx, VDDirector.RimZoneCyan, 120f, 260f, 0.1f);
                    }
                    if (Timer >= VDDirector.GateWindup) {
                        SwitchBeat(Beat.Push);
                    }
                    break;
                }
                default: {
                    ctx.RimCharge = 0.6f;
                    if (ringsPushed < rings && Timer == 1 + VDDirector.GateInterval * ringsPushed) {
                        PushRing(ctx, ringsPushed);
                        ringsPushed++;
                    }
                    //末环收完再留一口气
                    int lastDone = 1 + VDDirector.GateInterval * (rings - 1) + VDDirector.GatePreviewFrames + VDDirector.GateCloseFrames;
                    if (ringsPushed >= rings && Timer >= lastDone + VDDirector.GateTail) {
                        return EndAttack(ctx);
                    }
                    break;
                }
            }
            return null;
        }

        /// <summary>推出第 k 环:环心 = 玩家预测点;缺口角 = 掷骰基角 + k × 转角</summary>
        private void PushRing(VDStateContext ctx, int k) {
            Vector2 toTarget = (ctx.Target.Center - ctx.Owner.CorePos).SafeNormalize(Vector2.UnitY);
            MuzzleCue(ctx, toTarget, 3f, "CruiserSpit", 0.8f, 0.9f);
            if (!IsServer) {
                return;
            }
            Vector2 center = PredictTarget(ctx, VDDirector.GateCenterLead);
            float gapAngle = MathHelper.WrapAngle(ctx.RolledAngles[0] + MathHelper.ToRadians(VDDirector.GateGapStepDeg(ctx.Phase)) * k);
            Shoot<VDGateBolt>(ctx, center, Vector2.Zero, VDDirector.DmgGateRing, gapAngle, VDDirector.GateStartRadius);
            ctx.Npc.netUpdate = true;
        }

        private void SwitchBeat(Beat next) {
            beat = next;
            ResetTimer();
        }
    }
}
