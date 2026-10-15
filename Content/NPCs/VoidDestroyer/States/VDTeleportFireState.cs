using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using CalamityEntropy.Content.Projectiles.VoidDestroyer;
using InnoVault.StateMachines;
using System;
using Terraria;

namespace CalamityEntropy.Content.NPCs.VoidDestroyer.States
{
    /// <summary>依次闪到玩家的几个斜角,落地蓄力 18 帧后扇射一把起步慢、越飞越快的虚空弹;闪现期间计时暂停,自带传送</summary>
    [VaultState((int)VDStateIndex.TeleportFire, typeof(VDStateContext))]
    public class VDTeleportFireState : VDStateBase
    {
        public override string StateName => "TeleportFire";
        public override VDStateIndex StateIndex => VDStateIndex.TeleportFire;

        private int cornerStep;

        public override void OnEnter(VDStateContext ctx) {
            base.OnEnter(ctx);
            cornerStep = 0;
        }

        public override IVDState OnUpdate(VDStateContext ctx) {
            Timer++;
            int corners = VDDirector.TeleportFireCorners(ctx.Phase);
            if (cornerStep < corners) {
                if (Timer == 1 && IsServer) {
                    ctx.CornerIndex = VDDirector.TeleportFireOrder[cornerStep % VDDirector.TeleportFireOrder.Length];
                    ctx.Owner.StartBlink(ctx.Target.Center + VDVfx.CornerDirs[ctx.CornerIndex] * VDDirector.TeleportFireOffset);
                }
                //落地后用 18 帧做核心蓄力和汇聚流再出手,落地就给出预告
                float charge = MathHelper.Clamp(Timer / (float)VDDirector.TeleportFireShotFrame, 0f, 1f);
                ctx.CoreGlow = Math.Max(ctx.CoreGlow, charge);
                ctx.RimCharge = charge;
                if (Timer < VDDirector.TeleportFireShotFrame && Timer % 2 == 0) {
                    ConvergeSparks(ctx, VDVfx.VoidPurple, 50f, 110f, 0.14f);
                }
                if (Timer == VDDirector.TeleportFireShotFrame) {
                    Fire(ctx);
                }
                if (Timer > VDDirector.TeleportFireShotFrame) {
                    ctx.CoreGlow = 0f;
                }
                if (Timer >= VDDirector.TeleportFireCornerFrames) {
                    cornerStep++;
                    ResetTimer();
                    MarkNetUpdate(ctx);
                }
                return null;
            }
            ctx.CoreGlow = 0f;
            if (Timer >= VDDirector.TeleportFireTail) {
                return EndAttack(ctx);
            }
            return null;
        }

        /// <summary>扇形对准玩家当下位置,每颗起步 4 再逐帧加速</summary>
        private static void Fire(VDStateContext ctx) {
            Vector2 core = ctx.Owner.CorePos;
            Vector2 dir = (ctx.Target.Center - core).SafeNormalize(Vector2.UnitY);
            MuzzleCue(ctx, dir, 4f, "CruiserSpit", 0.85f);
            int half = VDDirector.TeleportFireBolts(ctx.Phase) / 2;
            for (int i = -half; i <= half; i++) {
                Vector2 vel = dir.RotatedBy(MathHelper.ToRadians(VDDirector.TeleportFireSpreadDeg * i)) * VDDirector.TeleportFireBoltSpeed;
                Shoot<VDVoidBolt>(ctx, core, vel, VDDirector.DmgVoidBolt, VDVoidBolt.ModeAccelerate);
            }
        }
    }
}
