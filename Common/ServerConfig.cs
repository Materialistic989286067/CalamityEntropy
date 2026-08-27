using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace CalamityEntropy.Common
{
    public class ServerConfig : ModConfig
    {
        public static ServerConfig Instance => ModContent.GetInstance<ServerConfig>();
        public override ConfigScope Mode => ConfigScope.ServerSide;
        [Header("Misc")]

        //脱离灾厄:ClearStealthWhenChangeEquipSet 已随盗贼潜行系统退役删除
        [DefaultValue(false)]
        public bool BramblecleaveAlwaysUnlockAllSkill { get; set; }

        [DefaultValue(true)]
        public bool ExtraItemsInStarterBag { get; set; }

        [DefaultValue(false)]
        public bool LoreSpecialEffect { get; set; }

        [Range(0f, 100f)]
        [DefaultValue(0f)]
        [Increment(0.5f)]
        public float LeastDamageSufferedBasedOnMaxHealth { get; set; }

        //脱离灾厄:RogueAccessoriesProvide40Stealth 已随盗贼潜行系统退役删除
        [DefaultValue(true)]
        [ReloadRequired]
        public bool EnableArmorPrefix { get; set; }
    }
}
