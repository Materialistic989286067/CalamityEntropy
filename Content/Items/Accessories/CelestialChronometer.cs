using CalamityEntropy.Content.Items.Donator;
using CalamityEntropy.Content.Rarities;
using CalamityEntropy.Content.Tiles;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Accessories
{
    public class CelestialChronometer : ModItem, IDonatorItem
    {
        public string DonatorName => "丰川祥子";
        public override void SetDefaults()
        {
            Item.width = 40;
            Item.height = 40;
            Item.value = Item.buyPrice(platinum: 1, gold: 50);
            Item.rare = ModContent.RarityType<NihilityBlue>();
            Item.accessory = true;
            Item.defense = 28;
        }
        public override void UpdateAccessory(Player player, bool hideVisual)
        {
            Vector2 c = (player.Center + new Vector2(0, player.height / 2 - 2)) / 16;
            if (!hideVisual && Main.rand.NextBool(10))
            {
                if (Main.netMode != NetmodeID.MultiplayerClient && TileLoader.CanPlace((int)c.X, (int)c.Y, 84) && Main.tile[(int)c.X, (int)c.Y + 1].HasTile)
                {
                    List<int> CanPlace = new();
                    int t = Main.tile[(int)c.X, (int)c.Y + 1].TileType;
                    bool cpls = (!((Main.tile[(int)c.X, (int)c.Y + 1]).Get<TileWallWireStateData>().Slope != SlopeType.Solid || (Main.tile[(int)c.X, (int)c.Y + 1]).Get<TileWallWireStateData>().IsHalfBlock));
                    if (cpls)
                    {
                        if (t == 0 || t == 59)
                        {
                            CanPlace.Add(2);
                        }
                        if (t == 2 || t == 109 || t == 477 || t == 492)
                        {
                            CanPlace.Add(0);
                        }
                        if (t == 23 || t == 661 || t == 199 || t == 662 || t == 15 || t == 203)
                        {
                            CanPlace.Add(3);
                        }
                        if (t == 57 || t == 633)
                        {
                            CanPlace.Add(5);
                        }
                        if (t == 53 || t == 234)
                        {
                            CanPlace.Add(4);
                        }
                        if (t == 60)
                        {
                            CanPlace.Add(1);
                        }
                        if (t == 147 || t == 161 || t == 163 || t == 164 || t == 200)
                        {
                            CanPlace.Add(7);
                        }
                        if (CanPlace.Count > 0)
                        {
                            short fx = (short)(18 * CanPlace[Main.rand.Next(CanPlace.Count)]);
                            var tl = CEUtils.PlaceTile((int)c.X, (int)c.Y, 83);
                            tl.Get<TileWallWireStateData>().TileFrameX = fx;
                            tl.Get<TileWallWireStateData>().TileFrameY = 0;
                        }
                    }
                }
            }
            player.Entropy().lifeRegenPerSec += 4;
            if (CEUtils.inWorld((int)c.X, (int)c.Y) && Main.tile[(int)c.X, (int)c.Y].HasTile)
            {
                int type = Main.tile[(int)c.X, (int)c.Y].TileType;
                if (type >= 82 && type <= 84)
                {
                    player.endurance += 0.2f;
                }
            }
            // 脱离灾厄:原委托三件灾厄回复饰品(血神圣杯/吸收者/光辉)的效果,改为等价自有回复包(表外裁定,数值供收尾实测调)
            player.Entropy().lifeRegenPerSec += 2;
            player.endurance += 0.05f;
            player.Entropy().LifeStealP += 0.01f;
        }

        public override void AddRecipes()
        {
            // 脱离灾厄:三件灾厄回复饰品原料改为原版回复饰品(misc-map 同类先例)
            CreateRecipe().
                    AddIngredient(ItemID.CharmofMyths).
                    AddIngredient(ItemID.FrozenTurtleShell).
                    AddIngredient(ItemID.CelestialShell).
                    AddIngredient(5295).
                    AddIngredient<FadingRunestone>(3).
                    AddTile<VoidWellTile>().
                    Register();
        }

        public override void ModifyTooltips(List<TooltipLine> tooltips)
        {
            foreach (var t in tooltips)
            {
                if (t.Mod == "Terraria")
                {
                    if (t.Text.Contains("$"))
                    {
                        t.OverrideColor = Color.Lerp(Color.White, Main.DiscoColor, (float)(Math.Sin(Main.GlobalTimeWrappedHourly * 10) * 0.5f + 0.5f));
                    }
                }
            }
        }
    }
}
