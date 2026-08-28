using CalamityEntropy.Common;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using CalamityEntropy.Content.Projectiles.VoidInvasion;
using InnoVault.PRT;
using System;
using Terraria;
using Terraria.Audio;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.NPCs.VoidInvasion
{
    /// <summary>
    /// 虚空烛灵(void-invasion.md §2.5):骚扰层空中火力,悬浮 sin 漂、与玩家保持 400~600px;
    /// 远程(>250px)抛 2 枚虚空火球,近程(<180px)定身喷焰 50t 后撤 2s。
    /// 状态全走 ai[] 原生字段(ai[0]=计时 ai[1]=状态 ai[2]=漂浮相位),不加 ExtraAI。
    /// </summary>
    public class VoidCandleWisp : ModNPC, IVoidInvasionNPC
    {
        //状态:0 巡浮 1 远程前摇 2 喷焰 3 后撤 4 冷却
        private const float StDrift = 0;
        private const float StRangedWindup = 1;
        private const float StBreath = 2;
        private const float StRetreat = 3;
        private const float StCooldown = 4;

        private ref float Timer => ref NPC.ai[0];
        private ref float State => ref NPC.ai[1];
        private ref float BobPhase => ref NPC.ai[2];

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = 4;
            NPCID.Sets.MPAllowedEnemies[Type] = true;
        }

        public override void SetBestiary(Terraria.GameContent.Bestiary.BestiaryDatabase database, Terraria.GameContent.Bestiary.BestiaryEntry bestiaryEntry)
        {
            bestiaryEntry.Info.AddRange(new Terraria.GameContent.Bestiary.IBestiaryInfoElement[]
            {
                new Terraria.GameContent.Bestiary.FlavorTextBestiaryInfoElement("Mods.CalamityEntropy.VoidCandleWispBestiary")
            });
        }

        public override void SetDefaults()
        {
            NPC.width = 40;
            NPC.height = 56;
            NPC.damage = 100;
            if (Main.expertMode)
            {
                NPC.damage += 20;
            }
            if (Main.masterMode)
            {
                NPC.damage += 20;
            }
            NPC.lifeMax = 80000;
            NPC.defense = 60;
            NPC.knockBackResist = 0.5f;
            NPC.aiStyle = -1;
            NPC.noGravity = true;
            NPC.noTileCollide = true;
            NPC.lavaImmune = true;
            NPC.HitSound = SoundID.NPCHit3;
            NPC.DeathSound = SoundID.NPCDeath3;
            NPC.value = Item.buyPrice(0, 0, 2, 0);
            NPC.Entropy().VoidTouchDR = 0.6f;
        }

        public override bool CheckActive()
        {
            return false;
        }

        public override void AI()
        {
            if (!NPC.HasValidTarget)
            {
                NPC.TargetClosest(false);
                if (!NPC.HasValidTarget)
                {
                    NPC.velocity *= 0.96f;
                    return;
                }
            }
            Player target = Main.player[NPC.target];
            BobPhase += 0.05f;
            float dist = NPC.Center.Distance(target.Center);
            NPC.direction = NPC.spriteDirection = target.Center.X > NPC.Center.X ? 1 : -1;

            switch ((int)State)
            {
                case (int)StDrift:
                case (int)StCooldown:
                    Drift(target, dist);
                    Timer++;
                    if ((int)State == (int)StCooldown)
                    {
                        //冷却 3s 后回巡浮(§2.5 远程节拍)
                        if (Timer >= 180)
                        {
                            State = StDrift;
                            Timer = 0;
                        }
                        break;
                    }
                    //攻击派发只在服务端判,ai[] 随 netUpdate 原生同步
                    if (Timer >= 60 && Main.netMode != NetmodeID.MultiplayerClient)
                    {
                        if (dist < 180f)
                        {
                            State = StBreath;
                            Timer = 0;
                            NPC.netUpdate = true;
                        }
                        else if (dist > 250f)
                        {
                            State = StRangedWindup;
                            Timer = 0;
                            NPC.netUpdate = true;
                        }
                    }
                    break;

                case (int)StRangedWindup:
                    //30t 前摇:减速悬停,烛焰胀大 + 火星向焰心汇聚(末 6t 静默,给出手让因果拍)
                    NPC.velocity *= 0.9f;
                    Timer++;
                    if (!Main.dedServ)
                    {
                        float grow = Math.Min(1f, Timer / 30f);
                        if (Main.rand.NextBool(2))
                        {
                            var p = PRTLoader.NewParticle<PRT_Light>(FlameAnchor + CEUtils.randomPointInCircle(6f),
                                new Vector2(0, -1.5f), new Color(200, 110, 255), 0.3f + 0.35f * grow);
                            p.Configure(0.8f, lifetime: 14);
                        }
                        if (Timer < 24 && Timer % 2 == 0)
                        {
                            Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(35f, 70f);
                            var line = PRTLoader.NewParticle<PRT_LineCal>(FlameAnchor + offset, -offset * 0.13f,
                                new Color(190, 100, 255), Main.rand.NextFloat(0.35f, 0.6f));
                            line.Configure(false, 12);
                        }
                    }
                    //音效按精确拍只播一次;联机客户端 Timer 会在此空转等服务端同步,>= 判定不能挂表现
                    if (Timer == 30 && !Main.dedServ)
                    {
                        SoundEngine.PlaySound(SoundID.Item20, NPC.Center);
                        //出手拍:焰心白闪 + 朝目标的小冲击环
                        var flash = PRTLoader.NewParticle<PRT_BloomCal>(FlameAnchor, Vector2.Zero, Color.White, 0.22f);
                        flash.Configure(1.1f, 9);
                        Vector2 aim = (target.Center - FlameAnchor).SafeNormalize(Vector2.UnitX * NPC.direction);
                        var ring = PRTLoader.NewParticle<PRT_DirectionalPulseRing>(FlameAnchor, Vector2.Zero,
                            new Color(210, 130, 255), 0.12f);
                        ring.Configure(new Vector2(0.5f, 1.1f), aim.ToRotation(), 1.2f, 12);
                    }
                    if (Timer >= 30)
                    {
                        if (Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            //抛 2 枚火球:按 0.1 重力解抛物线初速,落点取玩家当前位(§2.5)
                            //130 经典档 = 敌对弹幕命中 ×2,弹幕伤害取 NPC.damage(100)×0.65
                            for (int i = 0; i < 2; i++)
                            {
                                Vector2 vel = LobVelocity(target.Center + new Vector2(Main.rand.NextFloat(-40f, 40f), 0), 45f + 12f * i);
                                Projectile.NewProjectile(NPC.GetSource_FromAI(), FlameAnchor, vel,
                                    ModContent.ProjectileType<VoidFireball>(), (int)(NPC.damage * 0.65f), 2, -1);
                            }
                            State = StCooldown;
                            Timer = 0;
                            NPC.netUpdate = true;
                        }
                    }
                    break;

                case (int)StBreath:
                    //定身喷焰 50t(§2.5):弹幕吸附自身持续 50t,自己只管定住
                    NPC.velocity *= 0.85f;
                    Timer++;
                    if (Timer == 1)
                    {
                        if (Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            //90 经典档 → 弹幕伤害 NPC.damage(100)×0.45;通用件 scale=1
                            Vector2 dir = (target.Center - NPC.Center).SafeNormalize(Vector2.UnitX * NPC.direction);
                            var proj = Projectile.NewProjectileDirect(NPC.GetSource_FromAI(), NPC.Center, dir,
                                ModContent.ProjectileType<VoidFlameBreath>(), (int)(NPC.damage * 0.45f), 1, -1, NPC.whoAmI);
                            proj.timeLeft = 50;
                            proj.netUpdate = true;
                        }
                        if (!Main.dedServ)
                        {
                            SoundEngine.PlaySound(SoundID.Item34, NPC.Center);
                        }
                    }
                    if (Timer >= 50)
                    {
                        State = StRetreat;
                        Timer = 0;
                    }
                    break;

                case (int)StRetreat:
                    //后撤漂浮 2s(§2.5)
                    Timer++;
                    Vector2 away = (NPC.Center - target.Center).SafeNormalize(-Vector2.UnitX * NPC.direction);
                    NPC.velocity = Vector2.Lerp(NPC.velocity, away * 4f, 0.06f);
                    NPC.velocity.Y += (float)Math.Sin(BobPhase) * 0.05f;
                    if (Timer >= 120)
                    {
                        State = StDrift;
                        Timer = 0;
                    }
                    break;
            }

            Lighting.AddLight(FlameAnchor, 0.45f, 0.2f, 0.6f);
            if (!Main.dedServ)
            {
                //烛焰常燃:火团为主体 + 光点飘散(真火感)
                if (Main.rand.NextBool(4))
                {
                    var f = PRTLoader.NewParticle<PRT_FlameCal>(FlameAnchor + CEUtils.randomPointInCircle(4f),
                        new Vector2(Main.rand.NextFloat(-0.2f, 0.2f), -Main.rand.NextFloat(0.8f, 1.6f)),
                        new Color(190, 100, 255), Main.rand.NextFloat(0.3f, 0.45f));
                    f.Configure(16, 1f, new Color(70, 20, 120));
                }
                if (Main.rand.NextBool(6))
                {
                    var p = PRTLoader.NewParticle<PRT_Light>(FlameAnchor + CEUtils.randomPointInCircle(4f),
                        new Vector2(0, -Main.rand.NextFloat(0.8f, 1.6f)), new Color(180, 90, 255), 0.3f);
                    p.Configure(0.7f, lifetime: 14);
                }
            }
        }

        /// <summary>烛焰位置(头顶):粒子与弹幕出口。</summary>
        private Vector2 FlameAnchor => NPC.Center + new Vector2(0, -NPC.height * 0.5f - 4f);

        /// <summary>巡浮:保持 400~600px 环带,带 sin 上下漂(§2.5)。</summary>
        private void Drift(Player target, float dist)
        {
            Vector2 dir = (target.Center - NPC.Center).SafeNormalize(Vector2.UnitX);
            Vector2 want;
            if (dist > 600f)
            {
                want = dir * 5f;
            }
            else if (dist < 400f)
            {
                want = -dir * 4f;
            }
            else
            {
                want = new Vector2(dir.X * 0.5f, 0);
            }
            want.Y += (float)Math.Sin(BobPhase) * 1.4f;
            NPC.velocity = Vector2.Lerp(NPC.velocity, want, 0.045f);
        }

        /// <summary>按 0.1/t 重力解抛物线初速:飞行 T tick 后过目标点。</summary>
        private Vector2 LobVelocity(Vector2 targetPos, float flightTime)
        {
            Vector2 d = targetPos - FlameAnchor;
            const float g = 0.1f;
            return new Vector2(d.X / flightTime, d.Y / flightTime - 0.5f * g * flightTime);
        }

        public override void FindFrame(int frameHeight)
        {
            if (Main.dedServ)
                return;
            //竖排 4 帧(68x286,286/4=71):帧矩形高度收 1px 防切缝抖动(§2.5 待验项落地)
            NPC.frameCounter++;
            if (NPC.frameCounter >= 6)
            {
                NPC.frameCounter = 0;
                NPC.frame.Y += frameHeight;
            }
            if (NPC.frame.Y >= frameHeight * 4)
            {
                NPC.frame.Y = 0;
            }
            NPC.frame.Height = frameHeight - 1;
        }

        public override void HitEffect(NPC.HitInfo hit)
        {
            if (NPC.life > 0 || Main.dedServ)
                return;
            for (int i = 0; i < 32; i++)
            {
                var p = PRTLoader.NewParticle<PRT_Void>(NPC.Center,
                    CEUtils.randomRot().ToRotationVector2() * Main.rand.Next(0, 400) * 0.01f, Color.White, 1f);
                p.Opacity = Main.rand.Next(20, 100) * 0.01f;
            }
        }
    }
}
