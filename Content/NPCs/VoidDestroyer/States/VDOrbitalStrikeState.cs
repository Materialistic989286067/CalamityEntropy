using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using CalamityEntropy.Content.Projectiles.VoidDestroyer;
using InnoVault.StateMachines;
using System;
using Terraria;

namespace CalamityEntropy.Content.NPCs.VoidDestroyer.States
{
    /// <summary>
    /// 全场唯一的远景招:沿视线退入背景(表观位置从原处滑到玩家头顶,同时缩小、雾化、压到物块后)→ 落点准星 + 透视瞄准线 → 光柱错拍砸落 → 俯冲归位。
    /// 退远与归位两拍直写位置(PlaceOverPlanePoint),不让世界坐标的追赶在投影里读成「朝屏幕中心滑」
    /// </summary>
    [VaultState((int)VDStateIndex.OrbitalStrike, typeof(VDStateContext))]
    public class VDOrbitalStrikeState : VDStateBase
    {
        public override string StateName => "OrbitalStrike";
        public override VDStateIndex StateIndex => VDStateIndex.OrbitalStrike;

        private enum Beat { Ascend, Mark, Fire, Return }
        private Beat beat;
        private int pillarsFired;
        /// <summary>退远起手时本体相对目标的偏移(平面上)</summary>
        private Vector2 ascendFrom;
        /// <summary>归位起手时本体从目标视角压着的表观偏移</summary>
        private Vector2 returnFrom;

        public override void OnEnter(VDStateContext ctx) {
            base.OnEnter(ctx);
            beat = Beat.Ascend;
            pillarsFired = 0;
            ascendFrom = ctx.Npc.Center - ctx.Target.Center;
        }

        private static Vector2 LandOffset(VDStateContext ctx)
            => new Vector2(ctx.SideDir * VDDirector.OrbitalLandOffset.X, VDDirector.OrbitalLandOffset.Y);

