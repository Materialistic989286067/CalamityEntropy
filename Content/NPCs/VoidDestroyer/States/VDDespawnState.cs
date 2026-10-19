using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using InnoVault.StateMachines;
using Terraria;

namespace CalamityEntropy.Content.NPCs.VoidDestroyer.States
{
    /// <summary>沿视线遁入 Z 6,压着起点原地缩小、缓缓上飘;目标回来就钉回平面、闪现回场再进 hub</summary>
    [VaultState((int)VDStateIndex.Despawn, typeof(VDStateContext))]
    public class VDDespawnState : VDStateBase
    {
        public override string StateName => "Despawn";
        public override VDStateIndex StateIndex => VDStateIndex.Despawn;
        public override bool ContactByDefault => false;
        public override bool RunsDuringBlink => true;
        public override int TimeoutFrames => int.MaxValue;

        private float startDepth;
        private Vector2 startPoint;
        private Vector2 proxy;

        public override void OnEnter(VDStateContext ctx) {
            base.OnEnter(ctx);
            ctx.BlinkTimer = 0;
            (startDepth, startPoint) = CaptureSurface(ctx);
            proxy = ctx.Target.Center;
        }

        public override IVDState OnUpdate(VDStateContext ctx) {
            Timer++;
            NPC npc = ctx.Npc;
            ctx.ArenaActive = false;
            if (ctx.TargetValid) {
                ctx.Owner.SnapDepth(0f);
                if (IsServer) {
                    ctx.Owner.StartBlink(ctx.Target.Center + new Vector2(ctx.SideDir * VDDirector.ConnectorDefaultAnchor.X, VDDirector.ConnectorDefaultAnchor.Y));
                }
                return new VDHubState();
            }

            float fade = MathHelper.Clamp(1f - Timer / (float)VDDirector.NoTargetDespawnFrames, 0f, 1f);
            DeclareAlpha(ctx, fade, 1f);
            //立方缓出:起步猛、越远越慢
            ctx.Depth = MathHelper.Lerp(startDepth, VDDirector.DespawnDepth, VDDepth.RetreatCurve(Timer / (float)VDDirector.DespawnWarpFrames));
            PlaceOverPlanePoint(ctx, startPoint + new Vector2(0f, -VDDirector.DespawnRise * Timer), ctx.Depth, proxy);
            if (Timer == 1) {
                VDVfx.Sound("vbdisapear", 0.8f, startPoint, 2);
            }

            if (Timer > VDDirector.NoTargetDespawnFrames && IsServer) {
                npc.active = false;
                npc.netUpdate = true;
            }
            return null;
        }
    }
}
