using CalamityEntropy.Content.NPCs.VoidDestroyer.Core;
using CalamityEntropy.Content.Projectiles.VoidDestroyer;
using InnoVault.StateMachines;
using System;
using Terraria;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.NPCs.VoidDestroyer.States
{
    /// <summary>门槛写在 Substitute 里,舱落地时放出教徒,场上最多 8 个</summary>
    [VaultState((int)VDStateIndex.Reinforcement, typeof(VDStateContext))]
    public class VDReinforcementState : VDStateBase
    {
        public override string StateName => "Reinforcement";
        public override VDStateIndex StateIndex => VDStateIndex.Reinforcement;
        public override Vector2 AnchorFor(VDStateContext ctx) => ctx.Target.Center + VDDirector.ReinforceHoverOffset;

        public override IVDState OnUpdate(VDStateContext ctx) {
            Timer++;
            DeclareHoldRelative(ctx, VDDirector.ReinforceHoverOffset, VDDirector.ReinforceHoldStiffness, VDDirector.ReinforceHoldLerp, VDDirector.ReinforceHoldMaxSpeed);
            ctx.CoreGlow = Math.Max(ctx.CoreGlow, MathHelper.Clamp(Timer / (float)VDDirector.ReinforceSpawnFrame, 0f, 1f));
            ctx.RimCharge = MathHelper.Clamp(Timer / (float)VDDirector.ReinforceSpawnFrame, 0f, 1f);
            //开门 + 放舱(14 帧),舱从门正上方坠落 30 帧到门上(44 帧):门与落点环在地上亮着,舱拖着尾焰砸下来
            if (Timer == VDDirector.ReinforcePortalFrame) {
                VDVfx.Sound("VoidAnticipation", 0.9f, ctx.Target.Center, 3, 0.8f);
                ctx.WingPulse = Math.Max(ctx.WingPulse, 0.7f);
                if (IsServer) {
                    RollDropPoints(ctx);
                    ctx.Npc.netUpdate = true;
                }
            }
            if (Timer == VDDirector.ReinforceSpawnFrame) {
                //落地拍:本体只做表现,教徒由舱放出
                ctx.WingPulse = 1f;
                ctx.CoreGlow = 1f;
                ctx.RimFlash = 1f;
                VDVfx.Sound("VoidAttack", 0.7f, ctx.Npc.Center, 3, 0.8f);
            }
            if (Timer > VDDirector.ReinforceSpawnFrame) {
                ctx.CoreGlow = 0f;
            }
            if (Timer >= VDDirector.ReinforceDuration) {
                return EndAttack(ctx);
            }
            return null;
        }

        /// <summary>服务端掷落点、开门、放舱:RolledPoints[i] 为第 i 个落点,RandCount 为数量;舱生成在落点正上方,ai[2] 带落点 y,落地帧 = 开门帧 + 30</summary>
        private static void RollDropPoints(VDStateContext ctx) {
            int type = ModContent.NPCType<VoidVanguardCultist>();
            int alive = NPC.CountNPCS(type);
            int count = Math.Clamp(Math.Min(VDDirector.ReinforceCount(), VDDirector.MaxVanguards - alive), 0, ctx.RolledPoints.Length);
            ctx.RandCount = count;
            int portalLife = VDDirector.ReinforcePortalLife + (VDDirector.ReinforceSpawnFrame - VDDirector.ReinforcePortalFrame);
            for (int i = 0; i < count; i++) {
                float xOff = Main.rand.NextFloat(-VDDirector.ReinforceSpreadX, VDDirector.ReinforceSpreadX);
                Vector2 spawn = VDVfx.FindGround(ctx.Target.Center + new Vector2(xOff, 0f), ctx.Target.Bottom.Y);
                ctx.RolledPoints[i] = spawn;
                SpawnVisual<VDPortal>(ctx, spawn + new Vector2(0, -44f), Vector2.UnitX, VDPortal.ModeReinforce, portalLife);
                //落点略抬高,教徒落在地面上而不是嵌进去
                Vector2 landing = spawn + new Vector2(0, -30f);
                Shoot<VDDropPod>(ctx, landing - new Vector2(0f, VDDirector.ReinforcePodFallHeight), Vector2.Zero, VDDirector.DmgDropPod, ctx.Npc.target, ctx.Npc.whoAmI, landing.Y);
            }
        }
    }
}
