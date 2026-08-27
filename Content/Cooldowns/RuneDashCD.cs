using CalamityEntropy.Core.Cooldowns;
using Terraria.Audio;
using Terraria.Localization;

namespace CalamityEntropy.Content.Cooldowns
{
    public class RuneDashCD : CECooldownHandler
    {
        public static new string ID => "RuneDashCD";
        public override bool ShouldDisplay => true;
        public override LocalizedText DisplayName => Language.GetOrRegister("Mods.CalamityEntropy.RuneDashCD");
        public override string Texture => "CalamityEntropy/Content/Cooldowns/RuneDashCD";
        public override Color OutlineColor => Color.SkyBlue;
        public override Color CooldownStartColor => Color.SkyBlue;
        public override Color CooldownEndColor => Color.SkyBlue;

        // 占位音效:原为灾厄 CometShardUse,待 sound-map 定稿复核
        public override SoundStyle? EndSound => new("CalamityEntropy/Assets/Sounds/MantleCDEnd");
    }
}
