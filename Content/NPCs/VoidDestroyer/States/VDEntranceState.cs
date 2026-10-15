using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using InnoVault.StateMachines;
using System;
using Terraria;

namespace CalamityEntropy.Content.NPCs.VoidDestroyer.States
{
    /// <summary>
    /// 门开在平面锚点上;本体从门后深处沿视线逼近,始终压在门上原地放大,不朝屏幕中心滑。
    /// 170 帧后不再写相机,交给 EModPlayer 衰减
    /// </summary>
    [VaultState((int)VDStateIndex.Entrance, typeof(VDStateContext))]
    public class VDEntranceState : VDStateBase
    {
        public override string StateName => "Entrance";
        public override VDStateIndex StateIndex => VDStateIndex.Entrance;
        public override bool ContactByDefault => false;
        public override bool RunsDuringBlink => true;
        public override int TimeoutFrames => int.MaxValue;

        public override void OnEnter(VDStateContext ctx) {
            base.OnEnter(ctx);
            ctx.BlinkTimer = 0;
            ctx.Npc.velocity = Vector2.Zero;
            //从门后深处起手:深度视觉直接钉在起点,不能从平面缩过去
            ctx.Owner.SnapDepth(VDDirector.EntranceStartDepth);
        }

        public override IVDState OnUpdate(VDStateContext ctx) {
            Timer++;
            NPC npc = ctx.Npc;
            ctx.ArenaActive = false;
            ctx.WingsVisible = false;
            Vector2 gate = ctx.AnchorPos;

            int t = Timer;
            int warp = VDDirector.EntranceWarpFrames;
            int arrive = VDDirector.EntranceArriveFrame;
            if (t <= warp) {
                //门在平面上张开,本体还藏在门后
                ctx.Depth = VDDirector.EntranceStartDepth;
                PlaceOverPlanePoint(ctx, gate, ctx.Depth);
                DeclareAlpha(ctx, 0f, 1f);
                ctx.PortalOpenness = VDVfx.EaseOut(t / (float)warp);
                if (t == 1) {
                    VDVfx.Sound("portal_emerge", 1.1f, gate, 2, 0.9f);
                }
                if (t == warp / 2) {
                    VDSkyDrive.PushFlash(0.8f);
                }
            }
            else if (t <= arrive) {
                //立方缓入:大半时间是门后一点慢慢变大,最后十几帧猛冲出门
                float p = (t - warp) / (float)(arrive - warp);
                ctx.Depth = VDDirector.EntranceStartDepth * (1f - VDDepth.DiveCurve(p));
                PlaceOverPlanePoint(ctx, gate, ctx.Depth);
                DeclareAlpha(ctx, MathHelper.Clamp((t - warp) / 16f, 0f, 1f), 1f);
                ctx.PortalOpenness = 1f;
                ctx.CoreGlow = Math.Max(ctx.CoreGlow, p * 0.8f);
                ctx.RimCharge = p;
                //引擎音逐级升调:越近越尖
                int local = t - warp;
                if (local == 16 || local == 44 || local == 68) {
                    VDVfx.Sound("VoidAnticipation", 0.6f + local / 140f, gate, 3, 0.9f);
                }
                if (t == arrive) {
                    VDVfx.DiveShock(gate, 1.2f);
                    VDSkyDrive.PushFlash(0.6f);
                    ctx.CoreGlow = 1f;
                    ctx.RimFlash = 1f;
                    ctx.WingPulse = 1f;
                    ctx.ShakeStrength = 0.6f;
                }
            }
            else {
                //落定威压:门在 30 帧内收拢,本体钉在门口
                ctx.Depth = 0f;
                DeclareDirect(ctx);
                npc.velocity = Vector2.Zero;
                npc.Center = gate;
                DeclareAlpha(ctx, 1f, 1f);
                ctx.PortalOpenness = 1f - MathHelper.Clamp((t - arrive) / 30f, 0f, 1f);
                float breath = MathF.Sin((t - arrive) / (float)(VDDirector.EntranceDuration - arrive) * MathHelper.Pi);
                ctx.CoreGlow = Math.Max(ctx.CoreGlow, 0.4f + 0.5f * breath);
                if (t == arrive + 34) {
                    VDVfx.Sound("VoidAnticipation", 0.5f, gate, 2, 1.1f);
                    ctx.RimFlash = 0.8f;
                    ctx.ShakeStrength = Math.Max(ctx.ShakeStrength, 0.5f);
                    VDVfx.Shake(gate, 6f);
                }
            }

            if (t <= VDDirector.EntranceCameraFrames) {
                ctx.CameraFocus = gate + new Vector2(0, 80);
                ctx.CameraShift = t < 30 ? t / 30f * 0.12f : 0.12f;
            }

            if (t >= VDDirector.EntranceDuration) {
                return new VDHubState();
            }
            return null;
        }
    }
}
