using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Armor.Sunblaze
{
    [AutoloadEquip(EquipType.Head)]
    public class SunblazeHood : ModItem
    {
        public override void SetStaticDefaults() {
            ArmorIDs.Head.Sets.DrawHead[Item.headSlot] = false;
        }

        public override void SetDefaults() {
            Item.width = 26;
            Item.height = 26;
            Item.value = Item.sellPrice(gold: 20);
            Item.rare = ItemRarityID.Cyan;
            Item.defense = 14;
        }

        public override void UpdateEquip(Player player) {
            player.GetDamage(DamageClass.Summon) += 0.22f;
            player.maxMinions += 2;
            player.whipRangeMultiplier += 0.2f;
        }

        public override bool IsArmorSet(Item head, Item body, Item legs) {
            return body.type == ModContent.ItemType<SunblazeBreastplate>() && legs.type == ModContent.ItemType<SunblazeLeggings>();
        }

        public override void UpdateArmorSet(Player player) {
            player.setBonus = Mod.GetLocalization("SunblazeSummonSetBonus").Value;
            player.GetDamage(DamageClass.Summon) += 0.4f;
            player.maxMinions += 3;
            player.GetModPlayer<SunblazeArmorPlayer>().TotemActive = true;
            if (player.whoAmI == Main.myPlayer) {
                player.AddBuff(ModContent.BuffType<SunbirdTotemBuff>(), 2);
            }
        }

        public override void ArmorSetShadows(Player player) {
            player.armorEffectDrawShadow = true;
        }

        public override void AddRecipes() {
            CreateRecipe()
                .AddIngredient<BlazingSunlight>(22)
                .AddIngredient(ItemID.FragmentStardust, 8)
                .AddTile(TileID.LunarCraftingStation)
                .Register();
        }
    }
}
