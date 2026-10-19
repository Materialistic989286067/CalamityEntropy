using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Armor.Starmoon
{
    [AutoloadEquip(EquipType.Head)]
    public class StarmoonWizardHat : ModItem
    {
        public override void SetStaticDefaults() {
            ArmorIDs.Head.Sets.DrawHead[Item.headSlot] = false;
        }

        public override void SetDefaults() {
            Item.width = 28;
            Item.height = 24;
            Item.value = Item.sellPrice(gold: 20);
            Item.rare = ItemRarityID.Cyan;
            Item.defense = 27;
        }

        public override void UpdateEquip(Player player) {
            player.GetDamage(DamageClass.Magic) += 0.14f;
            player.GetCritChance(DamageClass.Magic) += 14;
            player.statManaMax2 += 70;
            player.manaCost -= 0.17f;
        }

        public override bool IsArmorSet(Item head, Item body, Item legs) {
            return body.type == ModContent.ItemType<StarmoonBreastplate>() && legs.type == ModContent.ItemType<StarmoonLeggings>();
        }

        public override void UpdateArmorSet(Player player) {
            player.setBonus = Mod.GetLocalization("StarmoonMagicSetBonus").Value;
            player.GetDamage(DamageClass.Magic) += 0.13f;
            player.GetCritChance(DamageClass.Magic) += 13;
            player.GetModPlayer<StarmoonArmorPlayer>().MagicSet = true;
        }

        public override void ArmorSetShadows(Player player) {
            player.armorEffectDrawOutlines = true;
        }

        public override void AddRecipes() {
            CreateRecipe()
                .AddIngredient<WraithSoulEssence>(22)
                .AddIngredient(ItemID.FragmentNebula, 8)
                .AddTile(TileID.LunarCraftingStation)
                .Register();
        }
    }
}
