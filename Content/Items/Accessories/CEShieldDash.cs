using System;
using System.Collections.Generic;
using CalamityEntropy.Common;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Accessories
{
    /// <summary>
    /// 盾冲刺命中上下文：由各冲刺效果的 OnHitEffects 填写，驱动器据此结算冲撞伤害。
    /// 形状对齐灾厄 DashHitContext 的本模组使用面。
    /// </summary>
    public struct CEDashHitContext
    {
        public int HitDirection;
        public int PlayerImmunityFrames;
        public DamageClass damageClass;
        public int BaseDamage;
        public float BaseKnockback;
    }

    /// <summary>
    /// 盾冲刺效果基类（自研，替代灾厄 PlayerDashEffect 的本模组使用面：水平方向盾撞冲刺）。
    /// 实例为静态单例，Time/PostHit 等运行时字段仅服务本地玩家（与灾厄同约束）。
    /// </summary>
    public abstract class CEShieldDashEffect
    {
        /// <summary>冲刺进行帧计数，驱动器每帧递增。</summary>
        public int dashTime;

        /// <summary>冲刺 ID，写入 player.Entropy().LastUsedDashID（约定名见 player-api.md）。</summary>
        public abstract string DashID { get; }

        public abstract float CalculateDashSpeed(Player player);

        /// <summary>冲刺触发瞬间。</summary>
        public virtual void OnDashEffects(Player player) { }

        /// <summary>冲刺进行中每帧：可改写冲刺速度与减速系数。</summary>
        public virtual void MidDashEffects(Player player, ref float dashSpeed, ref float dashSpeedDecelerationFactor, ref float runSpeedDecelerationFactor) { }

        /// <summary>冲撞命中敌怪：填写 hitContext 以结算伤害。</summary>
        public virtual void OnHitEffects(Player player, NPC npc, ref CEDashHitContext hitContext) { }
    }

    /// <summary>
    /// 盾冲刺驱动器（自研，承接灾厄 ModDashMovement 的本模组使用面）。
    /// 饰品每帧在 UpdateAccessory 里设置 ActiveDash；驱动器负责双击/冲刺键触发、
    /// 冲刺移动衰减、冲撞判定与冷却（复用原版 Player.dashDelay 语义：-1 冲刺中、>0 冷却中）。
    /// </summary>
    public class CEShieldDashPlayer : ModPlayer
    {
        /// <summary>本帧可用的冲刺效果，饰品侧每帧设置，ResetEffects 清空。</summary>
        public CEShieldDashEffect ActiveDash;

        /// <summary>正在进行的冲刺效果。</summary>
        private CEShieldDashEffect usedDash;

        /// <summary>冲撞后的敌怪受击间隔（对齐灾厄 dashImmunityTime，仅本地玩家维护）。</summary>
        private readonly Dictionary<int, int> npcHitGap = new();

        /// <summary>盾撞冲刺结束后的统一冷却（对齐灾厄 UniversalShieldSlamCooldown）。</summary>
        private const int ShieldSlamCooldown = 30;

        public override void ResetEffects()
        {
            ActiveDash = null;
        }

        public override void PreUpdateMovement()
        {
            if (npcHitGap.Count > 0)
            {
                foreach (int key in new List<int>(npcHitGap.Keys))
                {
                    if (--npcHitGap[key] <= 0)
                        npcHitGap.Remove(key);
                }
            }

            if (Player.whoAmI != Main.myPlayer)
                return;

            if (ActiveDash == null)
            {
                if (Player.dashDelay >= 0)
                    usedDash = null;
                return;
            }

            if (Player.dashDelay < 0 && usedDash != null)
            {
                UpdateActiveDash();
                return;
            }

            if (Player.dashDelay == 0 && !Player.mount.Active && TryGetDashDirection(out int direction))
                StartDash(direction);
        }

        private void StartDash(int direction)
        {
            usedDash = ActiveDash;
            usedDash.dashTime = 0;
            Player.velocity.X = direction * usedDash.CalculateDashSpeed(Player);

            // 前方贴墙时腰斩初速（对齐灾厄 DoADash 的物块检测）
            Point upwardTilePoint = (Player.Center + new Vector2(direction * Player.width / 2 + 2, Player.gravDir * -Player.height / 2f + Player.gravDir * 2f)).ToTileCoordinates();
            Point aheadTilePoint = (Player.Center + new Vector2(direction * Player.width / 2 + 2, 0f)).ToTileCoordinates();
            if (WorldGen.SolidOrSlopedTile(upwardTilePoint.X, upwardTilePoint.Y) || WorldGen.SolidOrSlopedTile(aheadTilePoint.X, aheadTilePoint.Y))
                Player.velocity.X /= 2f;

            Player.timeSinceLastDashStarted = 0;
            Player.dashDelay = -1;
            Player.Entropy().LastUsedDashID = usedDash.DashID;
            usedDash.OnDashEffects(Player);
        }

        private void UpdateActiveDash()
        {
            // 冲撞判定（对齐灾厄 ModDashMovement 的碰撞盒与免疫处理）
            Rectangle hitArea = new((int)(Player.position.X + Player.velocity.X * 0.5 - 4f), (int)(Player.position.Y + Player.velocity.Y * 0.5 - 4), Player.width + 8, Player.height + 8);
            foreach (NPC n in Main.ActiveNPCs)
            {
                if (Player.dontHurtCritters && NPCID.Sets.CountsAsCritter[n.type])
                    continue;
                if (n.dontTakeDamage || n.friendly || npcHitGap.ContainsKey(n.whoAmI))
                    continue;
                if (!hitArea.Intersects(n.getRect()) || (!n.noTileCollide && !Player.CanHit(n)))
                    continue;

                CEDashHitContext hitContext = default;
                usedDash.OnHitEffects(Player, n, ref hitContext);
                if (hitContext.damageClass is null || hitContext.BaseDamage <= 0)
                    continue;

                int dashDamage = (int)Player.GetTotalDamage(hitContext.damageClass).ApplyTo(hitContext.BaseDamage);
                Player.ApplyDamageToNPC(n, dashDamage, hitContext.BaseKnockback, hitContext.HitDirection, false, hitContext.damageClass);
                npcHitGap[n.whoAmI] = 12;
                Player.GiveImmuneTimeForCollisionAttack(hitContext.PlayerImmunityFrames);
            }
            usedDash.dashTime++;

            float dashSpeed = 12f;
            float dashSpeedDecelerationFactor = 0.985f;
            float runSpeed = Math.Max(Player.accRunSpeed, Player.maxRunSpeed);
            float runSpeedDecelerationFactor = 0.94f;
            usedDash.MidDashEffects(Player, ref dashSpeed, ref dashSpeedDecelerationFactor, ref runSpeedDecelerationFactor);

            Player.vortexStealthActive = false;
            if (Player.velocity.X != 0f)
                Player.ChangeDir(Math.Sign(Player.velocity.X));

            // 水平速度逐级衰减，回落到跑速后进入冷却（对齐灾厄非全向冲刺分支）
            if (Player.velocity.X > dashSpeed || Player.velocity.X < -dashSpeed)
            {
                Player.velocity.X *= dashSpeedDecelerationFactor;
                return;
            }
            if (Player.velocity.X > runSpeed || Player.velocity.X < -runSpeed)
            {
                Player.velocity.X *= runSpeedDecelerationFactor;
                return;
            }

            Player.dashDelay = ShieldSlamCooldown;
            if (Player.velocity.X < 0f)
                Player.velocity.X = -runSpeed;
            else if (Player.velocity.X > 0f)
                Player.velocity.X = runSpeed;
        }

        /// <summary>
        /// 触发检测：绑定了冲刺键则只认冲刺键（方向取按键/移动/朝向），
        /// 否则走原版双击方向键（与 EPlayerDash 相同的 doubleTapCardinalTimer 判定）。
        /// </summary>
        private bool TryGetDashDirection(out int direction)
        {
            direction = 0;
            var keys = EModPlayer.DashHotkey?.GetAssignedKeys();
            bool hotkeyBound = keys != null && keys.Count > 0;
            if (hotkeyBound)
            {
                if (!EModPlayer.DashHotkey.JustPressed)
                    return false;
                if (Player.controlRight && !Player.controlLeft)
                    direction = 1;
                else if (Player.controlLeft && !Player.controlRight)
                    direction = -1;
                else
                    direction = MathF.Abs(Player.velocity.X) <= 0.01f ? Player.direction : Math.Sign(Player.velocity.X);
                return true;
            }

            if (Player.controlRight && Player.releaseRight && Player.doubleTapCardinalTimer[2] < 15)
            {
                direction = 1;
                return true;
            }
            if (Player.controlLeft && Player.releaseLeft && Player.doubleTapCardinalTimer[3] < 15)
            {
                direction = -1;
                return true;
            }
            return false;
        }
    }
}
