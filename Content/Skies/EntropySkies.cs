using Terraria;
using Terraria.Graphics.Effects;

namespace CalamityEntropy.Content.Skies
{
    public static class EntropySkies
    {
        public static void setUpSkies()
        {
            //教皇 P3 领域滤镜(C 队,演出二迭):纯 Filters.Scene 键,无天空件;
            //新链路 .fxc 着色器,服务器上 CEFxcEffects.Get 返回 null,故只在客户端注册。
            if (!Main.dedServ)
            {
                Terraria.Graphics.Effects.Filters.Scene["CalamityEntropy:PopeDomain"] = new Filter(new PopeDomainShaderData(
                    new Ref<Microsoft.Xna.Framework.Graphics.Effect>(Core.Graphics.CEFxcEffects.Get("PopeDomainFilter")),
                    "PopeDomainPass"), EffectPriority.VeryHigh);
            }
            Terraria.Graphics.Effects.Filters.Scene["CalamityEntropy:Cruiser"] = new Filter(new CrScreenShaderData("FilterMiniTower").UseColor(Color.Transparent).UseOpacity(0f), EffectPriority.VeryHigh);
            SkyManager.Instance["CalamityEntropy:Cruiser"] = new CrSky();
            Terraria.Graphics.Effects.Filters.Scene["CalamityEntropy:DimensionLens"] = new Filter(new TransScreenShaderData("FilterMiniTower").UseColor(Color.Transparent).UseOpacity(0f), EffectPriority.VeryHigh);
            SkyManager.Instance["CalamityEntropy:DimensionLens"] = new LlSky();
            Terraria.Graphics.Effects.Filters.Scene["CalamityEntropy:NihTwin"] = new Filter(new TransScreenShaderData("FilterMiniTower").UseColor(Color.Transparent).UseOpacity(0f), EffectPriority.VeryHigh);
            SkyManager.Instance["CalamityEntropy:NihTwin"] = new NihTwinSky();
            Terraria.Graphics.Effects.Filters.Scene["CalamityEntropy:VoidVortex"] = new Filter(new TransScreenShaderData("FilterMiniTower").UseColor(new Color(60, 30, 100)).UseOpacity(0f), EffectPriority.VeryHigh);
            SkyManager.Instance["CalamityEntropy:VoidVortex"] = new VoidVortexSky();
            Terraria.Graphics.Effects.Filters.Scene["CalamityEntropy:Snowgrave"] = new Filter(new TransScreenShaderData("FilterMiniTower").UseColor(new Color(200, 200, 255)).UseOpacity(0f), EffectPriority.VeryHigh);
            SkyManager.Instance["CalamityEntropy:Snowgrave"] = new SnowgraveSky();
            Terraria.Graphics.Effects.Filters.Scene["CalamityEntropy:SunriseSky"] = new Filter(new TransScreenShaderData("FilterMiniTower").UseColor(Color.White).UseOpacity(0f), EffectPriority.VeryHigh);
            SkyManager.Instance["CalamityEntropy:SunriseSky"] = new SunriseSky();
        }
    }
}
