﻿using CalamityEntropy.Content.Projectiles.TwistedTwin;
using Terraria;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Buffs
{
    public class TwistedTwinBuff : ModBuff
    {
        public override void SetStaticDefaults()
        {
            Main.buffNoSave[Type] = true;
            Main.buffNoTimeDisplay[Type] = true;
        }
        public override void Update(Player player, ref int buffIndex)
        {
            player.GetDamage(DamageClass.Generic) *= 0.25f;
            if (player.ownedProjectileCounts[ModContent.ProjectileType<TwistedTwinMinion>()] > 0)
            {
                player.buffTime[buffIndex] = 18000;
            }
            else
            {
                player.DelBuff(buffIndex);
                buffIndex--;
            }
        }
    }
}
