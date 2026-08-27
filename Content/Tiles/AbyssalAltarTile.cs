using CalamityEntropy.Content.Items;
using CalamityEntropy.Content.NPCs.Cruiser;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;

namespace CalamityEntropy.Content.Tiles
{
    public class AbyssalAltarTile : ModTile
    {
        public override void SetStaticDefaults()
        {
            RegisterItemDrop(ModContent.ItemType<AbyssalAltar>());
            TileObjectData.newTile.CopyFrom(TileObjectData.Style6x3);
            TileObjectData.newTile.LavaDeath = false;
            TileObjectData.newTile.UsesCustomCanPlace = true;
            TileObjectData.newTile.AnchorBottom = new AnchorData(AnchorType.SolidTile | AnchorType.SolidWithTop, 4, 0);
            TileObjectData.addTile(Type);
            Main.tileFrameImportant[(int)base.Type] = true;
            AddMapEntry(new Color(134, 180, 240), CEUtils.GetItemName<AbyssalAltar>());

            // 脱离灾厄:原用灾厄紫色宇宙尘,改原版紫炬光尘
            DustType = DustID.PurpleTorch;

            // 脱离灾厄:灾厄合成站邻接(宇宙砧/嘉登熔炉/SCal祭坛)移除,远古操纵机已覆盖对应职能
            AdjTiles = new int[] {
                TileID.WorkBenches,
                TileID.Chairs,
                TileID.Tables,
                TileID.Anvils,
                TileID.MythrilAnvil,
                TileID.Furnaces,
                TileID.Hellforge,
                TileID.AdamantiteForge,
                TileID.TinkerersWorkbench,
                TileID.LunarCraftingStation,
                TileID.DemonAltar
            };
        }

        public override bool RightClick(int i, int j)
        {
            if (Main.LocalPlayer.HeldItem.type == ModContent.ItemType<WyrmTooth>())
            {
                // 脱离灾厄:原召唤灾厄渊海灾虫,其进度槽位已并入自有巡游者(progression-map)
                Player player = Main.LocalPlayer;
                int type = ModContent.NPCType<CruiserHead>();
                if (NPC.AnyNPCs(type))
                {
                    return false;
                }
                if (Main.netMode != NetmodeID.MultiplayerClient)
                    NPC.SpawnOnPlayer(player.whoAmI, type);
                else
                    NetMessage.SendData(MessageID.SpawnBossUseLicenseStartEvent, number: player.whoAmI, number2: type);

                return true;
            }
            return false;
        }
    }
}
