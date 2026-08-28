using CalamityEntropy.Content.Items.VoidInvasion;
using Terraria;
using Terraria.ModLoader;

namespace CalamityEntropy.Common.LoreReworks
{
    public class LEVoidPope : LoreEffect
    {
        public override int ItemType => ModContent.ItemType<VoidPopeLore>();

        public override void UpdateEffects(Player player)
        {
            //旗标每帧由 ResetEffects 复位;受伤减免结算在 EModPlayer.ModifyHitByNPC/ModifyHitByProjectile
            player.Entropy().voidPopeLoreGuard = true;
        }

        public override void ModifyTooltip(TooltipLine tooltip)
        {
            tooltip.Text = tooltip.Text.Replace("{1}", VoidPopeLore.voidDamageReduction.ToPercent().ToString());
        }
    }
}
