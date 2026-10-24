using CalamityEntropy.Content.Particles;
using InnoVault.PRT;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Buffs
{
    public class SoulDisorder : ModBuff
    {
        public override void SetStaticDefaults() {
            Main.buffNoSave[Type] = true;
            BuffID.Sets.NurseCannotRemoveDebuff[Type] = true;
            BuffID.Sets.LongerExpertDebuff[Type] = true;
            Main.debuff[Type] = true;
            Main.pvpBuff[Type] = true;
        }

        public override void Update(Player player, ref int buffIndex) {
            player.Entropy().soulDisorder = true;
            SoulDisorderVfx.Emit(player.getRect());
        }

        public override void Update(NPC npc, ref int buffIndex) {
            SoulDisorderVfx.Emit(npc.getRect());
        }
    }

    public static class SoulDisorderVfx
    {
        public const float SpawnChance = 0.25f;
        public const int ViewMargin = 64;
        public const int LifeMin = 42;
        public const int LifeMax = 72;
        public const float RiseMin = 0.5f;
        public const float RiseMax = 1.05f;
        public const float DriftX = 0.18f;
        public const float WispScaleMin = 0.06f;
        public const float WispScaleMax = 0.11f;
        public const float TiltMax = 0.25f;
        public const float HaloScale = 0.22f;
        public const float HaloAlpha = 0.3f;

        public static void Emit(Rectangle hitbox) {
            if (Main.dedServ || Main.rand.NextFloat() >= SpawnChance)
                return;
            Rectangle view = new((int)Main.screenPosition.X - ViewMargin, (int)Main.screenPosition.Y - ViewMargin,
                Main.screenWidth + ViewMargin * 2, Main.screenHeight + ViewMargin * 2);
            Rectangle area = Rectangle.Intersect(hitbox, view);
            if (area.Width <= 0 || area.Height <= 0)
                return;
            Vector2 pos = new(area.X + Main.rand.NextFloat() * area.Width, area.Y + Main.rand.NextFloat() * area.Height);
            Vector2 vel = new(Main.rand.NextFloat(-DriftX, DriftX), -Main.rand.NextFloat(RiseMin, RiseMax));
            int life = Main.rand.Next(LifeMin, LifeMax + 1);

            //白色竖向光瓣是主体,淡晕只垫底
            PRT_GlowSpark wisp = PRTLoader.NewParticle<PRT_GlowSpark>(pos, vel, Color.White, Main.rand.NextFloat(WispScaleMin, WispScaleMax));
            wisp.grav = false;
            wisp.Configure(1f, true, PRTDrawModeEnum.AdditiveBlend, -MathHelper.PiOver2 + Main.rand.NextFloat(-TiltMax, TiltMax), life);
            PRTLoader.NewParticle<PRT_ShineParticle>(pos, vel, Color.White * HaloAlpha, HaloScale)
                .Configure(1f, true, PRTDrawModeEnum.AdditiveBlend, 0f, life);
        }
    }

    public class SoulDisorderDebuffNPC : GlobalNPC
    {
        public override void ModifyIncomingHit(NPC npc, ref NPC.HitModifiers modifiers) {
            if (npc.HasBuff<SoulDisorder>()) {
                modifiers.ArmorPenetration += 15;
                modifiers.FinalDamage *= 1.05f;
            }
        }
    }

}
