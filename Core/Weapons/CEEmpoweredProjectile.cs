using System.IO;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace CalamityEntropy.Core.Weapons
{
    /// <summary>
    /// 蓄势强化标志的弹幕侧载体,原灾厄弹幕 stealthStrike 标志的 1:1 平替。
    /// 读:proj.IsEmpowered();写:CEChargeWeapon.Empower(p) 或 proj.SetEmpowered()。
    /// 标志随弹幕生成包与 netUpdate 同步(SendExtraAI/ReceiveExtraAI)。
    /// </summary>
    public class CEEmpowerGlobalProjectile : GlobalProjectile
    {
        public override bool InstancePerEntity => true;

        /// <summary>是否为蓄势强化弹(大招弹幕)。</summary>
        public bool Empowered;

        /// <summary>发射本弹幕的蓄势武器,仅所有者端有值,用于命中计数回充。</summary>
        internal Item sourceItem;

        /// <summary>本弹幕为大招弹幕的衍生弹,不参与命中计数回充(大招及其产物不给自己充能)。</summary>
        internal bool creditBlocked;

        public override void OnSpawn(Projectile projectile, IEntitySource source)
        {
            // 直接由蓄势武器使用生成:记录来源,并按当帧强化窗口打标。
            // (EntitySource_ItemUse 继承自 EntitySource_Parent,须先判)
            if (source is EntitySource_ItemUse itemUse)
            {
                if (itemUse.Item?.ModItem is not ICEChargeWeapon)
                    return;

                sourceItem = itemUse.Item;

                // TryConsume 打开的当帧强化窗口:同帧由该玩家此武器发出的弹幕自动打标。
                // OnSpawn 先于生成同步包发出,标志随首包到达其他端,无需二次同步。
                if (projectile.owner >= 0 && projectile.owner < Main.maxPlayers
                    && Main.player[projectile.owner].GetModPlayer<CEChargePlayer>().EmpowerWindowActive)
                {
                    Empowered = true;
                }
                return;
            }

            // 父弹幕链路(GetSource_FromAI 等):父弹幕已记录来源武器时,子弹幕继承其来源。
            // 覆盖手持弹幕→伤害弹的间接生成链(如 AzafureLightMachineGun 的 ALMGLaser)。
            // 每次生成继承一跳,深链由逐级继承自然传递,不做向上遍历,因此不存在环。
            if (source is EntitySource_Parent parentSource && parentSource.Entity is Projectile parentProj)
            {
                var parentGlobal = parentProj.GetGlobalProjectile<CEEmpowerGlobalProjectile>();
                if (parentGlobal.sourceItem != null)
                {
                    sourceItem = parentGlobal.sourceItem;
                    creditBlocked = parentGlobal.Empowered || parentGlobal.creditBlocked;
                }
            }
        }

        public override void OnHitNPC(Projectile projectile, NPC target, NPC.HitInfo hit, int damageDone)
        {
            // 命中计数回充:仅普通弹幕计数,大招弹幕及其衍生弹不回充
            if (Empowered || creditBlocked || sourceItem == null)
                return;
            CEChargeWeapon.CreditHit(Main.player[projectile.owner], sourceItem);
        }

        public override void SendExtraAI(Projectile projectile, BitWriter bitWriter, BinaryWriter binaryWriter)
        {
            bitWriter.WriteBit(Empowered);
        }

        public override void ReceiveExtraAI(Projectile projectile, BitReader bitReader, BinaryReader binaryReader)
        {
            Empowered = bitReader.ReadBit();
        }
    }

    /// <summary>蓄势强化标志的查询与写入扩展。</summary>
    public static class CEEmpowerExtensions
    {
        /// <summary>该弹幕是否为蓄势强化弹。对照原灾厄 stealthStrike 读取点。</summary>
        public static bool IsEmpowered(this Projectile projectile)
            => projectile.GetGlobalProjectile<CEEmpowerGlobalProjectile>().Empowered;

        /// <summary>
        /// 标记为蓄势强化弹。sync = true 时立即补发同步包
        /// (生成后再打标时首包不含标志,需要补同步;走 TryConsume 窗口自动打标的不需要)。
        /// </summary>
        public static void SetEmpowered(this Projectile projectile, bool sync = true)
        {
            projectile.GetGlobalProjectile<CEEmpowerGlobalProjectile>().Empowered = true;
            if (sync)
                CEUtils.SyncProj(projectile);
        }
    }
}
