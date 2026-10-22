using CalamityEntropy.Content.NPCs.Acropolis;
using CalamityEntropy.Core.CalamityRef;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items;

public class SignalBlocker : ModItem
{
    public override void SetStaticDefaults() {
        ItemID.Sets.SortingPriorityBossSpawns[Type] = 15;
    }

    public override void SetDefaults() {
        Item.width = 32;
        Item.height = 32;
        Item.useAnimation = 24;
        Item.useTime = 24;
        Item.useStyle = ItemUseStyleID.HoldUp;
        Item.UseSound = SoundID.Item44;
        Item.consumable = false;
        Item.rare = ItemRarityID.Orange;
        Item.value = Item.buyPrice(0, 0, 0, 20);
    }

    public override void ModifyResearchSorting(ref ContentSamples.CreativeHelper.ItemGroup itemGroup) {
        itemGroup = ContentSamples.CreativeHelper.ItemGroup.BossItem;
    }

    public override bool CanUseItem(Player player) {
        return player.ZoneUnderworldHeight && !NPC.AnyNPCs(ModContent.NPCType<AcropolisMachine>()) && !CECal.IsBossRushActive;
    }

    public override bool? UseItem(Player player) {
        int type = ModContent.NPCType<AcropolisMachine>();
        if (Main.netMode != NetmodeID.MultiplayerClient)
            NPC.SpawnOnPlayer(player.whoAmI, type);
        else
            NetMessage.SendData(MessageID.SpawnBossUseLicenseStartEvent, number: player.whoAmI, number2: type);
        return true;
    }

    public override void AddRecipes() {
        CreateRecipe().
            AddRecipeGroup(CERecipeGroups.IronBar, 20).
            AddIngredient(ItemID.Obsidian, 10).
            AddIngredient(ItemID.LavaBucket).
            AddTile(TileID.Anvils).
            Register();
    }
}
