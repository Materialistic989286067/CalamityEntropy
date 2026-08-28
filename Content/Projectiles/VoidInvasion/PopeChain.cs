using CalamityEntropy.Assets.Register;
using CalamityEntropy.Content.NPCs.VoidInvasion;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using CalamityEntropy.Core.Graphics;
using InnoVault;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Projectiles.VoidInvasion
{
    /// <summary>
    /// 死怨铁索(void-invasion.md §4.0,教皇三阶段通用模板;演出二迭:链体升格为 PopeChainLink 着色器):
    /// 警示 → 8t 伸出 → 定格 10t → 12t 收回;判定在伸出与定格期活跃(打击窗与可见突刺对齐,判定几何不动)。
    /// 基点与方向在生成时锁定(方向借初速度通道原生同步,首帧转存 rotation 后清零)。
    /// 链体 = PopeChainLink.fxc(程序化链环序列 + 张力高光流动 + 绷弦余波 + 崩断侵蚀),链头 = MaliceClaw
    /// (伸出期旋开,到位一帧咬合归位 + 白闪);伸出瞬间全长空气撕裂线 + 基点方向性冲环。
    /// ai[0] = 来源手 whoAmI(-1 = 无手来源):来源是手时命中触发 <see cref="VoidPopeHand.TryGrab"/> 抓投。
    /// ai[1] = 链长(0 视为 480)。
    /// ai[2] = 警示样式:0 = 方向警示线 20t;1 = 法阵警示 25t(P1-6 阵雨,自脚下法阵竖直刺出);
    /// 2 = 合围警示线 25t(§4.2 P2-3s 六索合围:警示稍长,缺口方位不生成链即明示);
    /// 3 = 缚身链(§4.3 P3-6,M8):自领域边缘反向钉入教皇本体,不伤玩家,到位拍走"钉入"顿挫
    ///   (碎屑 + 反向冲环 + 微震 + 视觉长度弹簧回稳),ai[0] 此时复用为定格时长(tick),
    ///   定格结束走 14t 崩断演出(着色器侵蚀 + 链屑坠落)而非收回。
    /// </summary>
    public class PopeChain : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Content/Projectiles/VoidInvasion/MaliceClaw";

        [VaultLoaden("CalamityEntropy/Content/Projectiles/VoidInvasion/VoidGlyph")]
        private static Asset<Texture2D> glyphTex;
        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light")]
        private static Asset<Texture2D> glowTex;

        /// <summary>方向警示线时长(样式 0)</summary>
        public const int LineWarnTime = 20;
        /// <summary>法阵警示时长(样式 1,§4.1 P1-6:25t 预警)</summary>
        public const int GlyphWarnTime = 25;
        /// <summary>合围警示线时长(样式 2,§4.2 P2-3s:25t)</summary>
        public const int SiegeWarnTime = 25;
        public const int ExtendTime = 8;
        public const int HoldTime = 10;
        public const int RetractTime = 12;
        /// <summary>缚身链崩断演出时长(样式 3)</summary>
        public const int BreakTime = 14;
        public const float DefaultLength = 480f;

        public int SourceHandIndex => (int)Projectile.ai[0];
        public float ChainLength => Projectile.ai[1] > 0 ? Projectile.ai[1] : DefaultLength;
        public bool GlyphStyle => Projectile.ai[2] == 1;
        /// <summary>缚身样式(§4.3 P3-6):ai[0] 复用为定格时长,不伤玩家,尾段崩断</summary>
        public bool BindStyle => Projectile.ai[2] == 3;
        /// <summary>本链定格时长(缚身链由 ai[0] 给定)</summary>
        public int HoldDur => BindStyle ? Math.Max((int)Projectile.ai[0], 1) : HoldTime;
        public int WarnTime => (int)Projectile.ai[2] switch { 1 => GlyphWarnTime, 2 => SiegeWarnTime, _ => LineWarnTime };
        public int TotalLife => BindStyle
            ? WarnTime + ExtendTime + HoldDur + BreakTime
            : WarnTime + ExtendTime + HoldTime + RetractTime;

        private float Timer => TotalLife - Projectile.timeLeft;

        public override void SetStaticDefaults()
        {
            //链长 + 出屏基点也要照画(M8 缚身链自领域边缘伸出,基点可离屏更远)
            ProjectileID.Sets.DrawScreenCheckFluff[Projectile.type] = 2600;
        }

        public override void SetDefaults()
        {
            Projectile.width = 26;
            Projectile.height = 26;
            Projectile.friendly = false;
            Projectile.hostile = true;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.timeLeft = LineWarnTime + ExtendTime + HoldTime + RetractTime;
        }

        public override bool ShouldUpdatePosition() => false;

        /// <summary>当前伸出比例:伸出段锐利缓出(突刺是一次爆发),收回段平滑缓入;缚身链定格到崩断不收回。</summary>
        public float ExtendProgress
        {
            get
            {
                float t = Timer;
                if (t < WarnTime)
                {
                    return 0f;
                }
                if (t < WarnTime + ExtendTime)
                {
                    float p = (t - WarnTime) / ExtendTime;
                    return 1f - (1f - p) * (1f - p) * (1f - p);
                }
                if (t < WarnTime + ExtendTime + HoldDur)
                {
                    return 1f;
                }
                if (BindStyle)
                {
                    return 1f; //崩断段仍满长,飞散演出在 PreDraw
                }
                float r = (t - WarnTime - ExtendTime - HoldTime) / RetractTime;
                return 1f - r * r;
            }
        }

        public Vector2 TipPos => Projectile.Center + Projectile.rotation.ToRotationVector2() * ChainLength * ExtendProgress;

        public override void AI()
        {
            if (Projectile.localAI[0] == 0)
            {
                Projectile.localAI[0] = 1;
                Projectile.rotation = Projectile.velocity.ToRotation();
                Projectile.velocity = Vector2.Zero;
                //样式 1 的警示更长,补齐总寿命(timeLeft 不进生成包,双端各自按样式推同一值)
                Projectile.timeLeft = TotalLife;
                if (!Main.dedServ && GlyphStyle)
                {
                    SoundEngine.PlaySound(SoundID.Item8 with { Volume = 0.6f, Pitch = -0.5f }, Projectile.Center);
                }
            }

            float t = Timer;
            //突刺拍:破空声 + 基点方向性冲环(空气被撕开的一拍)
            if ((int)t == WarnTime && !Main.dedServ)
            {
                SoundEngine.PlaySound(SoundID.Item71 with { Volume = 1f, Pitch = -0.35f }, Projectile.Center);
                SoundEngine.PlaySound(SoundID.DD2_GhastlyGlaivePierce with { Volume = 0.8f, Pitch = -0.2f }, Projectile.Center);
                var ring = PRTLoader.NewParticle<PRT_DirectionalPulseRing>(Projectile.Center, Vector2.Zero, new Color(200, 130, 255), 0.05f);
                ring.Configure(new Vector2(2.6f, 0.7f), Projectile.rotation, 1.5f, 15);
            }
            //到位拍:爪头咬合闪光;缚身链改为"钉入"顿挫(碎屑 + 反向冲环 + 微震)
            if ((int)t == WarnTime + ExtendTime && !Main.dedServ)
            {
                Vector2 tipNow = Projectile.Center + Projectile.rotation.ToRotationVector2() * ChainLength;
                if (BindStyle)
                {
                    SoundEngine.PlaySound(SoundID.NPCHit4 with { Volume = 0.9f, Pitch = -0.45f }, tipNow);
                    ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.ScreenShake(Vector2.Zero, 3), Main.LocalPlayer.Distance(tipNow), 1400);
                    for (int i = 0; i < 8; i++)
                    {
                        Dust.NewDust(tipNow + CEUtils.randomPointInCircle(20f), 1, 1, ModContent.DustType<Dusts.GlassBreak>());
                    }
                    var pin = PRTLoader.NewParticle<PRT_DirectionalPulseRing>(tipNow, Vector2.Zero, new Color(255, 220, 160), 0.05f);
                    pin.Configure(new Vector2(0.6f, 2.2f), Projectile.rotation, 1.2f, 14);
                }
                else
                {
                    SoundEngine.PlaySound(SoundID.NPCHit4 with { Volume = 0.6f, Pitch = 0.15f }, tipNow);
                    var flash = PRTLoader.NewParticle<PRT_Light>(tipNow, Vector2.Zero, Color.White, 1.1f);
                    flash.Configure(0.85f, lifetime: 10);
                    for (int i = 0; i < 6; i++)
                    {
                        var s = PRTLoader.NewParticle<PRT_GlowSparkCal>(tipNow,
                            Projectile.rotation.ToRotationVector2().RotatedBy(Main.rand.NextFloat(-0.55f, 0.55f)) * Main.rand.NextFloat(4f, 11f),
                            new Color(200, 130, 255), 0.7f);
                        s.Configure(false, 14, new Vector2(1.9f, 0.5f), quickShrink: true);
                    }
                }
            }
            //缚身链崩断拍:链体飞散粒子 + 链屑坠落(双端各自演出;总崩断音由教皇统一播一次)
            if (BindStyle && (int)t == WarnTime + ExtendTime + HoldDur && !Main.dedServ)
            {
                for (int i = 0; i < 10; i++)
                {
                    Vector2 pos = Vector2.Lerp(Projectile.Center, TipPos, Main.rand.NextFloat());
                    var v = PRTLoader.NewParticle<PRT_Void>(pos,
                        CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(2f, 7f), Color.White, 0.9f);
                    v.Opacity = Main.rand.Next(30, 80) * 0.01f;
                }
                for (int i = 0; i < 8; i++)
                {
                    Vector2 pos = Vector2.Lerp(Projectile.Center, TipPos, Main.rand.NextFloat());
                    Vector2 vel = (Projectile.rotation + MathHelper.PiOver2 * (Main.rand.NextBool() ? 1 : -1)).ToRotationVector2()
                        * Main.rand.NextFloat(2f, 6f) + new Vector2(0f, -2f);
                    var d = PRTLoader.NewParticle<PRT_LineCal>(pos, vel, new Color(150, 84, 220), 0.8f);
                    d.Configure(true, 26);
                }
            }
            if (t >= WarnTime)
            {
                Lighting.AddLight(Vector2.Lerp(Projectile.Center, TipPos, 0.7f), 0.45f, 0.15f, 0.7f);
            }
        }

        public override bool CanHitPlayer(Player target)
        {
            if (BindStyle)
            {
                return false; //缚身链只钉教皇,不伤玩家(§4.3 P3-6)
            }
            float t = Timer;
            return t >= WarnTime && t < WarnTime + ExtendTime + HoldTime;
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            float t = Timer;
            if (BindStyle || t < WarnTime || t >= WarnTime + ExtendTime + HoldTime)
            {
                return false;
            }
            return CEUtils.LineThroughRect(Projectile.Center, TipPos, targetHitbox, 26);
        }

        public override void OnHitPlayer(Player target, Player.HurtInfo info)
        {
            //来源是手的链:命中触发抓投(§4.0,定身 8t 后向教皇方向抛出)。
            //敌对弹幕对玩家的命中只在受击者本机结算,手的模拟在本机同样运行,位置经原生玩家同步外显。
            if (SourceHandIndex < 0 || SourceHandIndex >= Main.maxNPCs)
            {
                return;
            }
            NPC hand = Main.npc[SourceHandIndex];
            if (hand.active && hand.ModNPC is VoidPopeHand popeHand)
            {
                popeHand.TryGrab(target);
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            float t = Timer;
            //———警示拍———
            if (t < WarnTime)
            {
                float bright = t / WarnTime;
                Main.spriteBatch.UseAdditive();
                if (GlyphStyle)
                {
                    //法阵警示(§4.1 P1-6:VoidGlyph ×0.7,压扁贴地)
                    Texture2D glyph = glyphTex.Value;
                    float spin = t * 0.06f;
                    Vector2 pos = Projectile.Center - Main.screenPosition;
                    Color c = new Color(190, 100, 255) * (0.35f + 0.6f * bright);
                    Main.spriteBatch.Draw(glyph, pos, null, c, spin, glyph.Size() / 2, new Vector2(0.7f, 0.26f), SpriteEffects.None, 0);
                    Main.spriteBatch.Draw(glyph, pos, null, Color.White * (0.25f * bright), -spin * 0.7f, glyph.Size() / 2, new Vector2(0.5f, 0.19f), SpriteEffects.None, 0);
                }
                else
                {
                    //细暗紫警示线(§4.0:锁定方向 20t),贴图与全事件警示语言同款;
                    //近端半段更亮 + 基点聚能光(读作"力从这里出发")
                    Texture2D warn = CEExtraAssets.vlbw;
                    Vector2 lineOrigin = warn.Size() / 2 * new Vector2(0, 1);
                    Color c = Color.Lerp(new Color(70, 25, 120), new Color(200, 120, 255), bright) * (0.4f + 0.6f * bright);
                    Main.spriteBatch.Draw(warn, Projectile.Center - Main.screenPosition, null, c, Projectile.rotation,
                        lineOrigin, new Vector2(ChainLength / warn.Width, 0.2f + 0.3f * bright), SpriteEffects.None, 0);
                    Main.spriteBatch.Draw(warn, Projectile.Center - Main.screenPosition, null, c * 1.15f, Projectile.rotation,
                        lineOrigin, new Vector2(ChainLength * 0.45f / warn.Width, 0.14f + 0.2f * bright), SpriteEffects.None, 0);
                    Texture2D baseGlow = glowTex.Value;
                    Main.spriteBatch.Draw(baseGlow, Projectile.Center - Main.screenPosition, null,
                        new Color(200, 130, 255) * (0.8f * bright), 0, baseGlow.Size() / 2, 0.45f + 0.5f * bright, SpriteEffects.None, 0);
                }
                CEUtils.ReSetToEndShader();
                return false;
            }

            float ext = ExtendProgress;
            if (ext <= 0.02f)
            {
                return false;
            }
            Vector2 dir = Projectile.rotation.ToRotationVector2();
            float fullExtT = WarnTime + ExtendTime;

            //缚身链崩断段(§4.3 P3-6):着色器噪声侵蚀 + 渐隐(链屑坠落由 AI 拍点的粒子承担)
            float breakP = 0f;
            if (BindStyle)
            {
                float bt = Timer - (fullExtT + HoldDur);
                if (bt > 0)
                {
                    breakP = MathHelper.Clamp(bt / BreakTime, 0f, 1f);
                }
            }
            float bodyAlpha = 1f - breakP * breakP;
            if (bodyAlpha <= 0.03f)
            {
                return false;
            }

            //视觉长度与绷弦余波(判定用的 ExtendProgress/TipPos 不动):
            //缚身链钉入后弹簧回稳(长度过冲 5% 衰减振荡),到位后余波幅度指数衰减
            float extVis = ext;
            float wave = 0f;
            if (t >= fullExtT)
            {
                float since = t - fullExtT;
                if (BindStyle)
                {
                    extVis = ext * (1f + 0.05f * (float)(Math.Exp(-since * 0.3f) * Math.Cos(since * 1.1f)));
                    wave = 0.4f * (float)Math.Exp(-since * 0.18f) + 0.05f;
                }
                else
                {
                    wave = 0.36f * (float)Math.Exp(-since * 0.3f);
                }
            }
            //收回段:链体渐暗(力已卸掉)
            if (!BindStyle && t >= fullExtT + HoldTime)
            {
                bodyAlpha *= 0.8f;
            }
            //张力高光:伸出期随链头冲到顶,定格期驻留亮,收回/缚身期缓慢巡走
            float highlight;
            if (t < fullExtT)
            {
                highlight = ext;
            }
            else if (BindStyle)
            {
                highlight = 0.5f + 0.5f * (float)Math.Sin(t * 0.08f + Projectile.identity);
            }
            else
            {
                highlight = 1f - (t - fullExtT) * 0.045f;
            }

            Vector2 tipVis = Projectile.Center + dir * ChainLength * extVis;

            //———链体:PopeChainLink 着色器(链环序列 + 受力高光 + 绷弦余波 + 崩断侵蚀)———
            float quadLen = ChainLength * 1.1f;
            SpriteBatch sb = Main.spriteBatch;
            Effect chainFx = CEFxcEffects.Get("PopeChainLink");
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.NonPremultiplied, SamplerState.LinearWrap,
                DepthStencilState.None, RasterizerState.CullNone, chainFx, Main.GameViewMatrix.TransformationMatrix);
            chainFx.Parameters["uTime"]?.SetValue(Main.GlobalTimeWrappedHourly + Projectile.identity * 1.31f);
            chainFx.Parameters["uOpacity"]?.SetValue(bodyAlpha);
            chainFx.Parameters["uLinks"]?.SetValue(quadLen / 26f);
            chainFx.Parameters["uExtend"]?.SetValue(ChainLength * extVis / quadLen);
            chainFx.Parameters["uWaveAmp"]?.SetValue(wave);
            chainFx.Parameters["uHighlight"]?.SetValue(highlight);
            chainFx.Parameters["uBreak"]?.SetValue(breakP);
            chainFx.Parameters["uColorDark"]?.SetValue(new Color(40, 20, 66).ToVector3());
            chainFx.Parameters["uColorLit"]?.SetValue(new Color(150, 84, 220).ToVector3());
            chainFx.Parameters["uColorHot"]?.SetValue(new Color(232, 190, 255).ToVector3());
            Texture2D noise = CEExtraAssets.TurbulentNoise;
            sb.Draw(noise, Projectile.Center - Main.screenPosition, null, Color.White, Projectile.rotation,
                new Vector2(0f, noise.Height / 2f), new Vector2(quadLen / noise.Width, 30f / noise.Height), SpriteEffects.None, 0);
            CEUtils.ReSetToEndShader();

            sb.UseAdditive();
            //———伸出撕裂帧:前 2~3 帧全长白线(空气被撕开的一瞬)———
            if (t >= WarnTime && t - WarnTime < 3f)
            {
                float tear = 1f - (t - WarnTime) / 3f;
                Texture2D tearTex = CEExtraAssets.vlbw;
                sb.Draw(tearTex, Projectile.Center - Main.screenPosition, null, Color.White * (0.85f * tear), Projectile.rotation,
                    tearTex.Size() / 2 * new Vector2(0, 1), new Vector2(ChainLength / tearTex.Width, 0.5f * tear + 0.1f), SpriteEffects.None, 0);
            }

            //———链头恶念之爪:伸出期旋开蓄势,到位一帧咬合归位(6 次幂晚咬)+ 咬合白闪———
            float jawP = MathHelper.Clamp((t - WarnTime) / ExtendTime, 0f, 1f);
            float jaw = 0.45f * (1f - (float)Math.Pow(jawP, 6));
            float clawRot = Projectile.rotation - jaw + breakP * 1.1f;
            Vector2 clawPos = tipVis + new Vector2(0f, breakP * breakP * 60f);
            Vector2 clawOrigin = Vector2.Zero;
            Texture2D claw = Terraria.GameContent.TextureAssets.Projectile[Projectile.type].Value;
            clawOrigin = new Vector2(claw.Width * 0.35f, claw.Height * 0.5f);
            CEUtils.ReSetToEndShader();
            sb.Draw(claw, clawPos - Main.screenPosition, null, Color.White * bodyAlpha, clawRot,
                clawOrigin, 0.8f, SpriteEffects.FlipHorizontally, 0);

            //———链头辉光 + 咬合白闪(到位后 3 帧)———
            sb.UseAdditive();
            Texture2D glow = glowTex.Value;
            sb.Draw(glow, clawPos - Main.screenPosition, null, new Color(190, 100, 255) * (0.75f * bodyAlpha), 0, glow.Size() / 2, 0.8f, SpriteEffects.None, 0);
            if (t >= fullExtT && t - fullExtT < 3f)
            {
                float snap = 1f - (t - fullExtT) / 3f;
                sb.Draw(claw, clawPos - Main.screenPosition, null, Color.White * (0.9f * snap), clawRot,
                    clawOrigin, 0.84f, SpriteEffects.FlipHorizontally, 0);
            }
            CEUtils.ReSetToEndShader();
            return false;
        }
    }
}
