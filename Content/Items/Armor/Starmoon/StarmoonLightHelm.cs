using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Armor.Starmoon
{
    [AutoloadEquip(EquipType.Head)]
    public class StarmoonLightHelm : ModItem
    {
        public override void SetStaticDefaults() {
            ArmorIDs.Head.Sets.DrawHead[Item.headSlot] = false;
        }

        public override void SetDefaults() {
            Item.width = 26;
            Item.height = 24;
            Item.value = Item.sellPrice(gold: 20);
            Item.rare = ItemRarityID.Cyan;
            Item.defense = 33;
        }

        public override void UpdateEquip(Player player) {
            player.GetDamage(DamageClass.Ranged) += 0.1f;
            player.GetCritChance(DamageClass.Ranged) += 10;
            player.aggro -= 900;
        }

        public override bool IsArmorSet(Item head, Item body, Item legs) {
            return body.type == ModContent.ItemType<StarmoonBreastplate>() && legs.type == ModContent.ItemType<StarmoonLeggings>();
        }

        public override void UpdateArmorSet(Player player) {
            player.setBonus = Mod.GetLocalization("StarmoonRangedSetBonus").Value;
            player.GetModPlayer<StarmoonArmorPlayer>().RangedSet = true;
        }

        public override void ArmorSetShadows(Player player) {
            player.armorEffectDrawOutlines = true;
        }

        public override void AddRecipes() {
            CreateRecipe()
                .AddIngredient<WraithSoulEssence>(22)
                .AddIngredient(ItemID.FragmentVortex, 8)
                .AddTile(TileID.LunarCraftingStation)
                .Register();
        }
    }
}
