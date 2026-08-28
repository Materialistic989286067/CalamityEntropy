using CalamityEntropy.Assets.Register;
using CalamityEntropy.Common;
using CalamityEntropy.Content.NPCs.VoidInvasion;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using InnoVault;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using System;
using Terraria;
using Terraria.Audio;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Projectiles.VoidInvasion
{
    /// <summary>
    /// 维度魔盘(void-invasion.md §4.2 P2-5/P2-7/P2-8;维度魔盘.png 352x360):
    /// 行为完全由教皇的同步字段 <see cref="VoidPope.discState"/> 驱动,本弹幕是演出与判定载体。
    /// 状态语义:0 无盘(渐隐自毁)/1 升起(自旋加速 60t)/2 跟踪(朝玩家缓转)/3 锁定(盘心亮起+咔哒,
    /// 停止跟踪)/4 射击(激光是 <see cref="DiscBeam"/>,教皇服务端在锁定帧生成)/5 退场(缩小消失)
    /// /6 悬顶(P2-7/P2-8 起手)/7 掷出(12px/t 自旋,接触判定开)/8 减速悬停(判定保持)/9 破门
    /// (教皇自盘心现身:本弹幕凭状态跳变双端各自播爆闪,随后渐隐)。
    /// ai[0] = 教皇 whoAmI。悬浮段位置 = 教皇中心 + 确定性偏移;掷出段位置走弹幕原生同步。
    /// 盘朝向 localAI 双端同式积分,激光判定方向以 DiscBeam(服务端生成)为准。
    /// </summary>
    public class DimensionDisc : ModProjectile
    {
        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light")]
        private static Asset<Texture2D> glowTex;
        [VaultLoaden("CalamityEntropy/Content/Projectiles/VoidInvasion/VoidGlyph")]
        private static Asset<Texture2D> glyphTex;

        /// <summary>升起段时长(自旋加速)</summary>
        public const int RiseTime = 60;
        /// <summary>掷出接触判定圆半径</summary>
        public const float HitRadius = 130f;
        /// <summary>掷出速度(§4.2 P2-8)</summary>
        public const float ThrowSpeed = 12f;

        public int OwnerIndex => (int)Projectile.ai[0];

        //本地演出状态(双端各自推进)
        private float spin;          //自旋角
        private float spinSpeed;     //自旋速度(升起段 0→0.3)
        private float aimAngle;      //盘面朝向(跟踪段缓转,锁定后冻结)
        private bool aimInit;
        private byte lastState;      //状态跳变检测(咔哒/爆闪双端各自播)
        private float fade = 1f;     //渐隐(state 0/9)
        private float riseCounter;
        private float lockFlash;     //锁定拍盘面瞬亮(帧数余量,衰减)
        private float recoil;        //射击后座(沿瞄准反向的绘制偏移,弹簧回稳)

        public override void SetStaticDefaults()
        {
            ProjectileID.Sets.DrawScreenCheckFluff[Projectile.type] = 800;
        }

        public override void SetDefaults()
        {
            Projectile.width = 150;
            Projectile.height = 150;
            Projectile.friendly = false;
            Projectile.hostile = true;
            Projectile.penetrate = -1;
            Projectile.tileCollide = false;
            Projectile.timeLeft = 90;
        }

        private VoidPope Pope
        {
            get
            {
                if (OwnerIndex < 0 || OwnerIndex >= Main.maxNPCs)
                {
                    return null;
                }
                NPC n = Main.npc[OwnerIndex];
                return n.active && n.ModNPC is VoidPope pope ? pope : null;
            }
        }

        /// <summary>当前盘朝向(激光/亮心演出用)。</summary>
        public float AimAngle => aimAngle;

        public override void AI()
        {
            VoidPope pope = Pope;
            if (pope == null)
            {
                //教皇没了:快速渐隐退场
                fade -= 0.06f;
                if (fade <= 0f)
                {
                    Projectile.Kill();
                }
                Projectile.timeLeft = 30;
                return;
            }
            byte state = pope.discState;
            NPC owner = pope.NPC;
            Projectile.timeLeft = 90; //由状态机管生死,寿命持续刷新

            //朝向初始化:指向目标玩家
            if (!aimInit)
            {
                aimInit = true;
                aimAngle = owner.HasValidTarget
                    ? (Main.player[owner.target].Center - Projectile.Center).ToRotation()
                    : 0f;
            }

            //状态跳变演出(双端各自凭跳变播,无时序竞争)
            if (state != lastState)
            {
                OnStateChange(lastState, state, owner);
                lastState = state;
            }

            switch (state)
            {
                case 1: //升起:自教皇背后升到头顶上方,自旋加速
                    riseCounter = Math.Min(riseCounter + 1f, RiseTime);
                    {
                        float p = riseCounter / RiseTime;
                        float ease = 1f - (1f - p) * (1f - p);
                        int side = owner.HasValidTarget && Main.player[owner.target].Center.X >= owner.Center.X ? 1 : -1;
                        Vector2 from = owner.Center + new Vector2(-side * 70f, -30f);
                        Vector2 to = owner.Center + new Vector2(0f, -200f);
                        Projectile.Center = Vector2.Lerp(from, to, ease);
                        spinSpeed = 0.3f * p;
                    }
                    break;
                case 2: //跟踪:悬停,朝玩家缓转
                    Projectile.Center = owner.Center + new Vector2(0f, -200f);
                    spinSpeed = 0.3f;
                    if (owner.HasValidTarget)
                    {
                        float want = (Main.player[owner.target].Center - Projectile.Center).ToRotation();
                        float turn = MathHelper.Clamp(MathHelper.WrapAngle(want - aimAngle), -0.035f, 0.035f);
                        aimAngle += turn;
                    }
                    break;
                case 3: //锁定:朝向冻结,自旋骤停(发射前的静止拍)+ 符文升腾
                    Projectile.Center = owner.Center + new Vector2(0f, -200f);
                    spinSpeed = MathHelper.Lerp(spinSpeed, 0.04f, 0.35f);
                    if (!Main.dedServ && Main.rand.NextBool(3))
                    {
                        PRTLoader.NewParticle<PRT_RuneParticle>(
                            Projectile.Center + CEUtils.randomPointInCircle(90f),
                            new Vector2(0, -Main.rand.NextFloat(0.8f, 1.8f)), new Color(210, 150, 255), 0.7f);
                    }
                    break;
                case 4: //射击:激光已由 DiscBeam 承载,盘保持;后座弹簧回稳
                    Projectile.Center = owner.Center + new Vector2(0f, -200f);
                    spinSpeed = MathHelper.Lerp(spinSpeed, 0.45f, 0.25f);
                    break;
                case 5: //退场:缩小渐隐,收完自毁
                    fade -= 1f / 20f;
                    if (fade <= 0f)
                    {
                        Projectile.Kill();
                    }
                    break;
                case 6: //悬顶(P2-7/P2-8 起手)
                    {
                        Vector2 want = owner.Center + new Vector2(0f, -180f);
                        Projectile.Center = Vector2.Lerp(Projectile.Center, want, 0.16f);
                        spinSpeed = MathHelper.Lerp(spinSpeed, 0.2f, 0.1f);
                    }
                    break;
                case 7: //掷出:velocity 由教皇服务端点火,原生同步;本地只管自旋与判定
                    spinSpeed = 0.5f;
                    if (!Main.dedServ && Main.rand.NextBool(2))
                    {
                        var p = PRTLoader.NewParticle<PRT_Light>(Projectile.Center + CEUtils.randomPointInCircle(70f),
                            -Projectile.velocity * 0.1f, new Color(170, 80, 255), 0.5f);
                        p.Configure(0.85f, lifetime: 14);
                    }
                    break;
                case 8: //减速悬停(玩家附近的 1s 预告拍)
                    Projectile.velocity *= 0.9f;
                    spinSpeed = MathHelper.Lerp(spinSpeed, 0.22f, 0.08f);
                    break;
                case 9: //破门后:渐隐退场
                    Projectile.velocity *= 0.85f;
                    fade -= 1f / 30f;
                    if (fade <= 0f)
                    {
                        Projectile.Kill();
                    }
                    break;
                default: //0:无主状态,渐隐
                    fade -= 0.05f;
                    if (fade <= 0f)
                    {
                        Projectile.Kill();
                    }
                    break;
            }

            spin += spinSpeed;
            lockFlash = Math.Max(lockFlash - 1f, 0f);
            recoil *= 0.86f;
            Lighting.AddLight(Projectile.Center, 0.7f * fade, 0.3f * fade, 1.0f * fade);

            //服务端把盘位镜像进教皇同步字段(P2-8 破门点凭它)
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                pope.discPos = Projectile.Center;
            }
        }

        /// <summary>状态跳变的一次性演出(双端各自播)。</summary>
        private void OnStateChange(byte from, byte to, NPC owner)
        {
            if (Main.dedServ)
            {
                return;
            }
            if (to == 3)
            {
                //锁定咔哒(§4.2:盘面纹路瞬亮 + 一帧收束线 + 音效咔哒)
                SoundEngine.PlaySound(SoundID.Unlock with { Volume = 1.1f, Pitch = -0.2f }, Projectile.Center);
                lockFlash = 7f;
                for (int i = 0; i < 6; i++)
                {
                    Vector2 offset = (i * MathHelper.TwoPi / 6f + spin).ToRotationVector2() * 150f;
                    var s = PRTLoader.NewParticle<PRT_Light>(Projectile.Center + offset, -offset * 0.11f,
                        new Color(220, 150, 255), 0.55f);
                    s.Configure(0.9f, squishStrenght: 3.4f, maxSquish: 4.5f, lifetime: 12);
                }
            }
            if (to == 4)
            {
                //开火:后座 + 盘面再闪
                recoil = 16f;
                lockFlash = Math.Max(lockFlash, 4f);
            }
            if (to == 7)
            {
                SoundEngine.PlaySound(SoundID.DD2_BetsysWrathShot with { Volume = 1f, Pitch = -0.3f }, Projectile.Center);
            }
            if (to == 9)
            {
                //破门爆闪(§4.2 P2-8:教皇自盘心现身)
                if (Main.LocalPlayer.Distance(Projectile.Center) < 2200f)
                {
                    CalamityEntropy.FlashEffectStrength = 0.45f;
                }
                SoundEngine.PlaySound(SoundID.Roar with { Volume = 1f, Pitch = 0.1f }, Projectile.Center);
                SoundEngine.PlaySound(SoundID.Item8 with { Volume = 1f, Pitch = -0.5f }, Projectile.Center);
                ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.ScreenShake(Vector2.Zero, 9), Main.LocalPlayer.Distance(Projectile.Center), 2000);
                PRTLoader.NewParticle<PRT_PulseRing>(Projectile.Center, Vector2.Zero, new Color(190, 100, 255), 0.1f).Configure(6f, 40);
                PRTLoader.NewParticle<PRT_PulseRing>(Projectile.Center, Vector2.Zero, Color.White, 0.1f).Configure(4f, 30);
                for (int i = 0; i < 40; i++)
                {
                    var v = PRTLoader.NewParticle<PRT_Void>(Projectile.Center,
                        CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(2f, 12f), Color.White, 1f);
                    v.Opacity = Main.rand.Next(30, 90) * 0.01f;
                }
            }
        }

        public override bool CanHitPlayer(Player target)
        {
            //只在掷出与悬停段有接触判定(§4.2 P2-8:接触 200)
            VoidPope pope = Pope;
            return pope != null && (pope.discState == 7 || pope.discState == 8);
        }

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            VoidPope pope = Pope;
            if (pope == null || (pope.discState != 7 && pope.discState != 8))
            {
                return false;
            }
            return Vector2.Distance(targetHitbox.Center.ToVector2(), Projectile.Center) < HitRadius;
        }

        public override bool PreDraw(ref Color lightColor)
        {
            if (fade <= 0.01f)
            {
                return false;
            }
            VoidPope pope = Pope;
            byte state = pope?.discState ?? 0;
            SpriteBatch sb = Main.spriteBatch;
            Texture2D tex = TextureAssets.Projectile[Projectile.type].Value;
            //射击后座:绘制沿瞄准反向偏移,弹簧回稳(质量反应,位置与判定不动)
            Vector2 pos = Projectile.Center - aimAngle.ToRotationVector2() * recoil - Main.screenPosition;
            float scale = 0.85f * fade;

            //符文法阵环:盘后反旋 VoidGlyph(维度法器的流转辉光,跟踪/锁定期升温)
            sb.UseAdditive();
            {
                Texture2D glyph = glyphTex.Value;
                float runeAlpha = state switch { 2 => 0.5f, 3 => 0.85f, 4 => 0.95f, _ => 0.3f };
                float runePulse = 1f + 0.06f * (float)Math.Sin(Main.GlobalTimeWrappedHourly * 5f);
                sb.Draw(glyph, pos, null, new Color(170, 90, 255) * (runeAlpha * fade), -spin * 0.6f,
                    glyph.Size() / 2, 0.95f * scale / 0.85f * runePulse, SpriteEffects.None, 0);
                sb.Draw(glyph, pos, null, new Color(230, 180, 255) * (runeAlpha * 0.55f * fade), spin * 0.35f,
                    glyph.Size() / 2, 0.66f * scale / 0.85f, SpriteEffects.None, 0);
            }
            CEUtils.ReSetToEndShader();

            //盘体:自旋主体 + 暗层反旋(维度感)
            sb.Draw(tex, pos, null, Color.White * fade, spin, tex.Size() / 2, scale, SpriteEffects.None, 0);
            sb.UseAdditive();
            sb.Draw(tex, pos, null, new Color(150, 70, 255) * (0.35f * fade), -spin * 0.5f, tex.Size() / 2, scale * 0.8f, SpriteEffects.None, 0);

            //锁定拍盘面纹路瞬亮(白热两帧,读作"咔哒"的画面对应物)
            if (lockFlash > 0f)
            {
                sb.Draw(tex, pos, null, Color.White * (fade * lockFlash / 7f), spin, tex.Size() / 2, scale * 1.01f, SpriteEffects.None, 0);
            }

            //盘心亮起:锁定/射击段渐亮 + 沿瞄准方向的指示光(§4.2:锁定后可侧移躲开,朝向必须可读)
            if (state == 3 || state == 4)
            {
                Texture2D glow = glowTex.Value;
                float pulse = state == 4 ? 1.35f : 1f + 0.2f * (float)Math.Sin(spin * 3f);
                sb.Draw(glow, pos, null, new Color(220, 140, 255) * (0.95f * fade), 0, glow.Size() / 2, 1.5f * pulse, SpriteEffects.None, 0);
                sb.Draw(glow, pos, null, Color.White * (0.6f * fade), 0, glow.Size() / 2, 0.8f * pulse, SpriteEffects.None, 0);
            }
            else
            {
                Texture2D glow = glowTex.Value;
                sb.Draw(glow, pos, null, new Color(170, 80, 255) * (0.4f * fade), 0, glow.Size() / 2, 0.9f, SpriteEffects.None, 0);
            }
            CEUtils.ReSetToEndShader();
            return false;
        }
    }

    /// <summary>
    /// 维度魔盘的贯穿巨激光(§4.2 P2-5):基点与方向生成时锁定(方向借初速度通道),
    /// 20t 锁定警示(渐亮细线,与盘心亮起同窗)→ 45t 厚束(宽 90px 判定,长 1400px,震屏)→ 12t 渐灭。
    /// 锁定后不再跟踪 = 方向在生成帧冻结,侧移即可躲开。伤害 260 档。
    /// </summary>
    public class DiscBeam : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Assets/Extra/Ports/Invisible";

        public const int WarnTime = 20;
        public const int BeamTime = 45;
        public const int FadeTime = 12;
        public const float BeamLength = 1400f;
        public const float BeamWidth = 90f;

        private float Timer => WarnTime + BeamTime + FadeTime - Projectile.timeLeft;

        public override void SetStaticDefaults()
        {
            ProjectileID.Sets.DrawScreenCheckFluff[Projectile.type] = 2000;
        }

        public override void SetDefaults()
        {
            Projectile.width = 30;
            Projectile.height = 30;
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
            float t = Timer;
            if ((int)t == WarnTime && !Main.dedServ)
            {
                //发射拍:重音 + 震屏 + 出膛/落点两端爆花(§4.2:震屏)
                SoundEngine.PlaySound(SoundID.Zombie104 with { Volume = 1.1f, Pitch = -0.35f }, Projectile.Center);
                SoundEngine.PlaySound(new SoundStyle("CalamityEntropy/Assets/Sounds/light_bolt") { Volume = 1f, Pitch = -0.5f }, Projectile.Center);
                ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.ScreenShake(Vector2.Zero, 11), Main.LocalPlayer.Distance(Projectile.Center), 2400);
                Vector2 dir = Projectile.rotation.ToRotationVector2();
                var muzzle = PRTLoader.NewParticle<PRT_DirectionalPulseRing>(
                    Projectile.Center, Vector2.Zero, new Color(220, 150, 255), 0.1f);
                muzzle.Configure(new Vector2(2.4f, 0.9f), Projectile.rotation, 2.6f, 18);
                for (int i = 0; i < 10; i++)
                {
                    var s = PRTLoader.NewParticle<PRT_GlowSparkCal>(Projectile.Center,
                        dir.RotatedBy(Main.rand.NextFloat(-0.45f, 0.45f)) * Main.rand.NextFloat(6f, 15f),
                        new Color(210, 140, 255), Main.rand.NextFloat(0.6f, 1f));
                    s.Configure(false, 16, new Vector2(2.2f, 0.5f), quickShrink: true);
                }
                Vector2 endPos = Projectile.Center + dir * BeamLength;
                PRTLoader.NewParticle<PRT_PulseRing>(endPos, Vector2.Zero, new Color(200, 130, 255), 0.1f)
                    .Configure(4f, 26);
            }
            if (t >= WarnTime && t < WarnTime + BeamTime)
            {
                //持续微震
                if (!Main.dedServ && (int)t % 10 == 0)
                {
                    ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.ScreenShake(Vector2.Zero, 3), Main.LocalPlayer.Distance(Projectile.Center), 1800);
                }
                Lighting.AddLight(Projectile.Center + Projectile.rotation.ToRotationVector2() * 500f, 0.8f, 0.35f, 1.2f);
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
            return CEUtils.LineThroughRect(Projectile.Center,
                Projectile.Center + Projectile.rotation.ToRotationVector2() * BeamLength, targetHitbox, (int)BeamWidth);
        }

        public override bool PreDraw(ref Color lightColor)
        {
            float t = Timer;
            Texture2D beam = CEExtraAssets.vlbw;
            Vector2 pos = Projectile.Center - Main.screenPosition;
            Vector2 origin = beam.Size() / 2 * new Vector2(0, 1);
            float lenScale = BeamLength / beam.Width;
            if (t < WarnTime)
            {
                //锁定警示:细线渐亮(与盘心亮起同一节拍)
                Main.spriteBatch.UseAdditive();
                float bright = t / WarnTime;
                Color c = Color.Lerp(new Color(70, 25, 120), new Color(220, 140, 255), bright) * (0.4f + 0.6f * bright);
                Main.spriteBatch.Draw(beam, pos, null, c, Projectile.rotation, origin,
                    new Vector2(lenScale, 0.25f + 0.35f * bright), SpriteEffects.None, 0);
                CEUtils.ReSetToEndShader();
                return false;
            }
            //厚束:PopeBeam 分层束体(白核 + 色晕 + 热浪扰动边缘);出膛端 6t 展宽(公平阀)保留
            float fade = t >= WarnTime + BeamTime ? 1f - (t - WarnTime - BeamTime) / FadeTime : 1f;
            float grow = MathHelper.Clamp((t - WarnTime) / 6f, 0f, 1f);
            PopeVfx.DrawBeam(Projectile.Center, Projectile.rotation, BeamLength, BeamWidth * 1.5f, fade, grow,
                new Color(160, 70, 255), new Color(70, 24, 140), coreFrac: 0.26f, fringe: 0.6f,
                flicker: 0.3f, flickerSeed: Projectile.identity * 1.7f);
            //端点爆花:出膛核 + 远端撞点辉光;出膛冲击帧(头 2.5t 全长白闪)
            Main.spriteBatch.UseAdditive();
            PopeVfx.DrawBeamCap(Main.spriteBatch, Projectile.Center, (0.95f + 0.15f * (float)Math.Sin(t * 0.8f)) * fade, fade, new Color(220, 150, 255));
            Vector2 endPos = Projectile.Center + Projectile.rotation.ToRotationVector2() * BeamLength;
            PopeVfx.DrawBeamCap(Main.spriteBatch, endPos, 0.7f * fade, fade * 0.85f, new Color(190, 110, 255));
            if (t - WarnTime < 2.5f)
            {
                Main.spriteBatch.Draw(beam, pos, null, Color.White * (0.95f * (1f - (t - WarnTime) / 2.5f)), Projectile.rotation, origin,
                    new Vector2(lenScale, BeamWidth * 1.9f / beam.Height), SpriteEffects.None, 0);
            }
            CEUtils.ReSetToEndShader();
            return false;
        }
    }
}
