using CalamityEntropy.Common;
using CalamityEntropy.Content.NPCs.VoidInvasion;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.Graphics.Effects;
using Terraria.Graphics.Shaders;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Skies
{
    /// <summary>
    /// 教皇 P3 领域滤镜数据(演出二迭,C 队):键 CalamityEntropy:PopeDomain,
    /// 背靠新链路 PopeDomainFilter.fxc(域内轻微紫偏色 + 域外去饱和压暗,边界过渡带)。
    /// 激活由 <see cref="VoidPope"/> P3Upkeep 客户端侧驱动(EnablePixelEffect 关闭即不激活,优雅退化);
    /// 本类 Update 自灭:教皇缺位或配置关闭时主动 Deactivate(镜像 CrScreenShaderData 姿势)。
    /// 强度包络 = domainRadiusFactor(展开缓入,死亡爆碎后自然归零)。
    /// </summary>
    public class PopeDomainShaderData : ScreenShaderData
    {
        private int popeIndex = -1;

        public PopeDomainShaderData(Ref<Effect> shader, string passName)
            : base(shader, passName)
        {
        }

        private VoidPope FindPope()
        {
            if (popeIndex >= 0 && popeIndex < Main.maxNPCs)
            {
                NPC cached = Main.npc[popeIndex];
                if (cached.active && cached.ModNPC is VoidPope cachedPope && cachedPope.phase >= 3)
                {
                    return cachedPope;
                }
            }
            popeIndex = -1;
            foreach (NPC n in Main.ActiveNPCs)
            {
                if (n.ModNPC is VoidPope pope && pope.phase >= 3)
                {
                    popeIndex = n.whoAmI;
                    return pope;
                }
            }
            return null;
        }

        public override void Update(GameTime gameTime)
        {
            if (FindPope() == null || !ModContent.GetInstance<Config>().EnablePixelEffect)
            {
                Filters.Scene["CalamityEntropy:PopeDomain"].Deactivate(Array.Empty<object>());
            }
            base.Update(gameTime);
        }

        public override void Apply()
        {
            VoidPope pope = FindPope();
            if (pope != null)
            {
                UseTargetPosition(pope.DomainAnchor);
                UseIntensity(pope.DomainRadius);
                UseOpacity(0.9f * MathHelper.Clamp(pope.domainRadiusFactor, 0f, 1f));
                Shader?.Parameters["uZoomCE"]?.SetValue(Main.GameViewMatrix.Zoom.X);
            }
            base.Apply();
        }
    }
}
