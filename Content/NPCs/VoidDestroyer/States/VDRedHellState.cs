using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using CalamityEntropy.Content.Projectiles.VoidDestroyer;
using InnoVault.StateMachines;
using System;
using Terraria;

namespace CalamityEntropy.Content.NPCs.VoidDestroyer.States
{
    /// <summary>
    /// 本体在玩家头顶指挥,全息红恶魔每轮换到玩家另一侧上方现身(平面,无判定):
    /// 探照光盘追脚下 → 两排三叉戟扇从红恶魔朝玩家预测点射出,第二排错半格补缝。射线出手前 6 帧保持静默
    /// </summary>
    [VaultState((int)VDStateIndex.RedHell, typeof(VDStateContext))]
    public class VDRedHellState : VDStateBase
    {
        public override string StateName => "RedHell";
        public override VDStateIndex StateIndex => VDStateIndex.RedHell;
        public override Vector2 AnchorFor(VDStateContext ctx) => ctx.Target.Center + VDDirector.RedHellHoverOffset;

        private int round;
        private bool wrapUp;

        public override void OnEnter(VDStateContext ctx) {
            base.OnEnter(ctx);
            round = 0;
            wrapUp = false;
        }

        public override IVDState OnUpdate(VDStateContext ctx) {
            Timer++;
            NPC npc = ctx.Npc;
            ctx.CoreColorTarget = VDVfx.HellRed;

            if (wrapUp) {
                ctx.HoloWrapUp = true;
                DeclareHoldRelative(ctx, VDDirector.RedHellHoverOffset, VDDirector.RedHellHoldStiffness, VDDirector.RedHellHoldLerp, VDDirector.RedHellHoldMaxSpeed);
                if (Timer >= VDDirector.RedHellTail) {
                    return EndAttack(ctx);
                }
                return null;
            }

            int t = Timer;
            if (t == 1) {
                if (IsServer) {
                    //首轮随机侧,之后每轮换边
                    ctx.SideDir = round == 0 ? (Main.rand.NextBool() ? -1 : 1) : -ctx.SideDir;
                    ctx.AnchorPos = ctx.Target.Center + new Vector2(ctx.SideDir * VDDirector.RedDevilOffset.X, VDDirector.RedDevilOffset.Y);
                    if (round == 0) {
                        SpawnVisual<VDHoloRedDevil>(ctx, ctx.AnchorPos, Vector2.Zero, npc.whoAmI);
                    }
                    //探照光盘:预警 30 帧后从红恶魔打下来
                    Shoot<VDRedRay>(ctx, ctx.Target.Center, Vector2.Zero, VDDirector.DmgRedRay, VDDirector.RedHellRayDuration, VDDirector.RedHellRayWarn, npc.whoAmI + 1);
                    npc.netUpdate = true;
                }
                VDVfx.Sound("VoidAnticipation", 0.9f, npc.Center, 3, 0.9f);
            }

            //全息红恶魔的蓄力读数:1~60 蓄满,60~90 回落
            ctx.HoloCharge = t <= 60 ? MathHelper.Clamp(t / 60f, 0f, 1f) : MathHelper.Clamp(1f - (t - 60) / 30f, 0f, 1f);

            int rayEnd = VDDirector.RedHellRayWarn + VDDirector.RedHellRayDuration;
            if (t <= rayEnd) {
                //预警期与发射期本体定住指挥
                DeclareDirect(ctx);
                npc.velocity *= 0.8f;
                float warn = MathHelper.Clamp(t / (float)VDDirector.RedHellRayWarn, 0f, 1f);
                ctx.CoreGlow = Math.Max(ctx.CoreGlow, warn);
                ctx.RimCharge = warn;
                if (t <= VDDirector.RedHellRayWarn) {
                    //汇聚流在出手前 6 帧断掉,这段静默就是预告
                    if (t < VDDirector.RedHellRayWarn - 6 && t % 2 == 0) {
                        ConvergeSparks(ctx, VDVfx.HellRed, 70f, 150f, 0.1f);
                    }
                    else if (t >= VDDirector.RedHellRayWarn - 6) {
                        ctx.ShakeStrength = Math.Max(ctx.ShakeStrength, 0.45f);
                        ctx.RimSuppress = 1f;
                    }
                }
            }
            else {
                DeclareHoldRelative(ctx, VDDirector.RedHellHoverOffset, VDDirector.RedHellHoldStiffness, VDDirector.RedHellHoldLerp, VDDirector.RedHellHoldMaxSpeed);
            }

            if (t == VDDirector.RedHellRayWarn) {
                //红恶魔开火那一帧,本体也「下令」:反冲 + 爆闪(射线本身由光盘弹幕画)
                MuzzleCue(ctx, Vector2.UnitY, 5f, null);
            }

            //三叉戟:两排交错,三阶段各 +1 并把扇面张开;FTW 追加第三排 5 根
            int n1 = VDDirector.RedHellTridents(ctx.Phase);
            float fan = VDDirector.RedHellFanDeg(ctx.Phase);
            if (t == VDDirector.RedHellTridentFrame1) {
                FireTridentRow(ctx, n1, fan, n1 - 1, 0f);
            }
            if (t == VDDirector.RedHellTridentFrame2) {
                FireTridentRow(ctx, n1 - 1, fan, n1 - 1, 0.5f);
            }
            if (Main.getGoodWorld && t == VDDirector.RedHellTridentFrameFTW) {
                FireTridentRow(ctx, 5, fan, 4, 0f);
            }

            if (t >= VDDirector.RedHellCycle) {
                round++;
                ResetTimer();
                if (round >= VDDirector.RedHellRounds) {
                    wrapUp = true;
                }
                MarkNetUpdate(ctx);
            }
            return null;
        }

        /// <summary>一排三叉戟:从红恶魔处按扇面均布射向玩家预测点,第 i 根在 -fan/2 + fan*(i+offset)/divisions 上</summary>
        private static void FireTridentRow(VDStateContext ctx, int count, float fanDeg, int divisions, float offset) {
            Vector2 from = ctx.AnchorPos;
            VDVfx.Sound("VoidAttack", 1.2f, from, 4, 0.9f);
            if (!IsServer) {
                return;
            }
            Vector2 predicted = PredictTarget(ctx, VDDirector.RedHellTridentLead);
            Vector2 dir = (predicted - from).SafeNormalize(Vector2.UnitX);
            float fan = MathHelper.ToRadians(fanDeg);
            for (int i = 0; i < count; i++) {
                float ang = -fan / 2f + fan * (i + offset) / Math.Max(1, divisions);
                Shoot<VDHoloTrident>(ctx, from, dir.RotatedBy(ang) * VDDirector.RedHellTridentSpeed, VDDirector.DmgTrident);
            }
        }
    }
}
