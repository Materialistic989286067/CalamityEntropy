using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using CalamityEntropy.Content.Projectiles.VoidDestroyer;
using InnoVault.StateMachines;
using System;
using Terraria;

namespace CalamityEntropy.Content.NPCs.VoidDestroyer.States
{
    /// <summary>
    /// P3 压轴:闪到玩家头顶蓄力(导引线标出扫射起点,在玩家对侧)→ 出手后本体钉死,主炮从对侧扫过正下方到玩家一侧 →
    /// 过热下沉冒烟。解法是躲到炮口水平线以上,或者沿扫射方向跑出射线尾;过热期间减伤清零,是整场最大的输出窗
    /// </summary>
    [VaultState((int)VDStateIndex.AnnihilationCannon, typeof(VDStateContext))]
    public class VDAnnihilationCannonState : VDStateBase
    {
        public override string StateName => "AnnihilationCannon";
        public override VDStateIndex StateIndex => VDStateIndex.AnnihilationCannon;
        public override bool NeedsRepositionBlink => true;
        public override Vector2 AnchorFor(VDStateContext ctx) => ctx.Target.Center + VDDirector.CannonHoverOffset;

        private enum Beat { Charge, Fire, Overheat }
        private Beat beat;

        public override void OnEnter(VDStateContext ctx) {
            base.OnEnter(ctx);
            beat = Beat.Charge;
        }

        public override IVDState OnUpdate(VDStateContext ctx) {
            Timer++;
            NPC npc = ctx.Npc;
            switch (beat) {
                case Beat.Charge:
                    UpdateCharge(ctx, npc);
                    break;
                case Beat.Fire:
                    //出手后钉死:炮口不跟人,玩家才能绕到它的水平线以上
                    DeclareDirect(ctx);
                    npc.velocity *= 0.85f;
                    ctx.WingPulse = 1f;
                    ctx.CoreGlow = 1f;
                    //扫射全程描边保持过热白炽(过热风格自带高频闪)
                    ctx.RimCharge = 1f;
                    ctx.RimColorTarget = VDVfx.CannonCore;
                    ctx.ShakeStrength = Math.Max(ctx.ShakeStrength, 0.3f);
                    VDScreenFx.ReportVignette(VDDirector.CannonVignette);
                    VDSkyDrive.ReportCharge(VDDirector.SkyCannonSweepCharge);
                    if (Timer >= VDDirector.CannonSweepFrames) {
                        SwitchBeat(Beat.Overheat);
                        VDVfx.Sound("VoidBomb", 0.6f, npc.Center, 2, 1f);
                        VDVfx.SparkBurst(ctx.Owner.CorePos, VDVfx.VoidPink, 40, 3f, 12f, 40);
                        MarkNetUpdate(ctx);
                    }
                    break;
                default: {
                    //过热:下沉冒烟,无接触,减伤清零(破绽窗)
                    DeclareDirect(ctx);
                    ctx.ContactWindow = false;
                    ctx.Vulnerable = true;
                    float p = MathHelper.Clamp(Timer / (float)VDDirector.CannonOverheatFrames, 0f, 1f);
                    float sink = VDDirector.CannonOverheatSink * (1f - p);
                    npc.velocity = Vector2.Lerp(npc.velocity, new Vector2(0f, sink), 0.15f);
                    ctx.CoreGlow = 0.25f + 0.25f * MathF.Sin(Timer * 0.6f);
                    //过热的舰壳从烧红慢慢冷回虚空紫,描边跟着淡回去
                    ctx.RimCharge = Math.Max(ctx.RimCharge, 0.6f * (1f - p));
                    ctx.RimColorTarget = Color.Lerp(VDDirector.RimHeatRed, VDVfx.VoidPurple, p);
                    ctx.ShakeStrength = Math.Max(ctx.ShakeStrength, 0.25f * (1f - p));
                    VDScreenFx.ReportVignette(VDDirector.CannonVignette * (1f - p));
                    VDSkyDrive.ReportCharge(VDDirector.SkyCannonSweepCharge * (1f - p));
                    if (!Main.dedServ && Timer % 3 == 0) {
                        Vector2 pos = ctx.Owner.CorePos + CEUtils.randomPointInCircle(40f);
                        VDVfx.SparkBurst(pos, VDVfx.VoidPink, 2, 2f, 6f, 24, 0.4f, 0.9f);
                        VDVfx.VoidPuff(pos, new Vector2(Main.rand.NextFloat(-1f, 1f), -Main.rand.NextFloat(1f, 3f)), 1.3f, 0.6f);
                    }
                    if (Timer >= VDDirector.CannonOverheatFrames) {
                        return EndAttack(ctx);
                    }
                    break;
                }
            }
            return null;
        }

