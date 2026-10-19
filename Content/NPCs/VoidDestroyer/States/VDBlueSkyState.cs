using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using CalamityEntropy.Content.Projectiles.VoidDestroyer;
using InnoVault.StateMachines;
using System;
using Terraria;

namespace CalamityEntropy.Content.NPCs.VoidDestroyer.States
{
    /// <summary>
    /// 全息小白龙在玩家上方 50 格处横向飞过两程,每程沿路落一排雪花(第二程错半格),本体搁在一旁待机。
    /// 龙的高度与中心在放出那一刻锁死,不跟人;节拍由龙自管,这里只管放出与收招
    /// </summary>
    [VaultState((int)VDStateIndex.BlueSky, typeof(VDStateContext))]
    public class VDBlueSkyState : VDStateBase
    {
        public override string StateName => "BlueSky";
        public override VDStateIndex StateIndex => VDStateIndex.BlueSky;
        public override Vector2 AnchorFor(VDStateContext ctx)
            => ctx.Target.Center + new Vector2(ctx.SideDir * VDDirector.SkyIdleOffset.X, VDDirector.SkyIdleOffset.Y);

        /// <summary>放出 + 两程横越 + 末程最后一朵雪花落到玩家高度 + 收招</summary>
        private static int Duration
            => VDDirector.SkyWyvernFrame + VDDirector.SkyPassStride * (VDDirector.SkyPasses - 1) + VDDirector.SkyPassFrames
                + VDDirector.SkyFlakeFallFrames + VDDirector.SkyTail;

        public override IVDState OnUpdate(VDStateContext ctx) {
            Timer++;
            NPC npc = ctx.Npc;
            ctx.CoreColorTarget = VDVfx.SkyBlue;
            DeclareHoldRelative(ctx, new Vector2(ctx.SideDir * VDDirector.SkyIdleOffset.X, VDDirector.SkyIdleOffset.Y), 0.1f, 0.3f, 30f);

            ctx.CoreGlow = Math.Max(ctx.CoreGlow, MathHelper.Clamp(Timer / (float)VDDirector.SkyWyvernFrame, 0f, 1f));
            if (Timer < VDDirector.SkyWyvernFrame && Timer % 3 == 0) {
                ConvergeSparks(ctx, VDVfx.SkyBlue, 80f, 170f, 0.1f);
            }
            if (Timer == VDDirector.SkyWyvernFrame) {
                ctx.CoreGlow = 1f;
                ctx.WingPulse = 1f;
                ctx.RimFlash = 1f;
                VDVfx.Sound("VoidAnticipation", 0.75f, npc.Center, 3, 1f);
                if (IsServer) {
                    Vector2 target = ctx.Target.Center;
                    Shoot<VDHoloWyvern>(ctx, npc.Center, Vector2.Zero, VDDirector.DmgHoloBeast, npc.whoAmI, target.X, target.Y - VDDirector.SkyPassHeight);
                }
            }
            if (Timer >= Duration) {
                return EndAttack(ctx);
            }
            return null;
        }
    }
}
