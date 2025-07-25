﻿using CalamityEntropy.Common;
using CalamityMod;
using CalamityMod.Items.LoreItems;
using System.Collections.Generic;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items
{
    public class ProphetLore : LoreItem
    {
        public static float ImmuneAdd = 0.5f;
        public static int LifeRegen = 1;
        public override void SetStaticDefaults()
        {
            base.SetStaticDefaults();
        }
        public override void ModifyTooltips(List<TooltipLine> tooltips)
        {
            if (ModContent.GetInstance<ServerConfig>().LoreSpecialEffect)
            {
                TooltipLine tooltipLineEF = new TooltipLine(base.Mod, "Entropy:Effect", Language.GetTextValue("Mods.CalamityEntropy.UseToggle"));
                if (LoreColor.HasValue)
                {
                    tooltipLineEF.OverrideColor = LoreColor.Value;
                }
                tooltips.Add(tooltipLineEF);
                TooltipLine tooltipLineA = new TooltipLine(base.Mod, "Entropy:Effect", Language.GetTextValue("Mods.CalamityEntropy.ProphetLoreEffect"));
                if (LoreColor.HasValue)
                {
                    tooltipLineA.OverrideColor = LoreColor.Value;
                }
                tooltipLineA.Text = tooltipLineA.Text.Replace("{2}", LifeRegen.ToString());

                tooltips.Add(tooltipLineA);

                TooltipLine tooltipLineE = new TooltipLine(base.Mod, "Entropy:Effect", Language.GetTextValue("Mods.CalamityEntropy." + (Main.LocalPlayer.Entropy().ProphetLoreBonus ? "Enabled" : "Disabled")));
                tooltipLineE.OverrideColor = Main.LocalPlayer.Entropy().ProphetLoreBonus ? Color.Yellow : Color.Gray;
                tooltips.Add(tooltipLineE);
            }

            TooltipLine tooltipLine = new TooltipLine(base.Mod, "CalamityMod:Lore", Language.GetTextValue("Mods.CalamityEntropy.loreProphet"));
            if (LoreColor.HasValue)
            {
                tooltipLine.OverrideColor = LoreColor.Value;
            }

            CalamityUtils.HoldShiftTooltip(tooltips, new TooltipLine[1] { tooltipLine }, hideNormalTooltip: true);
        }
        public override bool CanUseItem(Player player)
        {
            return ModContent.GetInstance<ServerConfig>().LoreSpecialEffect;
        }
        public override void SetDefaults()
        {
            Item.width = 20;
            Item.height = 20;
            Item.useAnimation = 30;
            Item.useTime = 30;
            Item.useStyle = ItemUseStyleID.HoldUp;
            Item.rare = ItemRarityID.Cyan;
            SoundStyle s = new("CalamityEntropy/Assets/Sounds/soulshine");
            Item.UseSound = s;
            Item.maxStack = 1;
            Item.useTurn = true;
        }
        public override bool? UseItem(Player player)
        {
            EModPlayer modPlayer = player.Entropy();
            player.itemTime = Item.useTime;
            modPlayer.ProphetLoreBonus = !modPlayer.ProphetLoreBonus;
            return true;
        }
    }
}