        public override IVDState OnUpdate(VDStateContext ctx) {
            Timer++;
            NPC npc = ctx.Npc;
            int count = VDDirector.OrbitalPillars(ctx.Phase);
            switch (beat) {
                case Beat.Ascend: {
                    float p = MathHelper.Clamp(Timer / (float)VDDirector.OrbitalAscendFrames, 0f, 1f);
                    ctx.Depth = VDDirector.OrbitalFarDepth * VDDepth.RetreatCurve(p);
                    Vector2 apparent = Vector2.Lerp(ascendFrom, VDDirector.OrbitalHoverOffset, VDVfx.EaseOut(p));
                    PlaceOverPlanePoint(ctx, ctx.Target.Center + apparent, ctx.Depth);
                    if (Timer == 1) {
                        VDVfx.Sound("vbdisapear", 0.7f, npc.Center, 3, 0.9f);
                        ctx.WingPulse = 1f;
                        ctx.ShakeStrength = Math.Max(ctx.ShakeStrength, 0.4f);
                    }
                    ctx.CoreGlow = Math.Max(ctx.CoreGlow, p * 0.6f);
                    if (Timer >= VDDirector.OrbitalAscendFrames) {
                        SwitchBeat(Beat.Mark);
                    }
                    break;
                }
                case Beat.Mark:
                    ctx.Depth = VDDirector.OrbitalFarDepth;
                    DeclareHoldRelativeDepth(ctx, VDDirector.OrbitalHoverOffset, VDDirector.OrbitalFarDepth, 0.05f, 0.2f, 40f);
                    if (Timer == 1) {
                        if (IsServer) {
                            RollTargets(ctx, count);
                            for (int i = 0; i < count; i++) {
                                SpawnVisual<VDTargetReticle>(ctx, ctx.RolledPoints[i], Vector2.Zero, VDDirector.OrbitalMarkFrames + VDDirector.OrbitalPillarStagger * i, npc.whoAmI + 1);
                            }
                            npc.netUpdate = true;
                        }
                        VDVfx.Sound("VoidAnticipation", 1.1f, ctx.Target.Center, 3, 0.9f);
                    }
                    ctx.CoreGlow = Math.Max(ctx.CoreGlow, Timer / (float)VDDirector.OrbitalMarkFrames);
                    if (Timer >= VDDirector.OrbitalMarkFrames) {
                        SwitchBeat(Beat.Fire);
                    }
                    break;
                case Beat.Fire: {
                    ctx.Depth = VDDirector.OrbitalFarDepth;
                    DeclareHoldRelativeDepth(ctx, VDDirector.OrbitalHoverOffset, VDDirector.OrbitalFarDepth, 0.05f, 0.2f, 40f);
                    if (pillarsFired < count && Timer == 1 + VDDirector.OrbitalPillarStagger * pillarsFired) {
                        Vector2 point = ctx.RolledPoints[pillarsFired];
                        float sweep = VDDirector.OrbitalPillarSweep(ctx.Phase) * Math.Sign(ctx.Target.Center.X - point.X + 0.01f);
                        Shoot<VDVoidPillar>(ctx, point, new Vector2(sweep, 0f), VDDirector.DmgVoidPillar, VDDirector.OrbitalPillarLife, VDDirector.OrbitalPillarWidth, npc.whoAmI + 1);
                        ctx.WingPulse = 1f;
                        ctx.CoreGlow = 1f;
                        //每根光柱砸落,背景里的本体描边闪一下(退入纵深后描边本就减半,爆闪给足)
                        ctx.RimFlash = 1f;
                        ctx.ShakeStrength = Math.Max(ctx.ShakeStrength, 0.5f);
                        pillarsFired++;
                    }
                    if (pillarsFired >= count && Timer >= 1 + VDDirector.OrbitalPillarStagger * count + 10) {
                        returnFrom = PlanePointOf(ctx, VDDirector.OrbitalFarDepth) - ctx.Target.Center;
                        SwitchBeat(Beat.Return);
                        MarkNetUpdate(ctx);
                    }
                    break;
                }
                default: {
                    //俯冲归位:表观位置从头顶滑到落点,深度按立方曲线归零;落点大环 + 落地震屏 + 接触窗都在 DeclareDive 里
                    //先落位置再声明俯冲:落地那一帧的冲击要打在落点上
                    Vector2 land = ctx.Target.Center + LandOffset(ctx);
                    if (Timer <= VDDirector.OrbitalReturnFrames) {
                        float p = Timer / (float)VDDirector.OrbitalReturnFrames;
                        float z = VDDirector.OrbitalFarDepth * (1f - VDDepth.DiveCurve(p));
                        PlaceOverPlanePoint(ctx, ctx.Target.Center + Vector2.Lerp(returnFrom, LandOffset(ctx), VDDepth.DiveCurve(p)), z);
                    }
                    else {
                        DeclareHoverTo(ctx, land, 12f, 0.2f, 100f);
                    }
                    DeclareDive(ctx, VDDirector.OrbitalFarDepth, Timer, VDDirector.OrbitalReturnFrames, land);
                    if (Timer >= VDDirector.OrbitalReturnFrames + VDDirector.DiveContactFrames) {
                        return EndAttack(ctx);
                    }
                    break;
                }
            }
            return null;
        }

        /// <summary>落点沿玩家移动方向排布(静止时按 SideDir 横排),相邻间距 160,整体以玩家为中心</summary>
        private static void RollTargets(VDStateContext ctx, int count) {
            Player target = ctx.Target;
            Vector2 dir = target.velocity.LengthSquared() > 1f ? target.velocity.SafeNormalize(Vector2.UnitX) : new Vector2(ctx.SideDir, 0f);
            for (int i = 0; i < count; i++) {
                float offset = (i - (count - 1) * 0.5f) * VDDirector.OrbitalMarkSpacing;
                ctx.RolledPoints[i] = target.Center + dir * offset;
            }
        }

        private void SwitchBeat(Beat next) {
            beat = next;
            ResetTimer();
        }
    }
}
