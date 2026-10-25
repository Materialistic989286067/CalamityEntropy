using System;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Common
{
    /// <summary>原版武器弹幕的小改动,面板与使用时间的改动在 EGlobalItem.SetDefaults</summary>
    public class VanillaWeaponProjectileTweaks : GlobalProjectile
    {
        /// <summary>鞭炮爆炸单次命中伤害上限</summary>
        public const int FirecrackerBlastDamageCap = 1000;
        /// <summary>喜庆弹射器 Mk2 的射击间隔,等于 Item.useTime</summary>
        public const int Celeb2ShotInterval = 10;
        //原版手持弹幕写死的射击间隔
        private const int Celeb2VanillaInterval = 8;

        public override bool InstancePerEntity => true;

        //上一帧结束时的射击倒计时,用来区分"刚射完重置"与"自己数到 8"
        private float lastCountdown;

        public override bool AppliesToEntity(Projectile entity, bool lateInstantiation) {
            return entity.type == ProjectileID.ImpFireball
                || entity.type == ProjectileID.FireWhipProj
                || entity.type == ProjectileID.Celeb2Weapon;
        }

        public override void SetDefaults(Projectile projectile) {
            if (projectile.type == ProjectileID.ImpFireball) {
                projectile.usesIDStaticNPCImmunity = false;
                projectile.usesLocalNPCImmunity = true;
                projectile.localNPCHitCooldown = -1;
            }
        }

        public override void ModifyHitNPC(Projectile projectile, NPC target, ref NPC.HitModifiers modifiers) {
            if (projectile.type == ProjectileID.FireWhipProj)
                modifiers.SetMaxDamage(FirecrackerBlastDamageCap);
        }

        public override void PostAI(Projectile projectile) {
            if (projectile.type != ProjectileID.Celeb2Weapon)
                return;
            //ai[1] 是射击倒计时,刚射完被原版置为 8,此时上帧末的倒计时必已走到 1 以下;
            //倒数途中经过 8 时上帧末为 9,不能当作重置,否则永远数不到 0 而卡死
            if (projectile.ai[1] == Celeb2VanillaInterval && lastCountdown <= 1f) {
                int extra = Celeb2ShotInterval - Celeb2VanillaInterval;
                projectile.ai[1] = Celeb2ShotInterval;
                projectile.ai[0] = Math.Max(0f, projectile.ai[0] - extra);
                if (projectile.soundDelay == Celeb2VanillaInterval)
                    projectile.soundDelay = Celeb2ShotInterval;
            }
            lastCountdown = projectile.ai[1];
        }
    }
}
