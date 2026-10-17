using CalamityEntropy.Assets.Register;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using Terraria;

namespace CalamityEntropy.Content.NPCs.VoidDestroyer
{
    /// <summary>
    /// 虚空爆炸体(VDVoidBlast 着色器):Scale 是等离子壳外缘的最终半径(像素),配色与暗核强度由 <see cref="Configure"/> 给。
    /// 自己 End / Begin 着色器批次,画完按自身绘制模式还原 PRT 批次;着色器缺失退回加法 Glow + BloomRing
    /// </summary>
    public class PRT_VDVoidBlast : BasePRT
    {
        /// <summary>着色器里壳外缘的归一半径(uShell);方块半边长 = Scale / ShellRadius</summary>
        public const float ShellRadius = 0.7f;

        public float Seed;
        public float Darkness = 0.75f;
        public Color HotColor = new Color(255, 220, 255);
        public Color MainColor = VDVfx.VoidPurple;
        public Color CoolColor = new Color(92, 36, 158);
        public Color DarkColor = new Color(8, 0, 18);

        public override string Texture => CEUtils.WhiteTexPath;

        public PRT_VDVoidBlast Configure(int lifetime, float darkness, Color hot, Color main, Color cool) {
            Lifetime = lifetime;
            Darkness = darkness;
            HotColor = hot;
            MainColor = main;
            CoolColor = cool;
            Seed = Main.rand.NextFloat(10f);
            return this;
        }

        public override void SetProperty() {
            ShouldKillWhenOffScreen = false;
            PRTDrawMode = PRTDrawModeEnum.AlphaBlend;
            if (Lifetime <= 0) {
                Lifetime = 36;
            }
        }

        public override bool PreDraw(SpriteBatch sb) {
            float t = LifetimeCompletion;
            Vector2 pos = Position - Main.screenPosition;
            float half = Scale / ShellRadius;
            Effect fx = CEEffectAssets.VDVoidBlast?.Value;
            if (fx == null) {
                DrawFallback(sb, pos, t);
                return false;
            }
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone, fx, Main.GameViewMatrix.TransformationMatrix);
            GraphicsDevice gd = Main.instance.GraphicsDevice;
            gd.Textures[1] = CEUtils.getExtraTex("GradientNoise");
            gd.SamplerStates[1] = SamplerState.LinearWrap;
            gd.Textures[2] = CEUtils.getExtraTex("SwirlNoise");
            gd.SamplerStates[2] = SamplerState.LinearWrap;
            fx.Parameters["uProgress"]?.SetValue(t);
            fx.Parameters["uTime"]?.SetValue(Main.GlobalTimeWrappedHourly);
            fx.Parameters["uSeed"]?.SetValue(Seed);
            fx.Parameters["uOpacity"]?.SetValue(Opacity <= 0f ? 1f : Opacity);
            fx.Parameters["uDarkness"]?.SetValue(Darkness);
            fx.Parameters["uShell"]?.SetValue(ShellRadius);
            fx.Parameters["uColorHot"]?.SetValue(HotColor.ToVector3());
            fx.Parameters["uColorMain"]?.SetValue(MainColor.ToVector3());
            fx.Parameters["uColorCool"]?.SetValue(CoolColor.ToVector3());
            fx.Parameters["uColorDark"]?.SetValue(DarkColor.ToVector3());
            fx.CurrentTechnique.Passes[0].Apply();
            Texture2D white = CEExtraAssets.white ?? CEUtils.getExtraTex("white");
            sb.Draw(white, pos, null, Color.White, 0f, white.Size() / 2f, half * 2f / white.Width, SpriteEffects.None, 0f);
            sb.End();
            gd.Textures[1] = null;
            gd.Textures[2] = null;
            PRTLoader.BeginDrawingWithMode(PRTDrawMode, sb);
            return false;
        }

        private void DrawFallback(SpriteBatch sb, Vector2 pos, float t) {
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            Texture2D glow = CEUtils.getExtraTex("Glow");
            Texture2D ring = CEUtils.getExtraTex("BloomRing");
            float fade = 1f - t;
            sb.Draw(glow, pos, null, MainColor * (0.8f * fade), 0f, glow.Size() / 2f, Scale * 2.2f * VDVfx.EaseOut(t + 0.2f) / glow.Width, SpriteEffects.None, 0f);
            sb.Draw(glow, pos, null, HotColor * (fade * fade), 0f, glow.Size() / 2f, Scale * 1.1f / glow.Width, SpriteEffects.None, 0f);
            sb.Draw(ring, pos, null, MainColor * fade, 0f, ring.Size() / 2f, Scale * (0.4f + 0.9f * VDVfx.EaseOut(t)) / (ring.Width * 0.38f), SpriteEffects.None, 0f);
            sb.End();
            PRTLoader.BeginDrawingWithMode(PRTDrawMode, sb);
        }
    }
}
