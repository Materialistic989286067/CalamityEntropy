using CalamityEntropy.Assets.Register;
using CalamityEntropy.Core.Graphics;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace CalamityEntropy.Content.Projectiles.VoidInvasion
{
    /// <summary>
    /// 教皇演出共享绘制件(C 队,纯客户端):PopeBeam 束体的统一调用姿势。
    /// 魔盘巨激光 <see cref="DiscBeam"/>、爆弹放射激光 <see cref="DeathBombLaser"/>、
    /// 反射激光 <see cref="ReflectLaser"/> 三处共用,只差参数档。
    /// 调用约定:进入时 SpriteBatch 处于任意已 Begin 状态,返回时已恢复 Deferred/AlphaBlend
    /// (CEUtils.ReSetToEndShader 同款收尾)。只在绘制路径调用。
    /// </summary>
    internal static class PopeVfx
    {
        /// <summary>
        /// 画一段 PopeBeam 束体(白核 + 色晕 + 热浪扰动边缘)。
        /// start = 出膛端,rotation = 朝向,widthPx = 全宽,grow = 展宽包络(公平阀),
        /// flickerSeed 用于多束错相(反射激光四段各给不同值)。
        /// </summary>
        public static void DrawBeam(Vector2 start, float rotation, float length, float widthPx,
            float opacity, float grow, Color halo, Color edge, float coreFrac = 0.24f,
            float fringe = 0.55f, float flicker = 0.3f, float flickerSeed = 0f)
        {
            if (opacity <= 0.01f || length < 8f)
            {
                return;
            }
            SpriteBatch sb = Main.spriteBatch;
            Effect fx = CEFxcEffects.Get("PopeBeam");
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.LinearWrap,
                DepthStencilState.None, RasterizerState.CullNone, fx, Main.GameViewMatrix.TransformationMatrix);
            fx.Parameters["uTime"]?.SetValue(Main.GlobalTimeWrappedHourly + flickerSeed);
            fx.Parameters["uOpacity"]?.SetValue(opacity);
            fx.Parameters["uCore"]?.SetValue(coreFrac);
            fx.Parameters["uFringe"]?.SetValue(fringe);
            fx.Parameters["uGrow"]?.SetValue(MathHelper.Clamp(grow, 0.02f, 1f));
            fx.Parameters["uFlicker"]?.SetValue(flicker);
            fx.Parameters["uColorCore"]?.SetValue(new Color(255, 244, 255).ToVector3());
            fx.Parameters["uColorHalo"]?.SetValue(halo.ToVector3());
            fx.Parameters["uColorEdge"]?.SetValue(edge.ToVector3());
            Texture2D noise = CEExtraAssets.TurbulentNoise;
            sb.Draw(noise, start - Main.screenPosition, null, Color.White, rotation,
                new Vector2(0f, noise.Height / 2f), new Vector2(length / noise.Width, widthPx / noise.Height),
                SpriteEffects.None, 0);
            CEUtils.ReSetToEndShader();
        }

        /// <summary>束体端点爆花(出膛/落点的辉光核,加法批次内调用)。</summary>
        public static void DrawBeamCap(SpriteBatch sb, Vector2 pos, float scale, float opacity, Color color)
        {
            Texture2D glow = CEExtraAssets.Glow;
            sb.Draw(glow, pos - Main.screenPosition, null, color * (0.9f * opacity), 0f,
                glow.Size() / 2, scale, SpriteEffects.None, 0);
            sb.Draw(glow, pos - Main.screenPosition, null, Color.White * (0.55f * opacity), 0f,
                glow.Size() / 2, scale * 0.55f, SpriteEffects.None, 0);
        }
    }
}
