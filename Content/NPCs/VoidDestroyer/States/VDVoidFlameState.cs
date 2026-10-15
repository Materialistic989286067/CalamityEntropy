using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using CalamityEntropy.Content.Projectiles.VoidDestroyer;
using InnoVault.StateMachines;
using System;
using Terraria;

namespace CalamityEntropy.Content.NPCs.VoidDestroyer.States
{
    /// <summary>
    /// 闪到玩家脚下,每轮向下扇散一把火,火随即转头向上加速穿过玩家所在高度。
    /// P2 起喷口逐轮左右换边,P3 每轮三成火晚转向,升起两层火幕
    /// </summary>
    [VaultState((int)VDStateIndex.VoidFlame, typeof(VDStateContext))]
    public class VDVoidFlameState : VDStateBase
    {
        public override string StateName => "VoidFlame";
        public override VDStateIndex StateIndex => VDStateIndex.VoidFlame;
        /// <summary>压到玩家脚下要穿过玩家,飞过去会撞人:几何必需闪现</summary>
        public override bool NeedsRepositionBlink => true;
        public override Vector2 AnchorFor(VDStateContext ctx) => ctx.Target.Center + VDDirector.FlameOffset;

        private enum Beat { Approach, Fire }
        private Beat beat;
        private int volleys;

        public override void OnEnter(VDStateContext ctx) {
            base.OnEnter(ctx);
            beat = Beat.Approach;
            volleys = 0;
        }

        /// <summary>本轮喷口的悬停偏移:P2 起逐轮左右交替</summary>
        private Vector2 HoldOffset(VDStateContext ctx) {
            if (ctx.Phase < 2) {
                return VDDirector.FlameOffset;
            }
            float side = volleys % 2 == 0 ? ctx.SideDir : -ctx.SideDir;
            return VDDirector.FlameOffset + new Vector2(side * VDDirector.FlameSideShift, 0f);
        }

        public override IVDState OnUpdate(VDStateContext ctx) {
            Timer++;
            if (beat == Beat.Approach) {
                Vector2 dest = AnchorFor(ctx);
                DeclareHoverTo(ctx, dest, VDDirector.FlameApproachSpeed, VDDirector.FlameApproachAccel, VDDirector.FlameApproachSlow);
                if (Timer >= VDDirector.FlameApproachFrames || (Timer > 8 && ctx.Npc.Distance(dest) < 80f)) {
                    beat = Beat.Fire;
                    ResetTimer();
                    MarkNetUpdate(ctx);
                }
                return null;
            }

            DeclareHoldRelative(ctx, HoldOffset(ctx), VDDirector.FlameHoldStiffness, VDDirector.FlameHoldLerp, VDDirector.FlameHoldMaxSpeed);
            Vector2 core = ctx.Owner.CorePos;
            int total = VDDirector.FlameVolleys(ctx.Phase);
            //第 k 轮在 Timer = Telegraph + Interval*k 出手,之前 30 帧是锥形预告
            int fireAt = VDDirector.FlameTelegraph + VDDirector.FlameVolleyInterval * volleys;
            if (volleys < total) {
                int untilFire = fireAt - Timer;
                if (untilFire > 0 && untilFire <= VDDirector.FlameTelegraph) {
                    float p = 1f - untilFire / (float)VDDirector.FlameTelegraph;
                    ctx.CoreGlow = Math.Max(ctx.CoreGlow, p);
                    ctx.WingPulse = Math.Max(ctx.WingPulse, p * 0.6f);
                    ctx.RimCharge = p;
                    if (untilFire == VDDirector.FlameTelegraph) {
                        VDVfx.Sound("VoidAnticipation", 1.3f, core, 4, 0.6f);
                    }
                    if (!Main.dedServ && Timer % 2 == 0) {
                        //锥形火花从核心向下喷:火会先往下散再折回来
                        Vector2 v = (MathHelper.PiOver2 + Main.rand.NextFloat(-VDDirector.FlameSpread, VDDirector.FlameSpread)).ToRotationVector2() * Main.rand.NextFloat(3f, 7f) * (0.5f + p);
                        VDVfx.SparkBurst(core, VDVfx.VoidPurple, 1, v.Length(), v.Length(), 14, 0.4f, 0.7f);
                    }
                }
                if (Timer == fireAt) {
                    FireVolley(ctx, core);
                    volleys++;
                }
            }
            else {
                ctx.CoreGlow = 0f;
            }
            if (volleys >= total && Timer >= fireAt - VDDirector.FlameVolleyInterval + VDDirector.FlameTail) {
                return EndAttack(ctx);
            }
            return null;
        }

        /// <summary>向下扇散一把火(数量服务端掷),每颗过一段延迟后转头向上加速;P3 三分之一晚转向</summary>
        private static void FireVolley(VDStateContext ctx, Vector2 core) {
            MuzzleCue(ctx, Vector2.UnitY, 4f, "CruiserSpit", 0.8f);
            if (!IsServer) {
                return;
            }
            int phase = ctx.Phase;
            ctx.RandCount = Main.rand.Next(VDDirector.FlameCountMin(phase), VDDirector.FlameCountMax(phase));
            for (int i = 0; i < ctx.RandCount; i++) {
                Vector2 vel = (MathHelper.PiOver2 + Main.rand.NextFloat(-VDDirector.FlameSpread, VDDirector.FlameSpread)).ToRotationVector2() * Main.rand.NextFloat(VDDirector.FlameSpeedMin, VDDirector.FlameSpeedMax);
                int delay = phase >= 3 && i % 3 == 0 ? VDDirector.FlameLateRiseDelay : VDDirector.FlameRiseDelay;
                Shoot<VDVoidBolt>(ctx, core, vel, VDDirector.DmgVoidBolt, VDVoidBolt.ModeFanRise, delay);
            }
            ctx.Npc.netUpdate = true;
        }
    }
}
