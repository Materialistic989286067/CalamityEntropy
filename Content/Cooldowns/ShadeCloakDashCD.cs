using CalamityEntropy.Core.Cooldowns;
using Terraria.Audio;
using Terraria.Localization;

namespace CalamityEntropy.Content.Cooldowns
{
    public class ShadeCloakDashCD : CECooldownHandler
    {
        public static new string ID => "ShadeCloakDashCD";
        public override bool ShouldDisplay => true;
        public override LocalizedText DisplayName => Language.GetOrRegister("Mods.CalamityEntropy.ShadeCloakDashCD");
        public override string Texture => "CalamityEntropy/Content/Cooldowns/ShadowDashCD";
        public override Color OutlineColor => Color.Black;
        public override Color CooldownStartColor => Color.MediumPurple;
        public override Color CooldownEndColor => Color.Gray;

        public override SoundStyle? EndSound => new("CalamityEntropy/Assets/Sounds/MantleCDEnd");
    }
}
