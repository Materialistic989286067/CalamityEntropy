using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Armor.Sunblaze
{
    [AutoloadEquip(EquipType.Head)]
    public class SunblazeWarHelm : ModItem
    {
        public override void SetStaticDefaults() {
            ArmorIDs.Head.Sets.DrawHead[Item.headSlot] = false;
        }

        public override void SetDefaults() {
            Item.width = 28;
            Item.height = 28;
            Item.value = Item.sellPrice(gold: 20);
            Item.rare = ItemRarityID.Cyan;
            Item.defense = 42;
        }

        public override void UpdateEquip(Player player) {
            player.GetDamage(DamageClass.Melee) += 0.16f;
            player.GetCritChance(DamageClass.Melee) += 16;
            player.aggro += 700;
        }

        public override bool IsArmorSet(Item head, Item body, Item legs) {
            return body.type == ModContent.ItemType<SunblazeBreastplate>() && legs.type == ModContent.ItemType<SunblazeLeggings>();
        }

        public override void UpdateArmorSet(Player player) {
            player.setBonus = Mod.GetLocalization("SunblazeMeleeSetBonus").Value;
            player.GetDamage(DamageClass.Melee) += 0.1f;
            player.GetCritChance(DamageClass.Melee) += 10;
            player.GetAttackSpeed(DamageClass.Melee) += 0.2f;
            player.lifeRegen += 5;
            player.GetModPlayer<SunblazeArmorPlayer>().WarmthAura = true;
        }

        public override void ArmorSetShadows(Player player) {
            player.armorEffectDrawShadow = true;
        }

        public override void AddRecipes() {
            CreateRecipe()
                .AddIngredient<BlazingSunlight>(22)
                .AddIngredient(ItemID.FragmentSolar, 8)
                .AddTile(TileID.LunarCraftingStation)
                .Register();
        }
    }
}
