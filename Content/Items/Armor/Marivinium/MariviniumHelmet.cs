using CalamityEntropy.Content.Buffs;
using CalamityEntropy.Content.Buffs.PortsDoT;
using CalamityEntropy.Content.Rarities;
using CalamityEntropy.Content.Tiles;
using CalamityEntropy.Core.Weapons;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Armor.Marivinium
{
    [AutoloadEquip(EquipType.Head)]
    public class MariviniumHelmet : ModItem
    {
        public static int ShieldCd = 36 * 60;
        public static int MaxShield = 2;
        public override void SetDefaults()
        {
            Item.width = 48;
            Item.height = 48;
            Item.value = Item.buyPrice(platinum: 2, gold: 80);
            Item.defense = 50;
            Item.rare = ModContent.RarityType<AbyssalBlue>();
        }

        public override bool IsArmorSet(Item head, Item body, Item legs)
        {
            return body.type == ModContent.ItemType<MariviniumBodyArmor>() && legs.type == ModContent.ItemType<MariviniumLeggings>();
        }


        public override void UpdateArmorSet(Player player)
        {
            player.setBonus = Mod.GetLocalization("MariviniumSet").Value;
            player.Entropy().meleeDamageReduce += 0.20f;
            player.maxMinions += 10;
            player.GetDamage(DamageClass.Summon) += 0.75f;
            player.Entropy().MariviniumSet = true;
            // 潜行体系退役:原潜行条(上限1.35)按容量×10%换算为大招充能速度
            player.GetModPlayer<CEChargePlayer>().ChargeRateMult += 0.135f;
            if (player.velocity.Length() < 1)
            {
                player.lifeRegen += 20;
                player.Entropy().lifeRegenPerSec += 1;
            }
            ApplyBuffImmune(player);
            // 脱离灾厄:灾厄 TrueMeleeDamageClass 判定改为「近战且武器本体可挥砍」
            if (player.HeldItem.DamageType.CountsAsClass(DamageClass.Melee) && !player.HeldItem.noMelee)
            {
                player.Entropy().damageReduce += 0.10f;
                player.statDefense += 15;
            }
        }
        public static void ApplyBuffImmune(Player player)
        {
            player.buffImmune[ModContent.BuffType<VulnerabilityHex>()] = true;
            player.buffImmune[ModContent.BuffType<MiracleBlight>()] = true;
            player.buffImmune[ModContent.BuffType<Dragonfire>()] = true;
            player.buffImmune[ModContent.BuffType<GodSlayerInferno>()] = true;
            player.buffImmune[ModContent.BuffType<VoidTouch>()] = true;
            player.buffImmune[ModContent.BuffType<Plague>()] = true;
            player.buffImmune[ModContent.BuffType<VoidVirus>()] = true;
            player.buffImmune[ModContent.BuffType<Deceive>()] = true;
            player.buffImmune[ModContent.BuffType<SulphuricPoisoning>()] = true;
            player.buffImmune[ModContent.BuffType<MechanicalTrauma>()] = true;
            player.buffImmune[ModContent.BuffType<Irradiated>()] = true;
            player.buffImmune[BuffID.Venom] = true;
            player.buffImmune[ModContent.BuffType<BonePiercingToxin>()] = true;
            player.buffImmune[ModContent.BuffType<Nightwither>()] = true;
            player.buffImmune[ModContent.BuffType<HolyFlames>()] = true;
            player.buffImmune[ModContent.BuffType<GalvanicCorrosion>()] = true;
            player.buffImmune[BuffID.Frostburn] = true;
            player.buffImmune[ModContent.BuffType<ArmorCrunch>()] = true;
            player.buffImmune[BuffID.Electrified] = true;
            player.buffImmune[ModContent.BuffType<BrimstoneFlames>()] = true;
            player.buffImmune[BuffID.CursedInferno] = true;
            player.buffImmune[BuffID.ShadowFlame] = true;
            player.buffImmune[148] = true;
            player.buffImmune[BuffID.BrokenArmor] = true;
            player.buffImmune[BuffID.WitheredArmor] = true;
            player.buffImmune[ModContent.BuffType<MaliciousCode>()] = true;
            player.buffImmune[ModContent.BuffType<CrushDepth>()] = true;
            player.buffImmune[ModContent.BuffType<HadopelagicPressure>()] = true;
        }
        public override void UpdateEquip(Player player)
        {
            player.GetDamage(DamageClass.Generic) += 0.2f;
            player.GetCritChance(DamageClass.Generic) += 10;
            player.GetAttackSpeed(DamageClass.Melee) += 0.20f;
            player.statLifeMax2 += 200;
            player.statManaMax2 += 200;
        }

        public override void AddRecipes()
        {
            // 脱离灾厄:灾厄欧米茄蓝盔改为蘑菇矿潜袭面甲(表外裁定,档位由龙牙把关)
            CreateRecipe()
                .AddIngredient(ItemID.ShroomiteMask)
                .AddIngredient<WyrmTooth>(4)
                .AddIngredient<FadingRunestone>()
                .AddTile<AbyssalAltarTile>()
                .Register();
        }
    }
}
