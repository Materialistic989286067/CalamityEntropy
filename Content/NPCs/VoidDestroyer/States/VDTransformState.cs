using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using InnoVault.StateMachines;
using System;
using Terraria;

namespace CalamityEntropy.Content.NPCs.VoidDestroyer.States
{
    /// <summary>全程在平面:颤抖蓄能 → 逐帧变形 → 爆闪展翼;收尾写阶段 2,签名首招钉为轨道轰炸</summary>
    [VaultState((int)VDStateIndex.Transform, typeof(VDStateContext))]
    public class VDTransformState : VDStateBase
    {
        public override string StateName => "Transform";
        public override VDStateIndex StateIndex => VDStateIndex.Transform;
        public override bool ContactByDefault => false;
        public override bool RunsDuringBlink => true;
        public override int TimeoutFrames => int.MaxValue;

        private float surfaceDepth;
        private Vector2 surfacePoint;

        public override void OnEnter(VDStateContext ctx) {
            base.OnEnter(ctx);
            ctx.BlinkTimer = 0;
            ctx.Npc.velocity = Vector2.Zero;
            (surfaceDepth, surfacePoint) = CaptureSurface(ctx);
            VDVfx.ClearOwnProjectiles();
            MarkNetUpdate(ctx);
        }

        public override IVDState OnUpdate(VDStateContext ctx) {
            Timer++;
            NPC npc = ctx.Npc;
            int t = Timer;
            bool surfacing = SurfaceBeat(ctx, surfaceDepth, surfacePoint, t);
            if (!surfacing) {
                DeclareDirect(ctx);
                npc.velocity *= 0.8f;
            }
            DeclareAlpha(ctx, 1f, 1f);
            ctx.WingsVisible = t > VDDirector.TransformBurstFrame;
            Vector2 shown = ctx.Owner.ProjectedCenter;

            if (t <= VDDirector.TransformShudderEnd) {
                //蓄能:颤抖加剧、核心亮起,起手一记后仰
                float p = t / (float)VDDirector.TransformShudderEnd;
                ctx.TransformFrame = 0;
                ctx.ShakeStrength = Math.Max(ctx.ShakeStrength, p * p * 0.7f);
                ctx.CoreGlow = Math.Max(ctx.CoreGlow, p * 0.6f);
                ctx.RimCharge = p * 0.6f;
                if (t == 1) {
                    VDVfx.Sound("VoidAnticipation", 0.9f, shown, 2);
                    if (!surfacing && ctx.TargetValid) {
                        npc.velocity += (npc.Center - ctx.Target.Center).SafeNormalize(-Vector2.UnitY) * 4f;
                    }
                }
                if (t % 3 == 0) {
                    ConvergeSparks(ctx, VDVfx.VoidPurple, 90f, 200f, 0.08f);
                }
            }
            else if (t <= VDDirector.TransformBurstFrame) {
                //重构:六帧变形图推完,汇聚流越来越密,外壳往外冒烟
                float p = (t - VDDirector.TransformShudderEnd) / (float)(VDDirector.TransformBurstFrame - VDDirector.TransformShudderEnd);
                ctx.TransformFrame = (int)MathHelper.Clamp(p * 6f, 0, 5);
                ctx.ShakeStrength = Math.Max(ctx.ShakeStrength, 0.4f + p * p * 0.5f);
                ctx.CoreGlow = Math.Max(ctx.CoreGlow, 0.6f + 0.4f * p);
                ctx.RimCharge = 0.6f + 0.4f * p;
                if (!Main.dedServ && t % 3 == 0) {
                    ConvergeSparks(ctx, VDVfx.VoidPurple, 90f, 200f, 0.08f);
                    Vector2 v = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(1f, 3f);
                    VDVfx.VoidPuff(shown + CEUtils.randomPointInCircle(50f), v, 1f, 0.6f);
                }
                if (t == VDDirector.TransformBurstFrame) {
                    ctx.RimFlash = 1f;
                    ctx.WingPulse = 1f;
                    ctx.CoreGlow = 1f;
                    if (!Main.dedServ) {
                        VDVfx.Sound("VoidAttack", 1f, shown, 2);
                        VDVfx.Shake(npc.Center, 10f);
                        VDVfx.SparkBurst(shown, VDVfx.VoidPurple, 40, 3f, 10f, 36, 0.5f, 1f);
                        VDVfx.Explosion(shown, 0.9f, 26);
                        VDSkyDrive.PushFlash(VDDirector.SkyFlashBeat);
                    }
                }
            }
            else {
                //展翼:阶段号要到收尾才写 2,这里继续钉在变形图末帧,免得闪回一阶段贴图
                ctx.TransformFrame = 5;
                ctx.WingPulse = Math.Max(ctx.WingPulse, 0.8f);
                ctx.CoreGlow = Math.Max(ctx.CoreGlow, 0.8f);
                if (t == VDDirector.TransformBurstFrame + 1) {
                    VDVfx.Sound("VoidAnticipation", 1.3f, shown, 2, 0.8f);
                }
            }

            if (t >= VDDirector.TransformDuration) {
                ctx.Phase = 2;
                //签名首招:轨道轰炸;表指针跳过 0 号槽(那正是轨道轰炸,历史闸也会拦,这里只是把意图写明)
                ctx.ForcedNextState = (int)VDStateIndex.OrbitalStrike;
                ctx.AttackIndex = 1;
                MarkNetUpdate(ctx);
                return new VDHubState();
            }
            return null;
        }
    }
}
