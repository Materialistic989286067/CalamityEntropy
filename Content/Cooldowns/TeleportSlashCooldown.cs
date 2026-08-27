using CalamityEntropy.Content.Items.Donator;
using CalamityEntropy.Core.Cooldowns;
using Terraria;
using Terraria.Audio;
using Terraria.Localization;

namespace CalamityEntropy.Content.Cooldowns
{
    public class TeleportSlashCooldown : CECooldownHandler
    {
        public static new string ID => "TeleportSlashCooldown";
        public override bool ShouldDisplay => true;
        public override LocalizedText DisplayName => Language.GetOrRegister("Mods.CalamityEntropy.TeleportSlashCooldown");
        public override string Texture => "CalamityEntropy/Content/Cooldowns/TeleportSlashCooldown";
        public override Color OutlineColor => Color.SkyBlue;
        public override Color CooldownStartColor => Color.DarkRed;
        public override Color CooldownEndColor => Color.Firebrick;
        public override bool CanTickDown => Main.LocalPlayer.HeldItem.ModItem is TlipocasScythe;

        public override SoundStyle? EndSound => new("CalamityEntropy/Assets/Sounds/MantleCDEnd");
    }
}
