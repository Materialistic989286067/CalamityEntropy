using CalamityEntropy.Assets.Register;
using CalamityEntropy.Common;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using InnoVault;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using Terraria;
using Terraria.Audio;
using Terraria.Graphics.Effects;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Projectiles.VoidInvasion
{
    /// <summary>
    /// 死亡爆弹(void-invasion.md §4.1 P1-2 / §4.2 P2-2s、P2-2ss,充能/投掷/爆炸一体;P3-5 复用):
    /// 充能吸附教皇掌间(球体 0→90px,期间放射激光轮)→ 骤缩 40px 静默拍 10t(粒子骤停)
    /// → 掷向目标 12px/t 飞 90t(边缘警示圈渐亮)→ 爆炸膨胀 60t。
    /// 球体视觉 = CalamityEntropy:HellBall 着色器 + 辉光叠层;演出二迭加:PopeGravityWell 引力扭曲层
    /// (内旋涡丝/压缩环纹/中心压暗)、充能双运动族吸入光流(75% 硬切进静默前奏)、
    /// 骤缩拍世界一暗(黑纱软光罩)+ 视觉颤缩、爆炸分层(白核冲击帧 → 双色环 → 重力碎屑 → 残辉渐灭)。
    /// ai[0] = 所属教皇 whoAmI;ai[1] = 年龄计数(生成时 0,双端各自推进,状态全由它推导);
    /// ai[2] = 模式(节拍/挂点/瞄准全由它推导,双端一致):
    ///   0 = P1-2 单弹:充能 150t,半径 520,激光轮拍 35/85/135,瞄准玩家当前位;
    ///   1/2/3 = P2-2s 三重:充能 120t,半径 430,掷出 hold 依次 +30t(30t 间隔依次掷出),
    ///     瞄准依次 = 当前位 / 预判位(速度 ×45t 外推)/ 身后位;仅模式 1 放激光轮(拍 30/65/100);
    ///   4/5 = P2-2ss 灯弹复合:充能 63t(与 80t 收魂尾对齐),半径 430,hold 0/+15,
    ///     瞄准 = 当前位 / 预判位;无激光轮。
    ///   6/7 = P3-5 双爆弹终曲(§4.3,M8 巨型模式):球体 0→110px,充能 140t,吸附双巨手位(左/右),
    ///     各放 3 轮 6 向激光(拍 30/70/110 与 50/90/130 交替,右手基准角错 30°)→ 不投掷,
    ///     骤缩后原地同时引爆(半径 430)→ 爆炸帧各撒 14 枚缓速追踪魔焰弹(MagicEyeBolt 模式 2)。
    /// 弹幕伤害 = 爆炸档;激光伤害按比例另算(生成于服务端)。
    /// </summary>
    public class DeathBomb : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Assets/Extra/Ports/Invisible";

        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light")]
        private static Asset<Texture2D> glowTex;

        public const int ChargeTime = 150;
        public const int ShrinkTime = 10;
        public const int FlyTime = 90;
        public const int ExplodeExpand = 60;
        public const int FadeTime = 20;
        /// <summary>P1 档掷出拍(教皇 P1-2 的节拍常量引用它;各模式实际掷出拍见 <see cref="ThrowAt"/>)</summary>
        public const int ThrowTick = ChargeTime + ShrinkTime;          //160
        public const int BoomTick = ThrowTick + FlyTime;               //250
        public const int TotalLife = BoomTick + ExplodeExpand + FadeTime; //330
        public const float MaxChargeRadius = 90f;
        public const float ShrunkRadius = 40f;
        public const float BlastRadius = 520f;
        public const float ThrowSpeed = 12f;
        /// <summary>P2 档充能时长(三重爆弹)</summary>
        public const int ChargeTimeP2 = 120;
        /// <summary>复合招充能时长(收魂 80t 尾对齐:56 生成 + 63 充能 + 10 骤缩 = 129 收魂末)</summary>
        public const int ChargeTimeCombo = 63;
        /// <summary>P2 档爆炸半径(§4.2:数量换半径)</summary>
        public const float BlastRadiusP2 = 430f;
        /// <summary>P3 终曲档充能时长(§4.3 P3-5)</summary>
        public const int ChargeTimeP3 = 140;
        /// <summary>P3 终曲档球体半径(110px 巨型)</summary>
        public const float MaxChargeRadiusP3 = 110f;

        public int OwnerIndex => (int)Projectile.ai[0];
        public float Age => Projectile.ai[1];
        public int Mode => (int)Projectile.ai[2];

        /// <summary>巨型模式(P3-5:不投掷,原地引爆撒追踪弹)。</summary>
        public bool GiantMode => Mode >= 6;
        /// <summary>本模式充能时长。</summary>
        public int ChargeDur => Mode switch { >= 6 => ChargeTimeP3, >= 4 => ChargeTimeCombo, >= 1 => ChargeTimeP2, _ => ChargeTime };
        /// <summary>本模式掷出 hold(骤缩后至掷出的等待,依次错拍)。</summary>
        public int ThrowHold => Mode switch { >= 6 => 0, >= 4 => (Mode - 4) * 15, >= 1 => (Mode - 1) * 30, _ => 0 };
        /// <summary>本模式掷出拍(巨型模式无投掷,该拍即引爆拍)。</summary>
        public int ThrowAt => ChargeDur + ShrinkTime + ThrowHold;
        /// <summary>本模式爆炸拍。</summary>
        public int BoomAt => ThrowAt + (GiantMode ? 0 : FlyTime);
        /// <summary>本模式总寿命。</summary>
        public int TotalDur => BoomAt + ExplodeExpand + FadeTime;
        /// <summary>本模式爆炸半径。</summary>
        public float BlastR => Mode >= 1 ? BlastRadiusP2 : BlastRadius;
        /// <summary>本模式充能满半径。</summary>
        public float MaxR => GiantMode ? MaxChargeRadiusP3 : MaxChargeRadius;
        /// <summary>本模式骤缩半径。</summary>
        public float ShrunkR => GiantMode ? 55f : ShrunkRadius;

        /// <summary>本模式吸附挂点(相对教皇中心;P2 三重 = 上/左/右,复合 = 左上/右上,P3 终曲 = 双巨手位)。</summary>
        public Vector2 AnchorOffset => Mode switch
        {
            1 => new Vector2(0f, -150f),
            2 => new Vector2(-176f, -76f),
            3 => new Vector2(176f, -76f),
            4 => new Vector2(-96f, -128f),
            5 => new Vector2(96f, -128f),
            6 => new Vector2(-262f, -96f),
            7 => new Vector2(262f, -96f),
            _ => new Vector2(0f, -118f),
        };

        //激光轮基准角(仅服务端生成激光时用,不需同步)
        private float laserBaseRot = -1f;

        public override void SetStaticDefaults()
        {
            ProjectileID.Sets.DrawScreenCheckFluff[Projectile.type] = 1200;
        }

        public override void SetDefaults()
        {
            Projectile.width = 64;
            Projectile.height = 64;
            Projectile.friendly = false;
            Projectile.hostile = true;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            //寿命给足所有模式(最长 = 模式 3:120+10+60+90+60+20 = 360),生死由 age >= TotalDur 主动收
            Projectile.timeLeft = 420;
        }

        /// <summary>当前球体视觉半径(双端由年龄确定性推导)。</summary>
        public float BallRadius
        {
            get
            {
                float age = Age;
                if (age < ChargeDur)
                {
                    //充能:慢起快涨(不知不觉到满,末段醒目)
                    return MaxR * (float)Math.Pow(age / ChargeDur, 1.35);
                }
                if (age < ChargeDur + ShrinkTime)
                {
                    //静默拍:骤缩(爆发前先变小)
                    return MathHelper.Lerp(MaxR, ShrunkR, (age - ChargeDur) / ShrinkTime);
                }
                if (age < BoomAt)
                {
                    //掷出等待与飞行:小幅脉动
                    return ShrunkR + (float)Math.Sin(age * 0.5f) * 3f;
                }
                //爆炸:快始缓收的膨胀
                float p = MathHelper.Clamp((age - BoomAt) / ExplodeExpand, 0f, 1f);
                return BlastR * (1f - (1f - p) * (1f - p));
            }
        }

        /// <summary>各模式的掷出瞄准点(§4.2 P2-2s:当前位 / 预判位 / 身后位;身后 = 速度反向 230px)。</summary>
        private Vector2 AimPoint(Player target)
        {
            switch (Mode)
            {
                case 2:
                case 5:
                    return target.Center + target.velocity * 45f;
                case 3:
                    Vector2 back = target.velocity.Length() > 1f
                        ? -target.velocity.SafeNormalize(Vector2.UnitX)
                        : new Vector2(-target.direction, 0f);
                    return target.Center + back * 230f;
                default:
                    return target.Center;
            }
        }

        public override void AI()
        {
            NPC owner = OwnerIndex >= 0 && OwnerIndex < Main.maxNPCs ? Main.npc[OwnerIndex] : null;
            float age = Age;
            Projectile.ai[1]++;
            int chargeDur = ChargeDur;
            int throwAt = ThrowAt;
            int boomAt = BoomAt;

            //充能、静默与掷出等待期吸附教皇掌间;教皇没了就停在原位走完流程不再吸附
            if (age < throwAt)
            {
                if (owner != null && owner.active && owner.ModNPC is NPCs.VoidInvasion.VoidPope)
                {
                    Projectile.Center = owner.Center + AnchorOffset;
                }
                Projectile.velocity = Vector2.Zero;
            }

            //———充能段———
            if (age < chargeDur)
            {
                float charge = age / (float)chargeDur;
                //三轮 6 向放射激光(P1 轮拍 35/85/135;P2 三重仅模式 1 放,轮拍 30/65/100;
                //P3 终曲双弹交替:左手 30/70/110,右手 50/90/130,基准角互错 30°;轮间整体旋 30°)
                bool laserBeat = Mode switch
                {
                    0 => age == 35 || age == 85 || age == 135,
                    1 => age == 30 || age == 65 || age == 100,
                    6 => age == 30 || age == 70 || age == 110,
                    7 => age == 50 || age == 90 || age == 130,
                    _ => false,
                };
                if (Main.netMode != NetmodeID.MultiplayerClient && laserBeat)
                {
                    if (laserBaseRot < 0)
                    {
                        //巨型模式基准角固定(两手互错 30°,§4.3 P3-5:共 12 线可读);其余模式随机
                        laserBaseRot = GiantMode
                            ? (Mode == 7 ? MathHelper.ToRadians(30) : 0f)
                            : Main.rand.NextFloat(MathHelper.TwoPi);
                    }
                    int round = age < chargeDur * 0.4f ? 0 : (age < chargeDur * 0.73f ? 1 : 2);
                    int laserDamage = (int)(Projectile.damage * (180f / 260f) + 0.5f);
                    for (int i = 0; i < 6; i++)
                    {
                        Vector2 dir = (laserBaseRot + round * MathHelper.ToRadians(30) + i * MathHelper.TwoPi / 6f).ToRotationVector2();
                        Projectile.NewProjectile(Projectile.GetSource_FromAI(), Projectile.Center, dir * 0.02f,
                            ModContent.ProjectileType<DeathBombLaser>(), laserDamage, 2f, -1);
                    }
                }
                if (!Main.dedServ)
                {
                    //吸入光流(MOTION §6 双运动族):密度随充能爬升,~75% 硬切——最后一段安静是尖叫前的吸气
                    if (charge < 0.75f)
                    {
                        //径向内聚族:拉长的光streak 被拽向弹心
                        if (Main.rand.NextFloat() < 0.3f + charge * 0.9f)
                        {
                            Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(130f, 380f);
                            var p = PRTLoader.NewParticle<PRT_Light>(Projectile.Center + offset, -offset * 0.075f,
                                new Color(170, 70, 255), 0.4f + charge * 0.4f);
                            p.Configure(0.9f, squishStrenght: 3.2f, maxSquish: 4.5f, lifetime: 15);
                        }
                        //切向环绕族:速度旋转 90°,汇聚带上旋涡而不只是抽吸
                        if (Main.rand.NextFloat() < 0.18f + charge * 0.5f)
                        {
                            Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(60f, 200f);
                            Vector2 tangent = new Vector2(-offset.Y, offset.X).SafeNormalize(Vector2.UnitX) * (2.5f + charge * 3f);
                            var p = PRTLoader.NewParticle<PRT_Light>(Projectile.Center + offset, tangent - offset * 0.02f,
                                new Color(120, 55, 230), 0.35f);
                            p.Configure(0.8f, squishStrenght: 2.2f, lifetime: 18);
                        }
                    }
                    else if (Main.rand.NextBool(9))
                    {
                        //静默前奏:只剩核面偶发微闪
                        var p = PRTLoader.NewParticle<PRT_Light>(Projectile.Center + CEUtils.randomPointInCircle(MaxR * 0.5f),
                            Vector2.Zero, new Color(220, 170, 255), 0.3f);
                        p.Configure(0.9f, lifetime: 9);
                    }
                    //音调爬升(75% 后停,让静默拍有落差)
                    if ((int)age % 25 == 12 && charge < 0.78f)
                    {
                        SoundEngine.PlaySound(SoundID.Item15 with { Volume = 0.55f + charge * 0.4f, Pitch = -0.6f + charge * 0.9f }, Projectile.Center);
                    }
                }
                return;
            }

            //———静默拍(§4.1:10t 粒子骤停)与掷出等待(P2 依次错拍)———
            if (age < throwAt)
            {
                if (age == chargeDur && !Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.Item78 with { Volume = 0.9f, Pitch = -0.7f }, Projectile.Center);
                }
                return;
            }

            //———投掷拍:服务端定向一帧点火(巨型模式不投掷,直接落进爆炸拍)———
            if (age == throwAt && !GiantMode)
            {
                if (Main.netMode != NetmodeID.MultiplayerClient)
                {
                    Player target = owner != null && owner.active && owner.HasValidTarget
                        ? Main.player[owner.target]
                        : Main.player[Player.FindClosest(Projectile.Center, 1, 1)];
                    Projectile.velocity = (AimPoint(target) - Projectile.Center).SafeNormalize(Vector2.UnitY) * ThrowSpeed;
                    Projectile.netUpdate = true;
                }
                if (!Main.dedServ)
                {
                    SoundEngine.PlaySound(SoundID.DD2_BetsysWrathShot with { Volume = 1.1f, Pitch = -0.5f }, Projectile.Center);
                    //掷出帧:沿掷向的方向性冲环 + 微震(黑洞出手要有后座感)
                    var ring = PRTLoader.NewParticle<PRT_DirectionalPulseRing>(Projectile.Center, Vector2.Zero, new Color(190, 100, 255), 0.1f);
                    ring.Configure(new Vector2(2.2f, 0.85f), Projectile.velocity.ToRotation(), 2.2f, 18);
                    ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.ScreenShake(Vector2.Zero, 4), Main.LocalPlayer.Distance(Projectile.Center), 1600);
                }
                return;
            }

            //———飞行段———
            if (age < boomAt)
            {
                Lighting.AddLight(Projectile.Center, 0.9f, 0.3f, 1.2f);
                if (!Main.dedServ && Main.rand.NextBool(2))
                {
                    var p = PRTLoader.NewParticle<PRT_Light>(Projectile.Center + CEUtils.randomPointInCircle(20f), -Projectile.velocity * 0.15f, new Color(150, 60, 240), 0.5f);
                    p.Configure(0.85f, lifetime: 14);
                }
                return;
            }

            //———爆炸拍(分层:白核冲击帧 → 色环 → 碎屑 → 残辉渐灭;白核与残辉在 PreDraw 按龄推导)———
            Projectile.velocity = Vector2.Zero;
            if (age == boomAt && !Main.dedServ)
            {
                if (Main.LocalPlayer.Distance(Projectile.Center) < 2200f)
                {
                    CalamityEntropy.FlashEffectStrength = 0.5f;
                }
                SoundEngine.PlaySound(SoundID.Item14 with { Volume = 1.2f, Pitch = -0.4f }, Projectile.Center);
                SoundEngine.PlaySound(SoundID.DD2_KoboldExplosion with { Volume = 1f, Pitch = -0.5f }, Projectile.Center);
                ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.ScreenShake(Vector2.Zero, 13), Main.LocalPlayer.Distance(Projectile.Center), 2400);
                PRTLoader.NewParticle<PRT_PulseRing>(Projectile.Center, Vector2.Zero, new Color(190, 100, 255), 0.1f).Configure(8f, 46);
                PRTLoader.NewParticle<PRT_PulseRing>(Projectile.Center, Vector2.Zero, Color.White, 0.1f).Configure(5.5f, 36);
                for (int i = 0; i < 70; i++)
                {
                    var v = PRTLoader.NewParticle<PRT_Void>(Projectile.Center, CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(2f, 15f), Color.White, 1f);
                    v.Opacity = Main.rand.Next(30, 100) * 0.01f;
                }
                //碎屑层:带重力的拉长火花 + 硬直线屑(慢的活得久,快的死得快)
                for (int i = 0; i < 22; i++)
                {
                    float spd = Main.rand.NextFloat(5f, 17f);
                    var s = PRTLoader.NewParticle<PRT_GlowSparkCal>(Projectile.Center,
                        CEUtils.randomRot().ToRotationVector2() * spd, new Color(200, 120, 255), Main.rand.NextFloat(0.55f, 1.05f));
                    s.Configure(true, (int)MathHelper.Lerp(46f, 20f, spd / 17f), new Vector2(2.1f, 0.55f), quickShrink: true);
                }
                for (int i = 0; i < 14; i++)
                {
                    var d = PRTLoader.NewParticle<PRT_LineCal>(Projectile.Center,
                        CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(4f, 12f), new Color(150, 84, 220), 0.9f);
                    d.Configure(true, 30);
                }
            }
            //第二道色环(延迟 7t,爆发的回声拍)
            if (age == boomAt + 7 && !Main.dedServ)
            {
                PRTLoader.NewParticle<PRT_PulseRing>(Projectile.Center, Vector2.Zero, new Color(140, 60, 235), 0.1f).Configure(10f, 40);
            }
            //巨型模式爆炸帧:各撒 14 枚缓速追踪魔焰弹(§4.3 P3-5:8px/t、转向 0.03 上限、6s 命;两弹相位互错半步)
            if (age == boomAt && GiantMode && Main.netMode != NetmodeID.MultiplayerClient)
            {
                int boltDamage = (int)(Projectile.damage * (170f / 260f) + 0.5f);
                float phaseOffset = Mode == 7 ? MathHelper.TwoPi / 28f : 0f;
                for (int i = 0; i < 14; i++)
                {
                    Vector2 dir = (phaseOffset + i * MathHelper.TwoPi / 14f).ToRotationVector2();
                    Projectile.NewProjectile(Projectile.GetSource_FromAI(), Projectile.Center, dir * 8f,
                        ModContent.ProjectileType<MagicEyeBolt>(), boltDamage, 3f, -1, 2f);
                }
            }
            if (age >= TotalDur)
            {
                Projectile.Kill();
            }
        }

        public override bool CanHitPlayer(Player target)
        {
            //充能与静默期无判定(公平阀:掷出前只有激光在压场)
            return Age >= ThrowAt;
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            float age = Age;
            if (age < ThrowAt)
            {
                return false;
            }
            if (age < BoomAt)
            {
                //飞行接触:按球半径的圆判定
                return targetHitbox.Intersects(Utils.CenteredRectangle(Projectile.Center, new Vector2(ShrunkR * 1.6f)));
            }
            //爆炸:命中随膨胀半径走;膨胀完成后进入渐隐,判定关闭(打击窗与可见爆发对齐)
            if (age > BoomAt + ExplodeExpand)
            {
                return false;
            }
            return Vector2.Distance(targetHitbox.Center.ToVector2(), Projectile.Center) < BallRadius;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            float age = Age;
            float radius = BallRadius;
            if (radius < 3f)
            {
                return false;
            }
            SpriteBatch sb = Main.spriteBatch;
            int throwAt = ThrowAt;
            int boomAt = BoomAt;
            int chargeDur = ChargeDur;

            //爆炸尾段渐隐
            float opacity = 1f;
            if (age > boomAt + ExplodeExpand)
            {
                opacity = 1f - MathHelper.Clamp((age - boomAt - ExplodeExpand) / FadeTime, 0f, 1f);
            }

            //骤缩静默拍:球体视觉半径叠余弦颤缩(判定半径不动),"东西先变小再变响"
            float visualR = radius;
            if (age >= chargeDur && age < chargeDur + ShrinkTime)
            {
                visualR *= 0.94f + 0.06f * (float)Math.Cos(age * 2.1f);
            }

            //———静默拍"世界一暗":骤缩起周边亮度瞬降(黑纱软光罩,AlphaBlend 直接画)———
            if (age >= chargeDur && age < throwAt + 22 && opacity > 0.5f)
            {
                float veilIn = MathHelper.Clamp((age - chargeDur) / (float)ShrinkTime, 0f, 1f);
                float veilOut = age > throwAt ? MathHelper.Clamp(1f - (age - throwAt) / 22f, 0f, 1f) : 1f;
                float veil = veilIn * veilOut * 0.55f;
                if (veil > 0.02f)
                {
                    Texture2D soft = CEExtraAssets.Glow;
                    sb.Draw(soft, Projectile.Center - Main.screenPosition, null, Color.Black * veil, 0,
                        soft.Size() / 2, 900f / soft.Width * 2f, SpriteEffects.None, 0);
                }
            }

            //———引力扭曲层(伪透镜):充能中段起,内旋涡丝 + 向心压缩环纹 + 中心压暗———
            float gravity = 0f;
            if (age < chargeDur)
            {
                gravity = MathHelper.Clamp((age / (float)chargeDur - 0.15f) / 0.85f, 0f, 1f);
            }
            else if (age < boomAt)
            {
                gravity = 0.85f;
            }
            if (gravity > 0.03f)
            {
                Effect well = CEEffectAssets.PopeGravityWell;
                sb.End();
                sb.Begin(SpriteSortMode.Immediate, BlendState.NonPremultiplied, SamplerState.LinearWrap,
                    DepthStencilState.None, RasterizerState.CullNone, well, Main.GameViewMatrix.TransformationMatrix);
                well.Parameters["uTime"]?.SetValue(Main.GlobalTimeWrappedHourly + Projectile.identity * 2.13f);
                well.Parameters["uOpacity"]?.SetValue(opacity);
                well.Parameters["uStrength"]?.SetValue(gravity);
                Texture2D wellNoise = CEExtraAssets.TurbulentNoise;
                float wellSize = Math.Max(visualR * 5.2f, 230f);
                sb.Draw(wellNoise, Projectile.Center - Main.screenPosition, null, Color.White, 0,
                    wellNoise.Size() / 2, wellSize / wellNoise.Width, SpriteEffects.None, 0);
                CEUtils.ReSetToEndShader();
            }

            //———飞行段警示圈(§4.1:爆前边缘有警示圈,BloomRing 撑到爆炸半径)———
            if (age >= throwAt && age < boomAt)
            {
                float warnP = (age - throwAt) / (float)FlyTime;
                Texture2D ring = CEExtraAssets.BloomRing;
                sb.UseAdditive();
                float pulse = 1f + 0.03f * (float)Math.Sin(age * 0.35f);
                sb.Draw(ring, Projectile.Center - Main.screenPosition, null, new Color(190, 90, 255) * (0.16f + 0.3f * warnP),
                    0, ring.Size() / 2, BlastR * 2f / ring.Width * pulse, SpriteEffects.None, 0);
                CEUtils.ReSetToEndShader();
            }
            //———巨型模式警示圈(§4.3 P3-5:不投掷,充能尾段起在原地亮出爆炸半径)———
            if (GiantMode && age >= ChargeDur - 30 && age < boomAt)
            {
                float warnP = MathHelper.Clamp((age - (ChargeDur - 30)) / 40f, 0f, 1f);
                Texture2D ring = CEExtraAssets.BloomRing;
                sb.UseAdditive();
                float pulse = 1f + 0.03f * (float)Math.Sin(age * 0.35f);
                sb.Draw(ring, Projectile.Center - Main.screenPosition, null, new Color(190, 90, 255) * (0.14f + 0.34f * warnP),
                    0, ring.Size() / 2, BlastR * 2f / ring.Width * pulse, SpriteEffects.None, 0);
                CEUtils.ReSetToEndShader();
            }

            //———HellBall 球体(镜像 BookmarkPactOfDecay 的调用姿势:裸 Effect 挂 Immediate,逐参数 SetValue)———
            Effect shieldEffect = Filters.Scene["CalamityEntropy:HellBall"].GetShader().Shader;
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, shieldEffect, Main.GameViewMatrix.TransformationMatrix);
            shieldEffect.Parameters["time"].SetValue(Main.GameUpdateCount / 60f * 0.85f);
            shieldEffect.Parameters["blowUpPower"].SetValue(3.2f);
            shieldEffect.Parameters["blowUpSize"].SetValue(0.4f);
            shieldEffect.Parameters["noiseScale"].SetValue(0.7f);
            shieldEffect.Parameters["shieldOpacity"].SetValue(opacity);
            shieldEffect.Parameters["shieldEdgeBlendStrenght"].SetValue(4f);
            shieldEffect.Parameters["shieldColor"].SetValue((new Color(150, 40, 235) * opacity).ToVector3());
            shieldEffect.Parameters["shieldEdgeColor"].SetValue((new Color(35, 8, 70) * opacity).ToVector3());
            Texture2D noise = CEExtraAssets.TurbulentNoise;
            sb.Draw(noise, Projectile.Center - Main.screenPosition, null, Color.White * opacity, 0, noise.Size() / 2,
                visualR * 2f / noise.Width, SpriteEffects.None, 0);
            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, Main.DefaultSamplerState, DepthStencilState.None, Main.Rasterizer, null, Main.GameViewMatrix.TransformationMatrix);

            //———辉光叠层 + 白核冲击帧———
            sb.UseAdditive();
            Texture2D glow = glowTex.Value;
            sb.Draw(glow, Projectile.Center - Main.screenPosition, null, new Color(190, 90, 255) * (0.7f * opacity), 0, glow.Size() / 2,
                visualR / glow.Width * 4.2f, SpriteEffects.None, 0);
            sb.Draw(glow, Projectile.Center - Main.screenPosition, null, Color.White * (0.35f * opacity), 0, glow.Size() / 2,
                visualR / glow.Width * 2.2f, SpriteEffects.None, 0);
            //白核冲击帧(爆炸头 3 帧,纯白硬核先于色环撑出来)
            if (age >= boomAt && age - boomAt < 3f)
            {
                float core = 1f - (age - boomAt) / 3f;
                sb.Draw(glow, Projectile.Center - Main.screenPosition, null, Color.White * (0.95f * core), 0, glow.Size() / 2,
                    BlastR * 1.1f / glow.Width, SpriteEffects.None, 0);
            }
            CEUtils.ReSetToEndShader();
            return false;
        }
    }

    /// <summary>
    /// 死亡爆弹的放射激光(§4.1 P1-2):基点与方向生成时锁定(方向借初速度通道),
    /// 15t 细警示线 → 25t 直线光束(判定活跃)→ 10t 渐灭。
    /// 深渊亡魂的 AbyssalLaser 是绑定本体的弯曲追踪激光,不适合定角放射,故独立新建直线件。
    /// </summary>
    public class DeathBombLaser : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Assets/Extra/Ports/Invisible";

        public const int WarnTime = 15;
        public const int BeamTime = 25;
        public const int FadeTime = 10;
        public const float BeamLength = 2400f;

        private float Timer => WarnTime + BeamTime + FadeTime - Projectile.timeLeft;

        public override void SetStaticDefaults()
        {
            ProjectileID.Sets.DrawScreenCheckFluff[Projectile.type] = 3000;
        }

        public override void SetDefaults()
        {
            Projectile.width = 20;
            Projectile.height = 20;
            Projectile.friendly = false;
            Projectile.hostile = true;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.timeLeft = WarnTime + BeamTime + FadeTime;
        }

        public override bool ShouldUpdatePosition() => false;

        public override void AI()
        {
            if (Projectile.localAI[0] == 0)
            {
                Projectile.localAI[0] = 1;
                Projectile.rotation = Projectile.velocity.ToRotation();
                Projectile.velocity = Vector2.Zero;
            }
            if (Timer == WarnTime && !Main.dedServ)
            {
                SoundEngine.PlaySound(new SoundStyle("CalamityEntropy/Assets/Sounds/light_bolt") { Volume = 0.7f, Pitch = -0.15f }, Projectile.Center);
            }
            if (Timer >= WarnTime)
            {
                Lighting.AddLight(Projectile.Center + Projectile.rotation.ToRotationVector2() * 400f, 0.5f, 0.2f, 0.8f);
            }
        }

        public override bool CanHitPlayer(Player target)
        {
            return Timer >= WarnTime && Timer < WarnTime + BeamTime;
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            if (Timer < WarnTime || Timer >= WarnTime + BeamTime)
            {
                return false;
            }
            return CEUtils.LineThroughRect(Projectile.Center, Projectile.Center + Projectile.rotation.ToRotationVector2() * BeamLength, targetHitbox, 20);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            float t = Timer;
            Texture2D warn = CEExtraAssets.vlbw;
            Vector2 pos = Projectile.Center - Main.screenPosition;
            if (t < WarnTime)
            {
                //细警示线(§4.1:每轮先 15t)
                Main.spriteBatch.UseAdditive();
                float bright = t / WarnTime;
                Color c = Color.Lerp(new Color(70, 25, 120), new Color(200, 120, 255), bright) * (0.35f + 0.65f * bright);
                Main.spriteBatch.Draw(warn, pos, null, c, Projectile.rotation,
                    warn.Size() / 2 * new Vector2(0, 1), new Vector2(BeamLength / warn.Width, 0.22f + 0.3f * bright), SpriteEffects.None, 0);
                CEUtils.ReSetToEndShader();
                return false;
            }
            //光束:PopeBeam 束体(白核 + 紫晕 + 热浪扰动),渐灭段收窄;首帧出膛闪
            float fade = t >= WarnTime + BeamTime ? 1f - (t - WarnTime - BeamTime) / FadeTime : 1f;
            float grow = MathHelper.Clamp((t - WarnTime) / 4f, 0f, 1f);
            PopeVfx.DrawBeam(Projectile.Center, Projectile.rotation, BeamLength, 36f, fade, grow,
                new Color(165, 75, 255), new Color(80, 28, 150), coreFrac: 0.3f, fringe: 0.45f,
                flicker: 0.35f, flickerSeed: Projectile.identity * 0.77f);
            Main.spriteBatch.UseAdditive();
            PopeVfx.DrawBeamCap(Main.spriteBatch, Projectile.Center, (0.5f + 0.12f * (float)Math.Sin(t * 0.7f)) * fade, fade, new Color(200, 120, 255));
            if (t - WarnTime < 2.5f)
            {
                //出膛冲击帧:一瞬全长白闪
                Main.spriteBatch.Draw(warn, pos, null, Color.White * (0.9f * (1f - (t - WarnTime) / 2.5f)), Projectile.rotation,
                    warn.Size() / 2 * new Vector2(0, 1), new Vector2(BeamLength / warn.Width, 1.6f), SpriteEffects.None, 0);
            }
            CEUtils.ReSetToEndShader();
            return false;
        }
    }
}
