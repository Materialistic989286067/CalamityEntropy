using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using InnoVault.StateMachines;
using System;
using Terraria;

namespace CalamityEntropy.Content.NPCs.VoidDestroyer.States
{
    /// <summary>
    /// 平面逐级爆炸后沿视线沉进深处:压着死亡点原地缩小、缓缓上飘,代理相机钉在 RolledPoints[0](目标死了换人也不跳)。
    /// 真死走 StrikeInstantKill,联机靠击杀包
    /// </summary>
    [VaultState((int)VDStateIndex.Death, typeof(VDStateContext))]
    public class VDDeathState : VDStateBase
    {
        public override string StateName => "Death";
        public override VDStateIndex StateIndex => VDStateIndex.Death;
        public override bool ContactByDefault => false;
        public override bool RunsDuringBlink => true;
        public override int TimeoutFrames => int.MaxValue;

        private float surfaceDepth;
        private Vector2 surfacePoint;

        public override void OnEnter(VDStateContext ctx) {
            base.OnEnter(ctx);
            ctx.Dying = true;
            ctx.BlinkTimer = 0;
            (surfaceDepth, surfacePoint) = CaptureSurface(ctx);
            VDVfx.ClearOwnProjectiles();
            VDVfx.Sound("VoidAnticipation", 0.8f, ctx.Npc.Center, 2, 1.2f);
        }

        /// <summary>沉入期间压着的平面点:死亡点向上缓飘</summary>
        private Vector2 SinkPoint(VDStateContext ctx, int t)
            => ctx.AnchorPos + new Vector2(0f, -VDDirector.DeathDriftRise * Math.Max(0, t - VDDirector.DeathExplosionEnd));