        private void UpdateCharge(VDStateContext ctx, NPC npc) {
            DeclareHoldRelative(ctx, VDDirector.CannonHoverOffset, 0.08f, 0.25f, 24f);
            float progress = MathHelper.Clamp(Timer / (float)VDDirector.CannonChargeFrames, 0f, 1f);
            ctx.WingPulse = Math.Max(ctx.WingPulse, progress);
            ctx.CoreGlow = Math.Max(ctx.CoreGlow, progress);
            //描边随蓄力从虚空粉烧到炮芯色,出手前整圈已是白炽
            ctx.RimCharge = progress;
            ctx.RimColorTarget = Color.Lerp(VDVfx.VoidPink, VDVfx.CannonCore, progress);
            ctx.ShakeStrength = Math.Max(ctx.ShakeStrength, progress * progress * progress);
            VDScreenFx.ReportVignette(VDDirector.CannonVignette * progress);
            //天幕:力场随蓄力爬亮、本体周围六边形逐格填实、星野向本体吸入
            VDSkyDrive.ReportCharge(progress);

            if (Timer == 1) {
                VDVfx.Sound("VoidAnticipation", 0.55f, npc.Center, 2, 1.2f);
            }
            //导引线拍:出手前 40 帧掷扫射起点(玩家对侧),之后死向
            int guideStart = VDDirector.CannonChargeFrames - VDDirector.CannonGuideLead;
            if (Timer == guideStart && IsServer) {
                ctx.SideDir = ctx.Target.Center.X >= npc.Center.X ? 1 : -1;
                ctx.RolledAngles[0] = MathHelper.PiOver2 + ctx.SideDir * MathHelper.ToRadians(VDDirector.CannonStartOffsetDeg);
                npc.netUpdate = true;
            }
            if (Timer >= guideStart) {
                float g = (Timer - guideStart) / (float)VDDirector.CannonGuideLead;
                ctx.AimLineDir = ctx.RolledAngles[0].ToRotationVector2();
                ctx.AimLineStrength = MathHelper.Clamp(g, 0f, 1f);
                ctx.AimLineColor = VDVfx.CannonCore;
            }
            //汇聚流:密度随蓄力升,72% 处硬切成静默
            if (progress < VDDirector.CannonSilenceAt) {
                int every = progress < 0.4f ? 3 : 1;
                if (Timer % every == 0) {
                    ConvergeSparks(ctx, VDVfx.VoidPurple, 160f, 420f, 0.07f);
                    ConvergeSparks(ctx, VDVfx.CannonCore, 90f, 200f, 0.1f);
                }
                if (Timer % 6 == 0) {
                    VDVfx.Shake(npc.Center, 1.5f + 4f * progress * progress * progress, 3000f);
                }
            }
            if (Timer >= VDDirector.CannonChargeFrames) {
                Fire(ctx, npc);
                SwitchBeat(Beat.Fire);
                MarkNetUpdate(ctx);
            }
        }

        /// <summary>出手:射线弹幕自己每帧钉在核心上</summary>
        private void Fire(VDStateContext ctx, NPC npc) {
            float start = ctx.RolledAngles[0];
            float sweepSign = -ctx.SideDir;
            MuzzleCue(ctx, start.ToRotationVector2(), VDDirector.CannonRecoil, "VoidAttack", 0.5f, 1.2f);
            VDVfx.Shake(npc.Center, 14f, 4000f);
            Vector2 core = ctx.Owner.CorePos;
            VDVfx.Explosion(core, 1.3f, 20);
            VDVfx.SparkBurst(core, VDVfx.CannonCore, 50, 6f, 22f, 40);
            //天幕:出手一帧力场整面亮起,冲击环从本体扩散
            VDSkyDrive.PushFlash(VDDirector.SkyFlashBeat);
            Shoot<VDAnnihilationBeam>(ctx, core, start.ToRotationVector2(), VDDirector.DmgAnnihilationBeam, npc.whoAmI, VDDirector.CannonSweepFrames * sweepSign, start);
        }

        private void SwitchBeat(Beat next) {
            beat = next;
            ResetTimer();
        }
    }
}
