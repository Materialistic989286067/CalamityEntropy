using CalamityEntropy.Content.Rarities;
using CalamityEntropy.Content.Tiles;
using Microsoft.Xna.Framework.Graphics;
using Terraria.ID;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;

namespace CalamityEntropy.Content.Items.Accessories
{
    [AutoloadEquip(EquipType.Shield)]
    public class OdinsRefuge : ModItem
    {

        public override void SetDefaults()
        {
            Item.width = 86;
            Item.height = 86;
            Item.value = Item.buyPrice(platinum: 1, gold: 50);
            Item.rare = ModContent.RarityType<VoidPurple>();
            Item.accessory = true;
            Item.defense = 24;

        }

        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            player.Entropy().holyMantle = true;
            // 脱离灾厄:原委托灾厄阿斯加德神盾/神明壁垒效果,改为等价自有防御包(表外裁定,数值供收尾实测调)
            player.noKnockback = true;
            player.fireWalk = true;
            player.buffImmune[BuffID.OnFire] = true;
            player.buffImmune[BuffID.OnFire3] = true;
            player.endurance += 0.05f;
            player.lifeRegen += 4;

            //Panic Necklace effect if enabled
            player.panic = panicNecklaceEnabled;
        }
        #region Toggleable Panic Necklace

        bool panicNecklaceEnabled = true;
        public override void ModifyTooltips(List<TooltipLine> tooltips)
        {
            tooltips.FindAndReplace("[TOGGLE]", this.GetLocalizedValue("ToggleEffect" + (panicNecklaceEnabled ? "On" : "Off")));
        }
        public override bool CanRightClick() => Main.keyState.PressingShift();
        public override void RightClick(Player player)
        {
            panicNecklaceEnabled = !panicNecklaceEnabled;
            Item.NetStateChanged();
        }
        public override bool ConsumeItem(Player player) => false;
        public override void SaveData(TagCompound tag)
        {
            tag.Add("panic", panicNecklaceEnabled);
        }

        public override void LoadData(TagCompound tag)
        {
            panicNecklaceEnabled = tag.GetBool("panic");
        }

        public override void NetSend(BinaryWriter writer)
        {
            writer.Write(panicNecklaceEnabled);
        }

        public override void NetReceive(BinaryReader reader)
        {
            panicNecklaceEnabled = reader.ReadBoolean();
        }

        public override void PostDrawInInventory(SpriteBatch spriteBatch, Vector2 position, Rectangle frame, Color drawColor, Color itemColor, Vector2 origin, float scale)
        {
            CEUtils.DrawInventoryDot(spriteBatch, position, new Vector2(16, 16) * Main.inventoryScale, panicNecklaceEnabled);
        }
        #endregion
        public override void AddRecipes()
        {
            // 脱离灾厄:两件灾厄盾饰原料改为原版顶级防御饰品(表外裁定,档位由虚空井/虚空锭把关)
            CreateRecipe().
                AddIngredient(ItemID.AnkhShield, 1).
                AddIngredient(ItemID.PaladinsShield, 1).
                AddIngredient(ModContent.ItemType<HolyMantle>(), 1).
                AddIngredient(ModContent.ItemType<VoidBar>(), 10).
                AddTile(ModContent.TileType<VoidWellTile>()).
                Register();
        }
    }
}
