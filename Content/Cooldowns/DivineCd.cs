using CalamityEntropy.Core.Cooldowns;
using Terraria.Audio;
using Terraria.Localization;

namespace CalamityEntropy.Content.Cooldowns
{
    public class DivineCd : CECooldownHandler
    {
        public static new string ID => "DivingSield";
        public override bool ShouldDisplay => true;
        public override LocalizedText DisplayName => Language.GetOrRegister("Mods.CalamityEntropy.CdDS");
        public override string Texture => "CalamityEntropy/Content/Cooldowns/DivineShield";
        public override Color OutlineColor => new Color(197, 165, 108);
        public override Color CooldownStartColor => new Color(144, 84, 29);
        public override Color CooldownEndColor => Color.Khaki;

        // 占位音效:原为灾厄 AscendantOff,待 sound-map 定稿复核
        public override SoundStyle? EndSound => new("CalamityEntropy/Assets/Sounds/soulshine");
    }
}
