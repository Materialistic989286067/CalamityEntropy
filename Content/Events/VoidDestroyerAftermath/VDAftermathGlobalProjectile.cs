using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Events.VoidDestroyerAftermath
{
    /// <summary>强化敌怪 AI 里发射的敌对弹幕按同一伤害倍率放大,生成端算好后随弹幕包同步</summary>
    public class VDAftermathGlobalProjectile : GlobalProjectile
    {
        public override void OnSpawn(Projectile projectile, IEntitySource source) {
            if (!projectile.hostile || !VDAftermath.Active) {
                return;
            }
            if (source is EntitySource_Parent { Entity: NPC npc }
                && VDAftermath.StatBoosts.TryGetValue(npc.type, out VDStatBoost boost) && boost.Damage != 1f) {
                projectile.damage = (int)(projectile.damage * boost.Damage);
            }
        }
    }
}