        public override IVDState OnUpdate(VDStateContext ctx) {
            Timer++;
            NPC npc = ctx.Npc;
            int t = Timer;
            npc.life = Math.Max(1, npc.life);
            ctx.ArenaActive = false;
            ctx.WingsVisible = false;
            ctx.ShieldVisible = false;

            if (t <= VDDirector.DeathExplosionEnd) {
                if (!SurfaceBeat(ctx, surfaceDepth, surfacePoint, t)) {
                    DeclareDirect(ctx);
                    npc.velocity *= 0.9f;
                }
                DeclareAlpha(ctx, 1f, 1f);
                float progress = t / (float)VDDirector.DeathExplosionEnd;
                ctx.ShakeStrength = Math.Max(ctx.ShakeStrength, progress);
                int interval = Math.Max(4, 16 - t / 10);
                //舰壳失控:描边从虚空紫烧成红,每次内部爆炸都让整圈闪一下,越到后面越密
                ctx.RimCharge = progress;
                ctx.RimColorTarget = Color.Lerp(VDVfx.VoidPurple, VDDirector.RimHeatRed, progress);
                if (t % interval == 0) {
                    ctx.RimFlash = 0.6f;
                }
                if (!Main.dedServ && t % interval == 0) {
                    Vector2 pos = npc.Center + CEUtils.randomPointInCircle(70f);
                    VDVfx.Explosion(pos, Main.rand.NextFloat(0.5f, 0.9f), 30);
                    VDVfx.SparkBurst(pos, VDVfx.VoidPurple, 6, 4f, 12f, 30, 1f, 1f);
                    VDVfx.Sound("VoidBomb", Main.rand.NextFloat(0.9f, 1.1f), pos, 4, 0.6f);
                    VDVfx.Shake(npc.Center, 3f + t / 25f);
                }
                if (t == VDDirector.DeathExplosionEnd) {
                    ctx.AnchorPos = npc.Center;
                    ctx.RolledPoints[0] = ctx.TargetValid ? ctx.Target.Center : npc.Center + new Vector2(0f, 300f);
                    npc.velocity = Vector2.Zero;
                    VDVfx.Sound("vbdisapear", 0.5f, npc.Center, 2, 1.2f);
                    MarkNetUpdate(ctx);
                }
            }
            else if (t <= VDDirector.DeathDriftEnd) {
                //失去动力:平滑步进地沉进深处,一边翻滚
                float p = (t - VDDirector.DeathExplosionEnd) / (float)(VDDirector.DeathDriftEnd - VDDirector.DeathExplosionEnd);
                float s = p * p * (3f - 2f * p);
                ctx.Depth = VDDirector.DeathDriftDepth * s;
                PlaceOverPlanePoint(ctx, SinkPoint(ctx, t), ctx.Depth, ctx.RolledPoints[0]);
                ctx.TiltOverride = ctx.SideDir * 1.1f * s;
                DeclareAlpha(ctx, 1f, 1f);
                ctx.RimCharge = 1f - 0.5f * p;
                ctx.RimColorTarget = VDDirector.RimHeatRed;
                if (!Main.dedServ && t % 5 == 0) {
                    Vector2 shown = ctx.Owner.ProjectedCenter;
                    float sc = VDDepth.Scale(ctx.Owner.Depth);
                    VDVfx.VoidPuff(shown + CEUtils.randomPointInCircle(40f * sc), CEUtils.randomRot().ToRotationVector2() * 2f * sc, 1.2f * sc, 0.6f);
                    if (t % 15 == 0) {
                        VDVfx.Explosion(shown + CEUtils.randomPointInCircle(30f * sc), 0.5f * sc, 24);
                        VDVfx.Sound("VoidBomb", 1.2f, shown, 4, 0.4f);
                    }
                }
            }
            else {
                //远处连锁小爆,直到最后一炸点亮天幕
                ctx.Depth = VDDirector.DeathDriftDepth;
                PlaceOverPlanePoint(ctx, SinkPoint(ctx, t), ctx.Depth, ctx.RolledPoints[0]);
                ctx.TiltOverride = ctx.SideDir * 1.1f;
                float fade = 1f - MathHelper.Clamp((t - VDDirector.DeathFinalFlashFrame) / (float)(VDDirector.DeathFadeEnd - VDDirector.DeathFinalFlashFrame), 0f, 1f);
                DeclareAlpha(ctx, fade, 1f);
                ctx.RimCharge = fade;
                ctx.RimColorTarget = VDDirector.RimHeatRed;
                if (!Main.dedServ) {
                    Vector2 shown = ctx.Owner.ProjectedCenter;
                    float sc = VDDepth.Scale(ctx.Owner.Depth);
                    if (t < VDDirector.DeathFinalFlashFrame && t % 6 == 0) {
                        VDVfx.Explosion(shown + CEUtils.randomPointInCircle(26f * sc), Main.rand.NextFloat(0.25f, 0.45f), 22);
                        VDVfx.SparkBurst(shown, VDVfx.VoidPink, 4, 1f, 4f, 24, 0.4f, 0.8f);
                        VDVfx.Sound("VoidBomb", Main.rand.NextFloat(1.1f, 1.3f), shown, 4, 0.35f);
                    }
                    if (t == VDDirector.DeathFinalFlashFrame) {
                        //最后一炸:远处的一点白光,天幕整面亮起、冲击环从那里扩散出去
                        VDVfx.Explosion(shown, 1.2f, 40);
                        VDVfx.SparkBurst(shown, Color.White, 40, 2f, 9f, 50, 0.5f, 1.2f);
                        VDVfx.Sound("VoidBomb", 0.6f, shown, 2, 1.2f);
                        VDVfx.Shake(shown, 10f);
                        VDSkyDrive.PushFlash(VDDirector.SkyFlashBeat);
                        ctx.RimFlash = 1f;
                    }
                }
            }

            if (t >= VDDirector.DeathDuration - 1 && IsServer && !ctx.DeathPerformanceFinished) {
                //与巡游者一致:走 StrikeInstantKill,联机下由 DamageNPC 包把击杀带到各客户端
                //沉入时世界坐标被推到了视线远端,掉落要落回死亡点(此时已淡出,挪回去看不见)
                ctx.DeathPerformanceFinished = true;
                npc.Center = SinkPoint(ctx, t);
                npc.StrikeInstantKill();
                npc.netSpam = 9;
                npc.netUpdate = true;
            }
            return null;
        }
    }
}
