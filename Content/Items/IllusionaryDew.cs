using CalamityEntropy.Content.NPCs.LuminarisMoth;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items
{
    public class IllusionaryDew : ModItem
    {
        public override void SetStaticDefaults()
        {
            ItemID.Sets.SortingPriorityBossSpawns[Type] = 12;
        }
        public override void SetDefaults()
        {
            Item.width = 56;
            Item.height = 56;
            Item.useAnimation = 20;
            Item.useTime = 20;
            Item.noUseGraphic = true;
            Item.UseSound = SoundID.AbigailSummon;
            Item.useStyle = ItemUseStyleID.HoldUp;
            Item.consumable = false;
            Item.rare = ItemRarityID.Yellow;

        }
        public override void ModifyResearchSorting(ref ContentSamples.CreativeHelper.ItemGroup itemGroup)
        {
            itemGroup = ContentSamples.CreativeHelper.ItemGroup.BossItem;
        }

        public override bool CanUseItem(Player player)
        {
            // 星辉群系判定改为夜间发光蘑菇群系（与星辉鳞尘的产地闭环）
            return !NPC.AnyNPCs(ModContent.NPCType<Luminaris>()) && player.ZoneGlowshroom && !Main.dayTime;
        }

        public override bool? UseItem(Player player)
        {
            int type = ModContent.NPCType<Luminaris>();
            if (Main.netMode != NetmodeID.MultiplayerClient)
                NPC.SpawnOnPlayer(player.whoAmI, type);
            else
                NetMessage.SendData(MessageID.SpawnBossUseLicenseStartEvent, number: player.whoAmI, number2: type);

            return true;
        }
        public override void AddRecipes()
        {
            CreateRecipe()
                .AddIngredient<StarlitScaleDust>(6)
                .AddIngredient(ItemID.HallowedBar, 4)
                .AddTile(TileID.MythrilAnvil)
                .Register();
        }
    }
}
