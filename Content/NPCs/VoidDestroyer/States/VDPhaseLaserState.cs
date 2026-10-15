using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using CalamityEntropy.Content.Projectiles.VoidDestroyer;
using InnoVault.StateMachines;
using System;
using Terraria;

namespace CalamityEntropy.Content.NPCs.VoidDestroyer.States
{
    /// <summary>
    /// 无人机环绕起势 → 闪到玩家头顶 → 左右交替的无人机纵列横扫(每列空出一个安全口,口不在玩家当下那一格)→ 井字网收尾。
    /// 网心在第一轮落下时记进 RolledPoints[3],P3 第二轮绕同一网心错半格,玩家刚站进的格心正好变成交点
    /// </summary>
    [VaultState((int)VDStateIndex.PhaseLaser, typeof(VDStateContext))]
    public class VDPhaseLaserState : VDStateBase
    {
        public override string StateName => "PhaseLaser";
        public override VDStateIndex StateIndex => VDStateIndex.PhaseLaser;

        private enum Beat { Decor, Columns, Lattice }
        private Beat beat;
        private int columnsDone;
        private int pulsesFired;

        public override void OnEnter(VDStateContext ctx) {
            base.OnEnter(ctx);
            beat = Beat.Decor;
            columnsDone = 0;
            pulsesFired = 0;
        }

        /// <summary>第 k 轮井字网的起手帧(拍内计时):一轮 = 预警 + 判定 + 轮间隔</summary>
        private static int PulseStart(int k) => 1 + k * (VDDirector.LatticeWarn + VDDirector.LatticeStrikeFrames + VDDirector.LatticePulseGap);

        public override IVDState OnUpdate(VDStateContext ctx) {
            Timer++;
            bool ex = ctx.Phase >= 2;
            int columns = VDDirector.LaserColumns(ctx.Phase);
            switch (beat) {
                case Beat.Decor:
                    if (Timer == 1) {
                        for (int i = 0; i < VDDirector.LaserDecorDrones; i++) {
                            SpawnVisual<VDSporeDrone>(ctx, ctx.Npc.Center, Vector2.Zero, VDSporeDrone.ModeDecor, MathHelper.TwoPi * i / VDDirector.LaserDecorDrones, ctx.Npc.whoAmI);
                        }
                        VDVfx.Sound("VoidAnticipation", 1.2f, ctx.Npc.Center, 3, 0.8f);
                    }
                    ctx.CoreGlow = Math.Max(ctx.CoreGlow, Timer / (float)VDDirector.LaserDecorFrames);
                    if (Timer >= VDDirector.LaserDecorFrames) {
                        if (IsServer) {
                            ctx.Owner.StartBlink(ctx.Target.Center + VDDirector.LaserHoverOffset);
                        }
                        SwitchBeat(Beat.Columns);
                    }
                    break;
                case Beat.Columns:
                    DeclareHoldRelative(ctx, VDDirector.LaserHoverOffset, 0.08f, 0.25f, 30f);
                    if (columnsDone < columns && Timer >= VDDirector.LaserColumnTime(columnsDone, ex)) {
                        ctx.WingPulse = Math.Max(ctx.WingPulse, 0.6f);
                        ctx.RimFlash = Math.Max(ctx.RimFlash, 0.4f);
                        SpawnColumn(ctx, columnsDone);
                        columnsDone++;
                    }
                    if (columnsDone >= columns && Timer >= VDDirector.LaserColumnTime(columns, ex) + VDDirector.LaserColumnsToGrid) {
                        SwitchBeat(Beat.Lattice);
                        MarkNetUpdate(ctx);
                    }
                    break;
                default: {
                    DeclareHoldRelative(ctx, VDDirector.LaserHoverOffset, 0.08f, 0.25f, 30f);
                    int pulses = VDDirector.LatticePulses(ctx.Phase);
                    //起手:本体翼张核心亮(它在指挥这张网)
                    if (pulsesFired < pulses && Timer == PulseStart(pulsesFired)) {
                        ctx.WingPulse = 1f;
                        ctx.CoreGlow = 1f;
                        ctx.RimFlash = 0.6f;
                        VDVfx.Sound("VoidAnticipation", 1.3f, ctx.Target.Center, 3, 0.9f);
                        if (IsServer) {
                            //第一轮网心就是玩家当下的位置:人正踩在交点上,必须斜挪进格心;第二轮沿用同一网心错半格
                            if (pulsesFired == 0) {
                                ctx.RolledPoints[3] = ctx.Target.Center;
                                MarkNetUpdate(ctx);
                            }
                            Shoot<VDLatticeField>(ctx, ctx.RolledPoints[3], Vector2.Zero, VDDirector.DmgSporeLaser, VDDirector.LatticeWarn, 0f, pulsesFired % 2);
                        }
                        pulsesFired++;
                    }
                    //预警期核心随蓄力爬亮,打下那一帧爆闪 + 反冲
                    for (int k = 0; k < pulsesFired; k++) {
                        int since = Timer - PulseStart(k);
                        if (since >= 0 && since < VDDirector.LatticeWarn) {
                            ctx.CoreGlow = Math.Max(ctx.CoreGlow, 0.3f + 0.7f * since / VDDirector.LatticeWarn);
                            ctx.RimCharge = Math.Max(ctx.RimCharge, since / (float)VDDirector.LatticeWarn);
                        }
                        else if (since == VDDirector.LatticeWarn) {
                            MuzzleCue(ctx, Vector2.UnitY, 4f, null);
                            ctx.ShakeStrength = Math.Max(ctx.ShakeStrength, 0.5f);
                        }
                    }
                    int lastStrike = PulseStart(pulses - 1) + VDDirector.LatticeWarn + VDDirector.LatticeStrikeFrames;
                    if (pulsesFired >= pulses && Timer >= lastStrike + VDDirector.LatticeTail) {
                        return EndAttack(ctx);
                    }
                    break;
                }
            }
            return null;
        }

        /// <summary>
        /// 第 k 列:偶数列在玩家一侧朝里打、奇数列换另一侧;9 架以玩家当下的 y 为中心排开,
        /// 服务端掷一个连续 LaserGapDrones 架的空位当安全口,空位不含正中那一架(玩家所在的那格必须离开)
        /// </summary>
        private static void SpawnColumn(VDStateContext ctx, int k) {
            if (!IsServer) {
                return;
            }
            int side = (k % 2 == 0 ? -1 : 1) * ctx.SideDir;
            float x = ctx.Target.Center.X + side * VDDirector.LaserColumnX;
            float mode = side < 0 ? VDSporeDrone.ModeFireRight : VDSporeDrone.ModeFireLeft;
            int drones = VDDirector.LaserColumnDrones;
            int mid = drones / 2;
            int gapStart;
            do {
                gapStart = Main.rand.Next(drones - VDDirector.LaserGapDrones + 1);
            } while (mid >= gapStart && mid < gapStart + VDDirector.LaserGapDrones);
            for (int i = 0; i < drones; i++) {
                if (i >= gapStart && i < gapStart + VDDirector.LaserGapDrones) {
                    continue;
                }
                Vector2 pos = new Vector2(x, ctx.Target.Center.Y + (i - mid) * VDDirector.LaserDroneSpacing);
                Shoot<VDSporeDrone>(ctx, pos, Vector2.Zero, VDDirector.DmgSporeLaser, mode, VDDirector.LaserWarnTime, VDDirector.LaserColumnLength);
            }
        }

        private void SwitchBeat(Beat next) {
            beat = next;
            ResetTimer();
        }
    }
}
