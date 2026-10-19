using InnoVault.StateMachines;
using Terraria;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.NPCs.VoidDestroyer.Core
{
    /// <summary>
    /// 技能表按序循环,每阶段一张固定表,相邻两手家族互异,出招顺序可背
    /// 只有两处替补:舰队远距 → 幻影冲刺,支援无地面或满员 → 裂隙斩;替补撞上上一手就顺延一格
    /// 远景招只有轨道轰炸,招与招之间不连段,一律走 hub 的连接段
    /// </summary>
    public static class VDRotation
    {
        /// <summary>弹幕、冲刺、区域交替,首手弹幕最轻</summary>
        private static readonly VDStateIndex[] Phase1 =
        {
            VDStateIndex.ArcFireball, VDStateIndex.PhantomDash, VDStateIndex.ClosingGates, VDStateIndex.VoidFlame,
            VDStateIndex.PhantomFleet, VDStateIndex.PhaseLaser, VDStateIndex.HomingMissiles, VDStateIndex.RiftCut,
        };

        /// <summary>首手轨道轰炸是阶段签名</summary>
        private static readonly VDStateIndex[] Phase2 =
        {
            VDStateIndex.OrbitalStrike, VDStateIndex.PhantomDash, VDStateIndex.RedHell, VDStateIndex.HomingMissiles,
            VDStateIndex.Singularity, VDStateIndex.Reinforcement, VDStateIndex.PhantomFleet, VDStateIndex.BlueSky,
            VDStateIndex.BombingRun, VDStateIndex.GreenJungle, VDStateIndex.PhaseLaser, VDStateIndex.TeleportFire,
        };

        /// <summary>首手湮灭主炮,中段再来一次</summary>
        private static readonly VDStateIndex[] Phase3 =
        {
            VDStateIndex.AnnihilationCannon, VDStateIndex.RedHell, VDStateIndex.RiftCut, VDStateIndex.HomingMissiles,
            VDStateIndex.Singularity, VDStateIndex.PhantomFleet, VDStateIndex.GreenJungle, VDStateIndex.PhaseLaser,
            VDStateIndex.BombingRun, VDStateIndex.AnnihilationCannon, VDStateIndex.BlueSky, VDStateIndex.OrbitalStrike,
            VDStateIndex.TeleportFire, VDStateIndex.PhantomDash,
        };

        public static VDStateIndex[] TableFor(int phase) => phase >= 3 ? Phase3 : phase >= 2 ? Phase2 : Phase1;

        public static bool IsAttack(VDStateIndex state) => (int)state >= (int)VDStateIndex.ArcFireball;

        public static VDAttackFamily FamilyOf(VDStateIndex state) {
            switch (state) {
                case VDStateIndex.PhantomDash:
                case VDStateIndex.PhantomFleet:
                    return VDAttackFamily.Dash;
                case VDStateIndex.ArcFireball:
                case VDStateIndex.VoidFlame:
                case VDStateIndex.TeleportFire:
                case VDStateIndex.HomingMissiles:
                case VDStateIndex.BombingRun:
                    return VDAttackFamily.Barrage;
                case VDStateIndex.PhaseLaser:
                case VDStateIndex.RiftCut:
                case VDStateIndex.OrbitalStrike:
                case VDStateIndex.ClosingGates:
                    return VDAttackFamily.Zone;
                case VDStateIndex.Singularity:
                    return VDAttackFamily.Gravity;
                case VDStateIndex.RedHell:
                case VDStateIndex.GreenJungle:
                case VDStateIndex.BlueSky:
                    return VDAttackFamily.Hologram;
                case VDStateIndex.Reinforcement:
                    return VDAttackFamily.Summon;
                case VDStateIndex.AnnihilationCannon:
                    return VDAttackFamily.Finale;
                default:
                    return VDAttackFamily.None;
            }
        }

        public static IVDState Create(VDStateIndex state) {
            return VaultStateRegistry<VDStateContext>.Create((int)state) as IVDState;
        }

        /// <summary>舰队远距 → 幻影冲刺,支援无地面或满员 → 裂隙斩,唯一跨家族</summary>
        public static VDStateIndex Substitute(VDStateContext ctx, VDStateIndex candidate) {
            switch (candidate) {
                case VDStateIndex.PhantomFleet:
                    if (ctx.TargetValid && Vector2.Distance(ctx.Npc.Center, ctx.Target.Center) > VDDirector.FarDashDistance) {
                        return VDStateIndex.PhantomDash;
                    }
                    return candidate;
                case VDStateIndex.Reinforcement: {
                    bool ground = ctx.TargetValid && VDVfx.HasGroundBelow(ctx.Target.Center);
                    bool room = NPC.CountNPCS(ModContent.NPCType<VoidVanguardCultist>()) < VDDirector.MaxVanguards;
                    return ground && room ? candidate : VDStateIndex.RiftCut;
                }
                default:
                    return candidate;
            }
        }

        /// <summary>签名首招优先,否则取表里当前一格,替补撞上上一手顺延一格</summary>
        public static VDStateIndex Pick(VDStateContext ctx) {
            if (ctx.ForcedNextState >= 0) {
                VDStateIndex forced = (VDStateIndex)ctx.ForcedNextState;
                ctx.ForcedNextState = -1;
                if (IsAttack(forced)) {
                    Commit(ctx, forced);
                    return forced;
                }
            }

            VDStateIndex[] table = TableFor(ctx.Phase);
            int slot = ctx.AttackIndex % table.Length;
            VDStateIndex candidate = Substitute(ctx, table[slot]);
            if (ctx.RecentHistory[0] == (int)candidate) {
                slot = (slot + 1) % table.Length;
                candidate = Substitute(ctx, table[slot]);
            }
            ctx.AttackIndex = (slot + 1) % table.Length;
            Commit(ctx, candidate);
            return candidate;
        }

        /// <summary>记账:写历史环与上一手家族</summary>
        public static void Commit(VDStateContext ctx, VDStateIndex picked) {
            ctx.PushHistory(picked);
            ctx.LastFamily = FamilyOf(picked);
        }
    }
}
