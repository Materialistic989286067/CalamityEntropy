using InnoVault.StateMachines;
using Terraria;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.NPCs.VoidDestroyer.Core
{
    /// <summary>
    /// 表是建议序,IsLegal 才是闸,最近三手或同家族就沿表后找,阶段切换不清历史
    /// 替补只有两处,舰队远距 → 幻影冲刺,支援无地面或满员 → 裂隙斩,风筝时 P1 冲刺最多 3 手
    /// 远景招只剩轨道轰炸(P2 表两次、P3 表一次),本体带外约 150 帧一次,占一轮不到 6%
    /// </summary>
    public static class VDRotation
    {
        /// <summary>收缩环门是 P1 的区域招,第三手就亮</summary>
        private static readonly VDStateIndex[] Phase1 =
        {
            VDStateIndex.ArcFireball, VDStateIndex.PhantomDash, VDStateIndex.ClosingGates, VDStateIndex.HomingMissiles,
            VDStateIndex.PhantomFleet, VDStateIndex.VoidFlame, VDStateIndex.PhaseLaser, VDStateIndex.PhantomDash,
            VDStateIndex.RiftCut, VDStateIndex.ClosingGates, VDStateIndex.ArcFireball, VDStateIndex.PhantomFleet,
        };

        /// <summary>首手轨道轰炸是阶段签名</summary>
        private static readonly VDStateIndex[] Phase2 =
        {
            VDStateIndex.OrbitalStrike, VDStateIndex.PhantomDash, VDStateIndex.RedHell, VDStateIndex.HomingMissiles,
            VDStateIndex.Singularity, VDStateIndex.Reinforcement, VDStateIndex.PhantomFleet, VDStateIndex.GreenJungle,
            VDStateIndex.RiftCut, VDStateIndex.TeleportFire, VDStateIndex.OrbitalStrike, VDStateIndex.BombingRun,
            VDStateIndex.PhaseLaser, VDStateIndex.BlueSky,
        };

        /// <summary>首手湮灭主炮,连段后手计入历史,舰队表里只一次</summary>
        private static readonly VDStateIndex[] Phase3 =
        {
            VDStateIndex.AnnihilationCannon, VDStateIndex.RedHell, VDStateIndex.RiftCut, VDStateIndex.Singularity,
            VDStateIndex.BombingRun, VDStateIndex.OrbitalStrike, VDStateIndex.GreenJungle, VDStateIndex.TeleportFire,
            VDStateIndex.PhantomFleet, VDStateIndex.AnnihilationCannon, VDStateIndex.PhantomDash, VDStateIndex.BlueSky,
            VDStateIndex.ClosingGates, VDStateIndex.Reinforcement, VDStateIndex.PhaseLaser, VDStateIndex.HomingMissiles,
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

        /// <summary>硬性防复读判据:不在最近三手里,且与上一手不同家族</summary>
        public static bool IsLegal(VDStateContext ctx, VDStateIndex candidate) {
            if (!IsAttack(candidate)) {
                return false;
            }
            if (ctx.InHistory(candidate)) {
                return false;
            }
            return FamilyOf(candidate) != ctx.LastFamily;
        }

        public static IVDState Create(VDStateIndex state) {
            return VaultStateRegistry<VDStateContext>.Create((int)state) as IVDState;
        }

        /// <summary>P3 连段:头招收招直接接的后手(None = 无)</summary>
        public static VDStateIndex ChainFollow(int phase, VDStateIndex head) {
            if (phase < 3) {
                return VDStateIndex.Hub;
            }
            switch (head) {
                //奇点还在牵引时导弹环从四周扑来:引力把导弹的弧线也拉弯
                case VDStateIndex.Singularity:
                    return VDStateIndex.HomingMissiles;
                //缝刚合上舰队就从缝口的位置开门齐冲
                case VDStateIndex.RiftCut:
                    return VDStateIndex.PhantomFleet;
                //从背景俯冲归位直接接幻影冲刺
                case VDStateIndex.OrbitalStrike:
                    return VDStateIndex.PhantomDash;
                default:
                    return VDStateIndex.Hub;
            }
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

        /// <summary>签名首招优先,否则沿表找第一个合法的,无解退到家族互异的安全对</summary>
        public static VDStateIndex Pick(VDStateContext ctx) {
            ctx.QueuedChainState = -1;

            if (ctx.ForcedNextState >= 0) {
                VDStateIndex forced = (VDStateIndex)ctx.ForcedNextState;
                ctx.ForcedNextState = -1;
                if (IsAttack(forced)) {
                    Commit(ctx, forced);
                    return forced;
                }
            }

            VDStateIndex[] table = TableFor(ctx.Phase);
            for (int step = 0; step < VDDirector.RotationSearchSteps; step++) {
                int slot = (ctx.AttackIndex + step) % table.Length;
                VDStateIndex candidate = Substitute(ctx, table[slot]);
                if (!IsLegal(ctx, candidate)) {
                    continue;
                }
                ctx.AttackIndex = (ctx.AttackIndex + step + 1) % table.Length;
                QueueChain(ctx, candidate);
                Commit(ctx, candidate);
                return candidate;
            }

            //极端兜底:表里找不到合法招时(正常到不了),状态机也不能停在这里
            ctx.AttackIndex = (ctx.AttackIndex + 1) % table.Length;
            VDStateIndex fallback = IsLegal(ctx, VDStateIndex.PhantomDash) ? VDStateIndex.PhantomDash
                : IsLegal(ctx, VDStateIndex.ArcFireball) ? VDStateIndex.ArcFireball : VDStateIndex.PhantomDash;
            Commit(ctx, fallback);
            return fallback;
        }

        /// <summary>连段入队:后手在当下就要合法(不与头招同家族、不在历史里),否则不排</summary>
        private static void QueueChain(VDStateContext ctx, VDStateIndex head) {
            VDStateIndex follow = ChainFollow(ctx.Phase, head);
            if (!IsAttack(follow) || follow == head) {
                return;
            }
            if (ctx.InHistory(follow) || FamilyOf(follow) == FamilyOf(head)) {
                return;
            }
            ctx.QueuedChainState = (int)follow;
        }

        /// <summary>记账:写历史环与上一手家族</summary>
        public static void Commit(VDStateContext ctx, VDStateIndex picked) {
            ctx.PushHistory(picked);
            ctx.LastFamily = FamilyOf(picked);
        }
    }
}
