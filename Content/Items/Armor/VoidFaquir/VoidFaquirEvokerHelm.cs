using CalamityEntropy.Content.Rarities;
using CalamityEntropy.Content.Tiles;
using CalamityEntropy.Core.CalamityRef;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Armor.VoidFaquir
{
    [AutoloadEquip(EquipType.Head)]
    public class VoidFaquirEvokerHelm : ModItem
    {
        public override void SetStaticDefaults() {
            ArmorIDs.Head.Sets.DrawHead[Item.headSlot] = false;
        }
        public override void SetDefaults() {
            Item.width = 18;
            Item.height = 18;
            Item.value = Item.buyPrice(platinum: 2, gold: 40);
            Item.defense = 28;
            Item.rare = ModContent.RarityType<VoidPurple>();
        }

        public override bool IsArmorSet(Item head, Item body, Item legs) {
            return body.type == ModContent.ItemType<VoidFaquirBodyArmor>() && legs.type == ModContent.ItemType<VoidFaquirCuises>();
        }

        public override void ArmorSetShadows(Player player) {
            player.armorEffectDrawOutlines = true;
        }

        //职业奖励:召唤伤害、仆从栏、迷你吞噬者(VFHelmSummoner 驱动)
        public override void UpdateArmorSet(Player player) {
            player.setBonus = VoidFaquirSet.BonusText(Mod, "helmvfe");
            player.GetDamage(DamageClass.Summon) += 0.40f;
            player.maxMinions += 4;
            player.Entropy().VFSet = true;
            player.Entropy().VFHelmSummoner = true;
        }

        public override void UpdateEquip(Player player) {
            player.Entropy().summonerVF = true;
            player.GetDamage(DamageClass.Summon) += 0.40f;
            player.maxMinions += 2;
            player.statLifeMax2 += 80;
        }

        public override void ModifyTooltips(List<TooltipLine> tooltips) {

        }

        public override void AddRecipes() {
            if (CECal.CalChainReady(CEID.Item_TwistingNether)) {
                CreateRecipe()
                .AddIngredient(ModContent.ItemType<VoidBar>(), 14)
                .AddIngredient(CEID.Item_TwistingNether, 4)
                .AddTile(ModContent.TileType<VoidWellTile>())
                .Register();
                return;
            }
            CreateRecipe()
                .AddIngredient(ModContent.ItemType<VoidBar>(), 12)
                .AddTile(ModContent.TileType<VoidWellTile>())
                .Register();
        }
    }
}
