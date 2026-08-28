using CalamityEntropy.Assets.Register;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using CalamityEntropy.Core.Graphics;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Projectiles.VoidInvasion
{
    /// <summary>
    /// 水晶牢笼(void-invasion.md §3.2 招式 3):单弹幕承载整个围环(实现载体裁量:比 14 个散弹幕稳,
    /// 环上位置由年龄确定性推导,天然双端一致)。
    /// 以牢笼中心(生成时定格的玩家位置,弹幕坐标原生同步)为心,16 槽位留 2 对向缺口共 14 枚水晶;
    /// 浮现 30t(无判定)→ 0.9px/t 向心收缩 300t(接触 170)→ 到时碎裂,碎片向外飞散(向内躲安全)。
    /// 缺口随整环 0.008rad/t 缓转。ai[0] = 虚熵魔物 whoAmI(死亡演出时无碎裂中断),ai[1] = 环基准角。
    /// 年龄由 timeLeft 反推,双端同步。
    /// 演出:浮现期每颗水晶由光束自天而降凝成(逐槽错拍);收缩期环内暗光渐亮 + 低鸣渐高 + 棱光渐盛。
    /// </summary>
    public class VoidCrystalCage : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Content/Projectiles/VoidInvasion/VoidCrystal";

        public const int MaterializeTime = 30;
        /// <summary>收缩时长(§3.2:5s)</summary>
        public const int ShrinkTime = 300;
        public const int TotalLife = MaterializeTime + ShrinkTime;
        /// <summary>起始半径(§3.2:420px)</summary>
        public const float StartRadius = 420f;
        /// <summary>向心收缩速度(§3.2:0.9px/t)</summary>
        public const float ShrinkPerTick = 0.9f;
        /// <summary>槽位数:16 槽 14 晶,2 个对向缺口</summary>
        public const int SlotCount = 16;
        /// <summary>缺口缓转速度(§3.2:0.008rad/t)</summary>
        public const float GapDriftPerTick = 0.008f;
        /// <summary>逐槽凝成错拍(t/槽):15 槽 × 1.2 + 12 = 30,恰好铺满浮现期</summary>
        public const float SlotStagger = 1.2f;
        /// <summary>单槽凝成时长</summary>
        public const float SlotFormTime = 12f;

        /// <summary>魔物死亡/消失时的中断收尾:不碎裂,直接淡出(公平阀)</summary>
        private bool interrupted = false;
        private float fade = 1f;

        public int Age => TotalLife - Projectile.timeLeft;
        public float Radius => StartRadius - ShrinkPerTick * Math.Max(0, Age - MaterializeTime);
        public float RingRotation => Projectile.ai[1] + Age * GapDriftPerTick;
        /// <summary>收缩进度 0~1(压迫感曲线的公共输入)</summary>
        public float ShrinkProgress => MathHelper.Clamp((Age - MaterializeTime) / (float)ShrinkTime, 0f, 1f);

        private static bool IsGap(int slot) => slot == 0 || slot == SlotCount / 2;

        /// <summary>单槽凝成进度 0~1(逐槽错拍,浮现期铺满)</summary>
        private float SlotForm(int slot) => MathHelper.Clamp((Age - slot * SlotStagger) / SlotFormTime, 0f, 1f);

        public Vector2 SlotPos(int slot)
        {
            return Projectile.Center + (RingRotation + MathHelper.TwoPi * slot / SlotCount).ToRotationVector2() * Radius;
        }

        public override void SetDefaults()
        {
            Projectile.width = 10;
            Projectile.height = 10;
            Projectile.friendly = false;
            Projectile.hostile = true;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.timeLeft = TotalLife;
        }

        public override bool ShouldUpdatePosition()
        {
            return false;
        }

        public override void AI()
        {
            if (Projectile.localAI[0] == 0)
            {
                Projectile.localAI[0] = 1;
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.Item27 with { Pitch = -0.3f }, Projectile.Center);
                }
            }
            //宿主检查:魔物没了或进死亡演出 → 中断淡出(双端各自从同步态推得同一结论)
            NPC owner = ((int)Projectile.ai[0]).ToNPC();
            if (!interrupted && (!owner.active || owner.ModNPC is not NPCs.VoidInvasion.EntropyFiend ef || ef.InDeathAnim))
            {
                interrupted = true;
            }
            if (interrupted)
            {
                fade -= 0.06f;
                if (fade <= 0)
                {
                    Projectile.Kill();
                }
                return;
            }

            if (!Main.dedServ)
            {
                //凝成落拍:光束触地瞬间每槽一记闪 + 下坠光线(与 PreDraw 的 SlotForm 曲线同源)
                for (int slot = 0; slot < SlotCount; slot++)
                {
                    if (IsGap(slot))
                        continue;
                    if (Age == (int)(slot * SlotStagger) + 3)
                    {
                        Vector2 pos = SlotPos(slot);
                        var p = PRTLoader.NewParticle<PRT_Light>(pos, Vector2.Zero, new Color(200, 170, 255), 0.7f);
                        p.Configure(0.95f, lifetime: 12);
                        PRTLoader.NewParticle<PRT_LineCal>(pos - new Vector2(0, Main.rand.NextFloat(90f, 160f)), new Vector2(0, 9f),
                            new Color(190, 160, 255), 0.9f).Configure(false, 10);
                        SoundEngine.PlaySound(SoundID.Item101 with { Volume = 0.22f, Pitch = 0.5f }, pos);
                    }
                }

                //环上零星凝晶光点 + 环内暗光,提示收缩中
                for (int i = 0; i < 2; i++)
                {
                    int slot = Main.rand.Next(SlotCount);
                    if (IsGap(slot) || SlotForm(slot) < 1f)
                        continue;
                    var p = PRTLoader.NewParticle<PRT_Light>(SlotPos(slot) + CEUtils.randomPointInCircle(10), Vector2.Zero,
                        new Color(150, 110, 255), 0.35f);
                    p.Configure(0.7f, lifetime: 14);
                }
                float shrinkP = ShrinkProgress;
                Lighting.AddLight(Projectile.Center, 0.25f + shrinkP * 0.3f, 0.1f, 0.45f + shrinkP * 0.4f);

                //收缩低鸣渐高:间隔渐短、音调渐升(确定性由 Age 推,双端一致)
                if (Age > MaterializeTime)
                {
                    int interval = Math.Max(10, 30 - (int)(shrinkP * 18f));
                    if (Age % interval == 0)
                    {
                        SoundEngine.PlaySound(SoundID.Item27 with { Volume = 0.3f + shrinkP * 0.3f, Pitch = -0.8f + shrinkP * 0.9f }, Projectile.Center);
                    }
                }
            }

            //自然到时:碎片向外飞散(服务端生成;向内躲是安全解,§3.2)
            if (Age >= TotalLife - 1)
            {
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.Item27 with { Volume = 1f, Pitch = -0.5f }, Projectile.Center);
                }
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    for (int slot = 0; slot < SlotCount; slot++)
                    {
                        if (IsGap(slot))
                            continue;
                        Vector2 pos = SlotPos(slot);
                        Vector2 dir = (pos - Projectile.Center).SafeNormalize(Vector2.UnitY);
                        Projectile.NewProjectile(Projectile.GetSource_FromAI(), pos, dir * 7f,
                            ModContent.ProjectileType<VoidCrystal>(), Projectile.damage, 0, -1, 1f);
                    }
                }
                Projectile.Kill();
            }
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            //浮现期与中断期无判定(公平阀:判定窗口与可见收缩段完全一致)
            if (interrupted || Age <= MaterializeTime)
            {
                return false;
            }
            for (int slot = 0; slot < SlotCount; slot++)
            {
                if (IsGap(slot))
                    continue;
                Vector2 pos = SlotPos(slot);
                var rect = new Rectangle((int)pos.X - 18, (int)pos.Y - 18, 36, 36);
                if (rect.Intersects(targetHitbox))
                {
                    return true;
                }
            }
            return false;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            SpriteBatch sb = Main.spriteBatch;
            Texture2D tex = Terraria.GameContent.TextureAssets.Projectile[Projectile.type].Value;
            if (fade <= 0.01f)
            {
                return false;
            }
            float shrinkP = ShrinkProgress;

            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);

            //———加法层(预乘 alpha=0 即加法,默认管线)———
            //凝成光束:自天而降,随单槽进度起落
            if (Age <= MaterializeTime + 6)
            {
                for (int slot = 0; slot < SlotCount; slot++)
                {
                    if (IsGap(slot))
                        continue;
                    float form = SlotForm(slot);
                    if (form <= 0f || form >= 1f)
                        continue;
                    float beamA = (float)Math.Sin(form * MathHelper.Pi) * fade;
                    Vector2 pos = SlotPos(slot);
                    CEUtils.drawLine(sb, CEExtraAssets.white, pos - new Vector2(0, 920), pos, new Color(150, 110, 255, 0) * (0.5f * beamA), 20 * beamA);
                    CEUtils.drawLine(sb, CEExtraAssets.white, pos - new Vector2(0, 920), pos, new Color(255, 255, 255, 0) * (0.7f * beamA), 6 * beamA);
                }
            }
            //环内压迫暗光:半径越小越亮;贴环一圈软边光随收缩渐显
            if (Age > MaterializeTime)
            {
                Texture2D lb = CEExtraAssets.lightball;
                float innerA = (0.10f + shrinkP * 0.34f) * fade;
                sb.Draw(lb, Projectile.Center - Main.screenPosition, null, new Color(110, 60, 235, 0) * innerA, 0, lb.Size() / 2, Radius * 2.3f / lb.Width, SpriteEffects.None, 0);
                Texture2D ringT = CEExtraAssets.HollowCircleSoftEdge;
                float ringA = (0.12f + shrinkP * 0.4f) * fade;
                sb.Draw(ringT, Projectile.Center - Main.screenPosition, null, new Color(160, 110, 255, 0) * ringA, RingRotation, ringT.Size() / 2, Radius * 2.1f / ringT.Width, SpriteEffects.None, 0);
            }

            //———主体:棱光着色器逐槽绘制,收缩期内辉光渐盛———
            Effect fx = CEFxcEffects.Get("FiendCrystal");
            fx.CurrentTechnique = fx.Techniques["Technique1"];
            fx.Parameters["time"].SetValue(Main.GlobalTimeWrappedHourly);
            fx.Parameters["glowColor"].SetValue(new Vector4(0.55f, 0.3f, 1.1f, 0f));
            fx.Parameters["noiseTex"].SetValue(CEExtraAssets.Perlin);
            fx.Parameters["glowPulse"].SetValue(0.25f + shrinkP * 0.75f);
            for (int slot = 0; slot < SlotCount; slot++)
            {
                if (IsGap(slot))
                    continue;
                float form = SlotForm(slot);
                if (form <= 0f)
                    continue;
                Vector2 pos = SlotPos(slot) - Main.screenPosition;
                float spin = Age * (0.15f + shrinkP * 0.1f) + slot * 1.3f;
                //凝成瞬间带一点过冲弹性,落位有"咔"的实体感
                float scale = MathHelper.Lerp(0.3f, 1f, form) * (1f + 0.22f * (float)Math.Sin(form * MathHelper.Pi));
                fx.Parameters["seed"].SetValue(slot * 0.61f + Projectile.ai[1] % 3f);
                fx.Parameters["alpha"].SetValue(form * fade);
                fx.CurrentTechnique.Passes[0].Apply();
                sb.Draw(tex, pos, null, Color.White, spin, tex.Size() / 2, scale, SpriteEffects.None, 0);
            }

            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            return false;
        }

        public override void OnKill(int timeLeft)
        {
            if (Main.dedServ)
                return;
            for (int slot = 0; slot < SlotCount; slot++)
            {
                if (IsGap(slot))
                    continue;
                var p = PRTLoader.NewParticle<PRT_Light>(SlotPos(slot), CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(1f, 3f),
                    new Color(160, 120, 255), 0.5f);
                p.Configure(0.8f, lifetime: 20);
                PRTLoader.NewParticle<PRT_CrystalGlow>(SlotPos(slot), (SlotPos(slot) - Projectile.Center).SafeNormalize(Vector2.UnitY) * Main.rand.NextFloat(2f, 5f),
                    new Color(150, 115, 255), Main.rand.NextFloat(0.3f, 0.55f)).Configure(0.85f, true, PRTDrawModeEnum.AdditiveBlend, CEUtils.randomRot(), 24);
            }
            //整环崩解的一记收束脉冲
            PRTLoader.NewParticle<PRT_PulseRing>(Projectile.Center, Vector2.Zero, new Color(170, 120, 255), 0.1f).Configure(Radius / 96f, 26);
        }
    }
}
