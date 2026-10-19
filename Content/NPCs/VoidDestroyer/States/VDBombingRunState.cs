using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using CalamityEntropy.Content.Projectiles.VoidDestroyer;
using InnoVault.StateMachines;
using System;
using Terraria;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.NPCs.VoidDestroyer.States
{
    /// <summary>
    /// 闪到玩家一侧上空,两艘幻影护航从门里出来成梯队,起势后整队横越玩家头顶一路投弹(炸弹继承舰速、受重力下落)。
    /// 护航拖在后上方,炸弹晚一拍落下,地面上是一道斜着扫过去的弹幕;本体全程可被打、无接触
    /// </summary>
    [VaultState((int)VDStateIndex.BombingRun, typeof(VDStateContext))]
    public class VDBombingRunState : VDStateBase
    {
        public override string StateName => "BombingRun";
        public override VDStateIndex StateIndex => VDStateIndex.BombingRun;
        public override bool ContactByDefault => false;
        public override bool NeedsRepositionBlink => true;
        public override Vector2 AnchorFor(VDStateContext ctx) => ctx.Target.Center + RunOffset(ctx, 0f);

        private enum Beat { Windup, Run, Tail }
        private Beat beat;

        public override void OnEnter(VDStateContext ctx) {
            base.OnEnter(ctx);
            beat = Beat.Windup;
        }

        /// <summary>航线上相对玩家的偏移:t 0..1 从 SideDir 一侧飞到另一侧</summary>
        private static Vector2 RunOffset(VDStateContext ctx, float t) {
            float x = ctx.SideDir * MathHelper.Lerp(VDDirector.BombRunHalfSpan, -VDDirector.BombRunHalfSpan, t);
            return new Vector2(x, VDDirector.BombRunY);
        }

        public override IVDState OnUpdate(VDStateContext ctx) {
            Timer++;
            NPC npc = ctx.Npc;
            switch (beat) {
                case Beat.Windup: {
                    DeclareHoldRelative(ctx, RunOffset(ctx, 0f), 0.1f, 0.3f, 40f);
                    float p = Timer / (float)VDDirector.BombWindup;
                    ctx.CoreGlow = Math.Max(ctx.CoreGlow, p);
                    ctx.WingPulse = Math.Max(ctx.WingPulse, p * 0.6f);
                    ctx.RimCharge = p;
                    ctx.TiltOverride = -ctx.SideDir * 0.12f * p;
                    if (Timer == 1) {
                        VDVfx.Sound("VoidAnticipation", 0.75f, npc.Center, 3, 0.9f);
                        SpawnEscorts(ctx);
                    }
                    if (Timer % 3 == 0) {
                        ConvergeSparks(ctx, VDVfx.VoidPink, 100f, 220f, 0.1f);
                    }
                    if (Timer >= VDDirector.BombWindup) {
                        VDVfx.Sound("CruiserDash", 0.8f, npc.Center, 3, 0.8f);
                        SwitchBeat(Beat.Run);
                    }
                    break;
                }
                case Beat.Run: {
                    float t = MathHelper.Clamp(Timer / (float)VDDirector.BombRunFrames, 0f, 1f);
                    DeclareHoldRelative(ctx, RunOffset(ctx, t), 0.25f, 0.45f, 70f);
                    ctx.TiltOverride = -ctx.SideDir * 0.18f;
                    ctx.RimCharge = 0.7f;
                    ctx.CoreGlow = Math.Max(ctx.CoreGlow, 0.6f);
                    if (Timer % VDDirector.BombDropInterval == 1) {
                        DropBombs(ctx);
                    }
                    if (Timer >= VDDirector.BombRunFrames) {
                        SwitchBeat(Beat.Tail);
                        MarkNetUpdate(ctx);
                    }
                    break;
                }
                default:
                    DeclareDirect(ctx);
                    npc.velocity *= 0.88f;
                    ctx.CoreGlow = 0f;
                    if (Timer >= VDDirector.BombTail) {
                        return EndAttack(ctx);
                    }
                    break;
            }
            return null;
        }

        /// <summary>护航舰的编队偏移(x 按航向取反:拖在本体后方)</summary>
        private static Vector2 EscortOffset(VDStateContext ctx, Vector2 ofs) => new Vector2(ctx.SideDir * ofs.X, ofs.Y);

        private static void SpawnEscorts(VDStateContext ctx) {
            foreach (Vector2 ofs in VDDirector.BombEscortOffsets) {
                Vector2 o = EscortOffset(ctx, ofs);
                SpawnVisual<VDEscortShip>(ctx, ctx.Npc.Center + o, Vector2.Zero, ctx.Npc.whoAmI, o.X, o.Y);
            }
        }

        /// <summary>一轮:本体一发、每艘护航一发;护航的炸弹从它当下的位置落(编队有滞后,不用理想队形点)</summary>
        private static void DropBombs(VDStateContext ctx) {
            NPC npc = ctx.Npc;
            MuzzleCue(ctx, Vector2.UnitY, 1.5f, "CruiserSpit", 1.05f, 0.7f);
            if (!IsServer) {
                return;
            }
            Vector2 vel = new Vector2(npc.velocity.X * VDDirector.BombInherit, VDDirector.BombDropSpeed);
            Shoot<VDVoidBolt>(ctx, ctx.Owner.CorePos, vel, VDDirector.DmgVoidBolt, VDVoidBolt.ModeBomb);
            int escortType = ModContent.ProjectileType<VDEscortShip>();
            foreach (Projectile p in Main.ActiveProjectiles) {
                if (p.type == escortType && (int)p.ai[0] == npc.whoAmI) {
                    Shoot<VDVoidBolt>(ctx, p.Center + new Vector2(0f, 20f), vel, VDDirector.DmgVoidBolt, VDVoidBolt.ModeBomb);
                }
            }
        }

        private void SwitchBeat(Beat next) {
            beat = next;
            ResetTimer();
        }
    }
}
