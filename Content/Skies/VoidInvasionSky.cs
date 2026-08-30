using CalamityEntropy.Assets.Register;
using CalamityEntropy.Content.Events;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.Graphics.Effects;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Skies
{
    /// <summary>
    /// 虚空入侵事件天空开关(演出三迭):驱动源是已同步的世界态 <see cref="VoidInvasion.Active"/>,
    /// 事件侧不再借用 EModPlayer.VortexSky(原 VoidVortexSky 占位天空连同该字段转入休眠,本体零改动)。
    /// </summary>
    public class VoidInvasionScene : ModSceneEffect
    {
        public override SceneEffectPriority Priority => SceneEffectPriority.Event;

        public override bool IsSceneEffectActive(Player player) => VoidInvasion.Active;

        public override void SpecialVisuals(Player player, bool isActive)
        {
            player.ManageSpecialBiomeVisuals("CalamityEntropy:VoidInvasion", isActive);
        }
    }

    /// <summary>
    /// 虚空入侵氛围天空:天幕自上而下压暗染紫(渐变,地平线附近弱)+ 远景虚空微粒漂浮 + 偶发裂隙细光。
    /// 纯 SpriteBatch 自绘,不依赖滤镜管线,Config.EnablePixelEffect 关闭时同样成立;
    /// 全局天色/日光的保底染紫由 <see cref="VoidInvasionSunTint"/> 承担(天空/背景不可用时仍然成立)。
    /// 强度 = 淡入淡出 opacity × (基础 + 进度加深),进度读已同步的 <see cref="VoidInvasion.Progress"/>,纯客户端视觉。
    /// </summary>
    public class VoidInvasionSky : CustomSky
    {
        // ---- 可调常量:强度曲线 ----
        //淡入淡出步长(约 2.2 秒全程)
        private const float FadeStep = 0.0075f;
        //强度曲线:strength = opacity × (ProgressFloor + (1 - ProgressFloor) × Progress),0% 起于 35%,99% 逼近满值
        private const float ProgressFloor = 0.35f;

        // ---- 可调常量:天幕渐变 ----
        //顶部压暗色、顶部最大不透明度、全屏轻罩不透明度(地平线附近的弱染色)、渐变触底位置(屏高比例)与分带数
        private static readonly Color SkyTint = new Color(22, 8, 44);
        private const float TopAlphaMax = 0.5f;
        private const float WashAlphaMax = 0.1f;
        private const float GradientBottomRatio = 0.72f;
        private const int GradientBands = 20;

        // ---- 可调常量:微粒与裂隙 ----
        //微粒数量上限 = MoteCapBase + MoteCapScale × 强度;主色
        private const int MoteCapBase = 14;
        private const int MoteCapScale = 34;
        private static readonly Color MoteColor = new Color(150, 92, 255);
        //裂隙细光同屏上限与主色
        private const int RiftMaxAlive = 2;
        private static readonly Color RiftColor = new Color(192, 132, 255);
        //远景视差(相对镜头位移的跟随比例,越小越"远")
        private const float Parallax = 0.08f;

        /// <summary>当前强度(0~1),每 tick 由 Update 写入,供 <see cref="VoidInvasionSunTint"/> 全局染色读取。本端视觉标量,非玩家状态。</summary>
        public static float GlobalStrength;

        private bool skyActive;
        private float opacity;
        //进度的客户端平滑镜像:逐杀的进度跳变缓升,胜利/中断时 Progress 骤归零也不会闪降
        private float easedProgress;
        private int counter;
        private int riftTimer = 420;
        private readonly List<VoidMote> motes = new();
        private readonly List<RiftFlash> rifts = new();

        //远景漂浮微粒:pos 存在视差空间,绘制时按 Parallax 折算并回绕到屏幕附近
        private class VoidMote
        {
            public Vector2 pos;
            public Vector2 drift;
            public float scale;
            public float baseAlpha;
            public float seed;
            public bool bright;
            public int life;
            public int maxLife;
        }

        //偶发裂隙细光:近竖直的细长闪光,正弦包络淡入淡出
        private class RiftFlash
        {
            public Vector2 pos;
            public float rot;
            public float length;
            public int life;
            public int maxLife;
        }

        public override void Deactivate(params object[] args)
        {
            skyActive = VoidInvasion.Active;
        }

        public override void Reset()
        {
            skyActive = false;
            easedProgress = 0f;
            motes.Clear();
            rifts.Clear();
            GlobalStrength = 0f;
        }

        public override bool IsActive() => skyActive || opacity > 0f;

        public override void Activate(Vector2 position, params object[] args)
        {
            skyActive = true;
        }

        /// <summary>当前强度:淡入淡出 × (基础 + 平滑进度加深)。</summary>
        private float Strength => opacity * (ProgressFloor + (1f - ProgressFloor) * easedProgress);

        public override void Update(GameTime gameTime)
        {
            if (!VoidInvasion.Active || Main.gameMenu)
                skyActive = false;

            if (skyActive && opacity < 1f)
                opacity = Math.Min(1f, opacity + FadeStep);
            else if (!skyActive && opacity > 0f)
                opacity = Math.Max(0f, opacity - FadeStep);

            easedProgress += (VoidInvasion.Progress - easedProgress) * 0.02f;
            float strength = Strength;
            GlobalStrength = strength;
            Opacity = opacity;
            counter++;

            if (strength <= 0f || Main.gameMenu)
                return;

            //微粒补充:数量上限随强度(含进度)缓慢上浮,寿命耗尽自然换代
            int cap = MoteCapBase + (int)(MoteCapScale * strength);
            if (motes.Count < cap && Main.rand.NextBool(3))
            {
                var m = new VoidMote
                {
                    pos = Main.screenPosition * Parallax + new Vector2(Main.rand.NextFloat(Main.screenWidth), Main.rand.NextFloat(Main.screenHeight * 0.8f)),
                    drift = new Vector2(Main.rand.NextFloat(-0.22f, 0.22f), -Main.rand.NextFloat(0.08f, 0.34f)),
                    scale = Main.rand.NextFloat(6f, 18f),
                    baseAlpha = Main.rand.NextFloat(0.1f, 0.26f),
                    seed = Main.rand.NextFloat(MathHelper.TwoPi),
                    bright = Main.rand.NextBool(5),
                    maxLife = Main.rand.Next(360, 780),
                };
                m.life = m.maxLife;
                motes.Add(m);
            }
            for (int i = motes.Count - 1; i >= 0; i--)
            {
                VoidMote m = motes[i];
                m.pos += m.drift;
                if (--m.life <= 0)
                    motes.RemoveAt(i);
            }

            //裂隙细光:偶发,进度越深间隔越短
            if (--riftTimer <= 0 && rifts.Count < RiftMaxAlive && strength > 0.12f)
            {
                rifts.Add(new RiftFlash
                {
                    pos = Main.screenPosition * Parallax + new Vector2(Main.rand.NextFloat(0.08f, 0.92f) * Main.screenWidth, Main.rand.NextFloat(0.06f, 0.5f) * Main.screenHeight),
                    rot = MathHelper.PiOver2 + Main.rand.NextFloat(-0.65f, 0.65f),
                    length = Main.rand.NextFloat(160f, 380f),
                    maxLife = Main.rand.Next(80, 130),
                });
                riftTimer = (int)(Main.rand.Next(300, 680) * (1.15f - 0.65f * VoidInvasion.Progress));
            }
            for (int i = rifts.Count - 1; i >= 0; i--)
            {
                if (++rifts[i].life >= rifts[i].maxLife)
                    rifts.RemoveAt(i);
            }
        }

        /// <summary>还原 Main.DoDraw 传给 DrawBG 的背景矩阵(含重力翻转与背景缩放补偿),保证自开批次与调用方同空间。</summary>
        private static Matrix BackgroundMatrix()
        {
            Matrix m = Main.BackgroundViewMatrix.TransformationMatrix;
            m.Translation -= Main.BackgroundViewMatrix.ZoomMatrix.Translation
                * new Vector3(1f, Main.BackgroundViewMatrix.Effects.HasFlag(SpriteEffects.FlipVertically) ? -1f : 1f, 1f);
            return m;
        }

        //把视差空间坐标回绕进屏幕邻域,镜头大位移时微粒不散场
        private static float WrapF(float v, float min, float max)
        {
            float range = max - min;
            v = (v - min) % range;
            if (v < 0f)
                v += range;
            return v + min;
        }

        public override void Draw(SpriteBatch spriteBatch, float minDepth, float maxDepth)
        {
            //只在最远深度切片画一拍:DrawToDepth 每个背景视差层都会回调,不门控会整套重画
            //(旧 VoidVortexSky 的过曝正是 7 层加法 × 每帧十余个切片叠出来的)
            if (maxDepth < float.MaxValue || minDepth >= float.MaxValue)
                return;
            //地表事件氛围:镜头压到地下/地狱背景时不画(地狱背景同样会回调 SkyManager)
            if (Main.screenPosition.Y / 16.0 > Main.worldSurface + 60.0)
                return;
            float strength = Strength;
            if (strength <= 0.004f)
                return;

            //1) 天幕渐变:留在调用方批次(AlphaBlend + 背景矩阵)。DrawBG 期间 Main.screenWidth/Height
            //   已按背景缩放预除,矩形配合矩阵恰好铺满全屏;分带近似竖直渐变,顶部最浓,触底归零(地平线附近弱)
            Texture2D pixel = CEExtraAssets.white;
            spriteBatch.Draw(pixel, new Rectangle(0, 0, Main.screenWidth, Main.screenHeight), SkyTint * (WashAlphaMax * strength));
            int bottom = (int)(Main.screenHeight * GradientBottomRatio);
            int bandH = Math.Max(1, bottom / GradientBands);
            for (int i = 0; i < GradientBands; i++)
            {
                float fade = 1f - i / (float)GradientBands;
                float alpha = TopAlphaMax * strength * fade * fade;
                spriteBatch.Draw(pixel, new Rectangle(0, i * bandH, Main.screenWidth, bandH + 1), SkyTint * alpha);
            }

            //2) 微粒与裂隙走加法批次,画完按 Main.DoDraw 开 DrawBG 批次的参数还原
            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp, DepthStencilState.None, Main.Rasterizer, null, BackgroundMatrix());

            Texture2D glow = CEExtraAssets.Glow;
            Vector2 glowOrigin = glow.Size() / 2f;
            foreach (VoidMote m in motes)
            {
                float env = MathF.Sin(MathF.PI * (m.life / (float)m.maxLife));
                float twinkle = 0.82f + 0.18f * MathF.Sin(counter * 0.06f + m.seed);
                Color c = m.bright ? new Color(228, 205, 255) : MoteColor;
                Vector2 dp = new Vector2(
                    WrapF(m.pos.X - Main.screenPosition.X * Parallax, -60f, Main.screenWidth + 60f),
                    WrapF(m.pos.Y - Main.screenPosition.Y * Parallax, -60f, Main.screenHeight * 0.85f));
                spriteBatch.Draw(glow, dp, null, c * (m.baseAlpha * env * twinkle * strength), 0f, glowOrigin, m.scale / glow.Width, SpriteEffects.None, 0f);
            }

            Texture2D streak = CEExtraAssets.StreakFaded;
            Vector2 streakOrigin = streak.Size() / 2f;
            foreach (RiftFlash r in rifts)
            {
                float env = MathF.Sin(MathF.PI * (r.life / (float)r.maxLife));
                float alpha = env * strength;
                Vector2 dp = new Vector2(
                    WrapF(r.pos.X - Main.screenPosition.X * Parallax, -80f, Main.screenWidth + 80f),
                    WrapF(r.pos.Y - Main.screenPosition.Y * Parallax, -80f, Main.screenHeight * 0.7f));
                //细缝本体 + 更宽的淡晕 + 中心辉光,读作"天幕裂开一线"
                Vector2 scale = new Vector2(r.length / streak.Width, (7f + 5f * env) / streak.Height);
                spriteBatch.Draw(streak, dp, null, RiftColor * (alpha * 0.75f), r.rot, streakOrigin, scale, SpriteEffects.None, 0f);
                spriteBatch.Draw(streak, dp, null, RiftColor * (alpha * 0.3f), r.rot, streakOrigin, scale * new Vector2(1f, 2.6f), SpriteEffects.None, 0f);
                spriteBatch.Draw(glow, dp, null, new Color(225, 195, 255) * (alpha * 0.5f), 0f, glowOrigin, r.length * 0.16f / glow.Width, SpriteEffects.None, 0f);
            }

            spriteBatch.End();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, Main.Rasterizer, null, BackgroundMatrix());
        }

        //云随侵染变稀,让紫幕更通透
        public override float GetCloudAlpha() => 1f - 0.45f * Strength;
    }

    /// <summary>
    /// 事件全局天色/日光染紫(先乘法再插值,只压暗不提亮,夜晚不会被点亮)。
    /// 走 tML 的 ModifySunLightColor,与滤镜/像素特效管线完全无关:
    /// Retro 光照、EnablePixelEffect = false、背景被禁用时,"天色被虚空侵染"仍然成立,是优雅退化的保底层。
    /// </summary>
    public class VoidInvasionSunTint : ModSystem
    {
        // ---- 可调常量:乘法染色目标与最大混合比 ----
        private static readonly Color SkyMultiply = new Color(135, 82, 198);
        private static readonly Color SunMultiply = new Color(206, 180, 236);
        private const float SkyLerpMax = 0.6f;
        private const float SunLerpMax = 0.45f;

        public override void ModifySunLightColor(ref Color tileColor, ref Color backgroundColor)
        {
            float s = VoidInvasionSky.GlobalStrength;
            if (s <= 0f)
                return;
            backgroundColor = Color.Lerp(backgroundColor, backgroundColor.MultiplyRGB(SkyMultiply), SkyLerpMax * s);
            tileColor = Color.Lerp(tileColor, tileColor.MultiplyRGB(SunMultiply), SunLerpMax * s);
        }
    }
}
