﻿using CalamityMod.Items;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
namespace CalamityEntropy.Content.Items.Books.BookMarks
{
    public class BookMarkPerfection : BookMark
    {
        public override void SetDefaults()
        {
            base.SetDefaults();
            Item.rare = ItemRarityID.Orange;
            Item.value = CalamityGlobalItem.RarityOrangeBuyPrice;
        }
        public override Texture2D UITexture => BookMark.GetUITexture("Perfection");
        public override Color tooltipColor => Color.Green;
        public override EBookProjectileEffect getEffect()
        {
            return new APlusBMEffect();
        }

        public override void ModifyStat(EBookStatModifer modifer)
        {
            if (Main.LocalPlayer.Entropy().hitTimeCount > 600)
            {
                modifer.Crit += 16;
            }
        }
    }

    public class APlusBMEffect : EBookProjectileEffect
    {
    }
}