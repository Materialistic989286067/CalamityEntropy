using CalamityEntropy.Content.Buffs;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Armor.Sunblaze
{
    public class SunblazeArmorPlayer : ModPlayer
    {
        /// <summary>暖洋洋光环半径,25 格</summary>
        public const float WarmthRadius = 25 * 16;

        public bool WarmthAura;
        public bool TotemActive;

        public override void ResetEffects() {
            WarmthAura = false;
            TotemActive = false;
        }

        public override void PostUpdateEquips() {
            //每个客户端只给本机玩家上增益,穿戴者是谁都一样,增益不走网络
            if (WarmthAura && !Main.dedServ) {
                Player local = Main.LocalPlayer;
                if (local.active && !local.dead && local.DistanceSQ(Player.Center) <= WarmthRadius * WarmthRadius) {
                    local.AddBuff(ModContent.BuffType<CozyWarmth>(), 120);
                }
            }
            if (TotemActive && Player.whoAmI == Main.myPlayer) {
                int totem = ModContent.ProjectileType<SunbirdTotem>();
                if (Player.ownedProjectileCounts[totem] < 1) {
                    Projectile.NewProjectile(Player.GetSource_FromThis(), Player.Center, Vector2.Zero, totem, 0, 0f, Player.whoAmI);
                }
            }
        }
    }

    public class CozyWarmth : ModBuff
    {
        public override void SetStaticDefaults() {
            Main.buffNoSave[Type] = true;
        }

        public override void Update(Player player, ref int buffIndex) {
            player.lifeRegen += 4;
        }
    }

    public class SunbirdTotemBuff : ModBuff
    {
        public override void SetStaticDefaults() {
            Main.buffNoSave[Type] = true;
            Main.buffNoTimeDisplay[Type] = true;
        }
    }

    /// <summary>敌怪每秒 300,玩家(PvP)每秒 50,由 EDamageOverTimeNPC/Player 结算</summary>
    public class SunblazeBurn : DotBuff
    {
        public override int DamagePlayerPerSec => 50;
        public override int DamageEnemiesPerSec => 300;

        public override void SetStaticDefaults() {
            base.SetStaticDefaults();
            Main.buffNoSave[Type] = true;
            BuffID.Sets.LongerExpertDebuff[Type] = false;
        }

        public override void Update(NPC npc, ref int buffIndex) {
            if (Main.rand.NextBool(3)) {
                SpawnFlame(npc.position, npc.width, npc.height);
            }
        }

        public override void Update(Player player, ref int buffIndex) {
            if (Main.rand.NextBool(3)) {
                SpawnFlame(player.position, player.width, player.height);
            }
        }

        private static void SpawnFlame(Vector2 position, int width, int height) {
            Dust d = Dust.NewDustDirect(position, width, height, DustID.SolarFlare, 0f, -2f, 100, default, Main.rand.NextFloat(0.9f, 1.4f));
            d.noGravity = true;
            d.velocity *= 0.6f;
            d.velocity.Y -= 1.2f;
        }
    }
}
