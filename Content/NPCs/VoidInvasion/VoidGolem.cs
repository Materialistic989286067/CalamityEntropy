using CalamityEntropy.Common;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using CalamityEntropy.Content.Projectiles.VoidInvasion;
using InnoVault;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.NPCs.VoidInvasion
{
    /// <summary>
    /// 虚空魔像(void-invasion.md §2.6):精英层走地重锤,读招-惩罚型。
    /// 三招:跳跃锤击(蹲 25t → 抛物线跃向起跳时玩家位 → 落地双侧冲击波 → 收招 40t 窗口)、
    /// 光柱锤击(每第 3 锤替换:落点两侧 120/240px 四处法阵告警 30t → 光柱 20t)、
    /// 远距冲撞(>700px:蓄力 30t → 8px/t 狂奔,撞墙/触玩家/4s 超时 → 踉跄 30t 窗口)。
    /// M4 主教投放走 <see cref="StartCharging"/> 或生成时置 ai[3]=1(出场即狂奔)。
    /// </summary>
    public class VoidGolem : ModNPC, IVoidInvasionNPC
    {
        public override string Texture => "CalamityEntropy/Content/NPCs/VoidInvasion/Golem/atk1";

        //逐帧散图数组只在绘制路径读取(服务器恒 null,AI 不许碰)
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Golem/walk", 1, 8, AssetMode = AssetMode.TextureValueArray)]
        private static Texture2D[] walkFrames;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/Golem/atk", 1, 16, AssetMode = AssetMode.TextureValueArray)]
        private static Texture2D[] atkFrames;

        public enum GolemState : byte
        {
            Walk,
            HammerCrouch,
            HammerJump,
            HammerSlam,
            HammerRecover,
            ChargeWindup,
            Charging,
            ChargeStagger,
        }

        //节拍与数值常量(§2.6)
        private const float WalkSpeed = 2.2f;
        private const int CrouchTime = 25;
        private const int SlamTime = 10;
        private const int RecoverTime = 40;
        private const int ChargeWindupTime = 30;
        private const float ChargeSpeed = 8f;
        private const int ChargeMaxTime = 240;
        private const int StaggerTime = 30;
        private const float HammerRange = 320f;
        private const float ChargeRange = 700f;
        //126x124 画布实心约 88x86,按判定框 100x120 放大补齐(进游戏校验)
        private const float DrawScale = 1.25f;

        public GolemState state = GolemState.Walk;
        public int stateTimer = 0;
        public Vector2 jumpTarget = Vector2.Zero;
        public byte smashCounter = 0;
        public sbyte chargeDir = 1;
        //行走动画计数,双端 AI 各自推进(纯视觉,不同步)
        private float walkAnimCount = 0;

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = 1;
            NPCID.Sets.MPAllowedEnemies[Type] = true;
        }

        public override void SetBestiary(Terraria.GameContent.Bestiary.BestiaryDatabase database, Terraria.GameContent.Bestiary.BestiaryEntry bestiaryEntry)
        {
            bestiaryEntry.Info.AddRange(new Terraria.GameContent.Bestiary.IBestiaryInfoElement[]
            {
                new Terraria.GameContent.Bestiary.FlavorTextBestiaryInfoElement("Mods.CalamityEntropy.VoidGolemBestiary")
            });
        }

        public override void SetDefaults()
        {
            NPC.width = 100;
            NPC.height = 120;
            NPC.damage = 240;
            if (Main.expertMode)
            {
                NPC.damage += 20;
            }
            if (Main.masterMode)
            {
                NPC.damage += 20;
            }
            NPC.lifeMax = 350000;
            NPC.defense = 120;
            NPC.knockBackResist = 0f;
            NPC.aiStyle = -1;
            NPC.noGravity = false;
            NPC.noTileCollide = false;
            NPC.HitSound = SoundID.NPCHit4;
            NPC.DeathSound = SoundID.NPCDeath14;
            NPC.value = Item.buyPrice(0, 5, 0, 0);
            NPC.Entropy().VoidTouchDR = 0.7f;
        }

        public override bool CheckActive()
        {
            return false;
        }

        public override void OnSpawn(IEntitySource source)
        {
            //M4 公共入口之二:主教传送门投放"冲撞中的魔像"(§2.3),生成侧置 ai[3]=1 即出场狂奔
            if (NPC.ai[3] == 1 && Main.netMode != NetmodeID.MultiplayerClient)
            {
                StartCharging(0);
            }
        }

        /// <summary>
        /// M4 公共入口(§2.3/§2.6):跳过蓄力拍直接进入冲撞狂奔。
        /// dir = 冲撞水平方向(±1),传 0 自动朝最近玩家。服务端调用,状态随 SendExtraAI 同步。
        /// </summary>
        public void StartCharging(int dir)
        {
            if (dir == 0)
            {
                NPC.TargetClosest(false);
                dir = NPC.HasValidTarget && Main.player[NPC.target].Center.X > NPC.Center.X ? 1 : -1;
            }
            chargeDir = (sbyte)Math.Sign(dir);
            NPC.direction = NPC.spriteDirection = chargeDir;
            SwitchState(GolemState.Charging);
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write((byte)state);
            writer.Write(stateTimer);
            writer.WriteVector2(jumpTarget);
            writer.Write(smashCounter);
            writer.Write(chargeDir);
        }

        public override void ReceiveExtraAI(BinaryReader reader)
        {
            state = (GolemState)reader.ReadByte();
            stateTimer = reader.ReadInt32();
            jumpTarget = reader.ReadVector2();
            smashCounter = reader.ReadByte();
            chargeDir = reader.ReadSByte();
        }

        private void SwitchState(GolemState next)
        {
            state = next;
            stateTimer = 0;
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                NPC.netUpdate = true;
            }
        }

        public override void AI()
        {
            //跳锤落点档接触 260,其余 240(§2.6);defDamage 是 SetDefaults 定稿值
            NPC.damage = state == GolemState.HammerJump || state == GolemState.HammerSlam ? NPC.defDamage + 20 : NPC.defDamage;

            if (!NPC.HasValidTarget)
            {
                NPC.TargetClosest(false);
                if (!NPC.HasValidTarget)
                {
                    NPC.velocity.X *= 0.9f;
                    return;
                }
            }
            Player target = Main.player[NPC.target];
            //跳跃/冲撞期间锁面向,其余朝目标
            if (state != GolemState.HammerJump && state != GolemState.Charging && state != GolemState.ChargeWindup)
            {
                NPC.direction = NPC.spriteDirection = target.Center.X > NPC.Center.X ? 1 : -1;
            }
            stateTimer++;
            bool grounded = NPC.velocity.Y == 0;

            switch (state)
            {
                case GolemState.Walk:
                    {
                        float dist = NPC.Center.Distance(target.Center);
                        NPC.velocity.X = MathHelper.Lerp(NPC.velocity.X, WalkSpeed * NPC.direction, 0.08f);
                        if (Math.Abs(NPC.velocity.X) > 0.3f && grounded)
                        {
                            walkAnimCount += 1f / 6f;
                            if (NPC.collideX && !ClimbStep(NPC.direction, 2))
                            {
                                NPC.velocity.Y = -6.5f;
                            }
                        }
                        //出招派发仅服务端;stateTimer 门槛给收招后的呼吸拍(§0.3 支柱 2)
                        if (grounded && stateTimer >= 30 && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            if (dist > ChargeRange)
                            {
                                chargeDir = (sbyte)NPC.direction;
                                SwitchState(GolemState.ChargeWindup);
                            }
                            else if (dist < HammerRange)
                            {
                                SwitchState(GolemState.HammerCrouch);
                            }
                        }
                        break;
                    }
                case GolemState.HammerCrouch:
                    {
                        //下蹲 25t:脚下扬尘(§2.6)
                        NPC.velocity.X *= 0.8f;
                        if (!Main.dedServ && Main.rand.NextBool(2))
                        {
                            Dust d = Dust.NewDustDirect(NPC.BottomLeft - new Vector2(0, 8), NPC.width, 8, DustID.Smoke, -NPC.direction * 2f, -1.5f, 130, default, 1.1f);
                            d.noGravity = true;
                        }
                        if (stateTimer >= CrouchTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            //锁定起跳时玩家位置(§2.6 落点为起跳时玩家位),按 0.3/t 重力解抛物线初速
                            jumpTarget = target.Center;
                            Vector2 d = jumpTarget - NPC.Center;
                            float flightTime = MathHelper.Clamp(Math.Abs(d.X) / 8f, 25f, 45f);
                            NPC.velocity = new Vector2(d.X / flightTime, d.Y / flightTime - 0.5f * 0.3f * flightTime);
                            SwitchState(GolemState.HammerJump);
                        }
                        break;
                    }
                case GolemState.HammerJump:
                    {
                        //空中举锤,引擎重力自然回落;落地进砸击
                        if (stateTimer > 6 && (grounded || NPC.collideY) && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            SwitchState(GolemState.HammerSlam);
                        }
                        break;
                    }
                case GolemState.HammerSlam:
                    {
                        NPC.velocity.X = 0;
                        if (stateTimer == 1)
                        {
                            DoSlamImpact();
                        }
                        if (stateTimer >= SlamTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            SwitchState(GolemState.HammerRecover);
                        }
                        break;
                    }
                case GolemState.HammerRecover:
                    {
                        //收招 40t:可打窗口(§2.6)
                        NPC.velocity.X *= 0.9f;
                        if (stateTimer >= RecoverTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            SwitchState(GolemState.Walk);
                        }
                        break;
                    }
                case GolemState.ChargeWindup:
                    {
                        //前倾蓄力 30t:脚刨地扬尘(§2.6)
                        NPC.velocity.X *= 0.8f;
                        if (!Main.dedServ)
                        {
                            Dust d = Dust.NewDustDirect(NPC.Bottom - new Vector2(chargeDir * NPC.width / 2 + 10, 10), 20, 10, DustID.Smoke, -chargeDir * 3f, -2f, 120, default, 1.3f);
                            d.noGravity = true;
                        }
                        if (stateTimer >= ChargeWindupTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            SwitchState(GolemState.Charging);
                        }
                        break;
                    }
                case GolemState.Charging:
                    {
                        NPC.direction = NPC.spriteDirection = chargeDir;
                        NPC.velocity.X = ChargeSpeed * chargeDir;
                        walkAnimCount += 1f / 3f;
                        if (!Main.dedServ)
                        {
                            //起跑拍:蹬地尘爆 + 白闪 + 低吼(冲撞的"出膛")
                            if (stateTimer == 1)
                            {
                                SoundEngine.PlaySound(SoundID.ForceRoarPitched with { Volume = 0.45f, Pitch = -0.45f }, NPC.Center);
                                var flash = PRTLoader.NewParticle<PRT_BloomCal>(NPC.Center, Vector2.Zero, new Color(220, 150, 255), 0.3f);
                                flash.Configure(1.4f, 9);
                                for (int i = 0; i < 10; i++)
                                {
                                    Dust d = Dust.NewDustDirect(NPC.Bottom - new Vector2(chargeDir * NPC.width / 2 + 16, 12), 32, 12, DustID.Smoke,
                                        -chargeDir * Main.rand.NextFloat(2f, 5f), -Main.rand.NextFloat(1f, 3f), 110, default, Main.rand.NextFloat(1.1f, 1.8f));
                                    d.noGravity = Main.rand.NextBool();
                                }
                            }
                            //冲撞途中:身后速度线 + 沿途尘土
                            if (Main.rand.NextBool(2))
                            {
                                var line = PRTLoader.NewParticle<PRT_LineCal>(NPC.Center + CEUtils.randomPointInCircle(46f),
                                    new Vector2(-chargeDir * Main.rand.NextFloat(5f, 10f), Main.rand.NextFloat(-0.8f, 0.8f)),
                                    new Color(190, 110, 255), Main.rand.NextFloat(0.4f, 0.7f));
                                line.Configure(false, 10);
                            }
                            if (Main.rand.NextBool(2))
                            {
                                var p = PRTLoader.NewParticle<PRT_Void>(NPC.Bottom + new Vector2(Main.rand.NextFloat(-30f, 30f), -8f),
                                    new Vector2(-chargeDir * 2f, -1f), Color.White, 1f);
                                p.Opacity = 0.5f;
                            }
                        }
                        //小台阶抬升双端各自推进(collideX 两端各算),避免位置劈叉;停撞裁决只在服务端
                        bool climbed = false;
                        if (NPC.collideX && grounded)
                        {
                            climbed = ClimbStep(chargeDir, 2);
                        }
                        if (Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            //撞墙(2 格内台阶直接抬升不打断)/触玩家/4s 超时即止(§2.6)
                            bool wallHit = NPC.collideX && grounded && !climbed;
                            bool playerHit = false;
                            foreach (Player p in Main.ActivePlayers)
                            {
                                if (!p.dead && p.Hitbox.Intersects(NPC.Hitbox))
                                {
                                    playerHit = true;
                                    break;
                                }
                            }
                            if (wallHit || playerHit || stateTimer >= ChargeMaxTime)
                            {
                                SwitchState(GolemState.ChargeStagger);
                            }
                        }
                        break;
                    }
                case GolemState.ChargeStagger:
                    {
                        //踉跄硬直 30t:可打窗口(§2.6);入拍闷响 + 震屏放在状态切换处,联机端也能感到撞击
                        if (stateTimer == 1 && !Main.dedServ)
                        {
                            SoundEngine.PlaySound(SoundID.Item14 with { Volume = 0.7f, Pitch = -0.3f }, NPC.Center);
                            ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.NoDirQuickShake(6), Main.LocalPlayer.Distance(NPC.Center));
                        }
                        NPC.velocity.X *= 0.85f;
                        if (stateTimer >= StaggerTime && Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            SwitchState(GolemState.Walk);
                        }
                        break;
                    }
            }
        }

        /// <summary>
        /// 落地拍:双侧冲击波(每第 3 锤替换为 120/240px 四处光柱,§2.6)+ 震屏与尘土。
        /// 弹幕仅服务端生成;表现拍在双端 stateTimer==1 时各自触发。
        /// </summary>
        private void DoSlamImpact()
        {
            if (!Main.dedServ)
            {
                SoundEngine.PlaySound(SoundID.Item14, NPC.Center);
                ScreenShaker.AddShakeWithRangeFade(new ScreenShaker.NoDirQuickShake(8), Main.LocalPlayer.Distance(NPC.Center));
                for (int i = 0; i < 26; i++)
                {
                    Dust d = Dust.NewDustDirect(NPC.BottomLeft - new Vector2(10, 16), NPC.width + 20, 16, DustID.Smoke,
                        Main.rand.NextFloat(-4f, 4f), Main.rand.NextFloat(-3.5f, -0.5f), 110, default, Main.rand.NextFloat(1f, 1.8f));
                    d.noGravity = Main.rand.NextBool();
                }
                //落锤冲击:白闪 + 贴地扁环 + 双侧崩石(冲击波的"因"在锤,不在波)
                var flash = PRTLoader.NewParticle<PRT_BloomCal>(NPC.Bottom + new Vector2(0, -14), Vector2.Zero, Color.White, 0.4f);
                flash.Configure(2f, 10);
                var ring = PRTLoader.NewParticle<PRT_DirectionalPulseRing>(NPC.Bottom + new Vector2(0, -10), Vector2.Zero,
                    new Color(210, 130, 255), 0.25f);
                ring.Configure(new Vector2(1.6f, 0.45f), 0f, 3f, 16);
                for (int i = 0; i < 10; i++)
                {
                    Vector2 vel = new Vector2(Main.rand.NextFloat(-6f, 6f), -Main.rand.NextFloat(3f, 8f));
                    var rock = PRTLoader.NewParticle<PRT_LineCal>(NPC.Bottom + new Vector2(Main.rand.NextFloat(-40f, 40f), -10f), vel,
                        Color.Lerp(new Color(160, 120, 210), new Color(90, 60, 130), Main.rand.NextFloat()), Main.rand.NextFloat(0.55f, 0.95f));
                    rock.Configure(true, 28);
                }
            }
            if (Main.netMode == NetmodeID.MultiplayerClient)
                return;

            bool pillarSmash = smashCounter % 3 == 2;
            smashCounter++;
            NPC.netUpdate = true;
            if (pillarSmash)
            {
                //光柱锤击:落点两侧 120/240px 四处,各自贴地,30t 法阵告警 → 光柱 20t,180 档(§2.6)
                int pillarDamage = (int)(NPC.defDamage * 0.375f);
                foreach (float offset in new float[] { -240f, -120f, 120f, 240f })
                {
                    Vector2 basePos = FindGround(NPC.Bottom + new Vector2(offset, -32));
                    Projectile.NewProjectile(NPC.GetSource_FromAI(), basePos, Vector2.Zero,
                        ModContent.ProjectileType<GolemLightPillar>(), pillarDamage, 2f, -1, 30);
                }
            }
            else
            {
                //贴地冲击波,±两侧各一道 18px/t(§2.6),240 档
                int waveDamage = (int)(NPC.defDamage * 0.5f);
                for (int dir = -1; dir <= 1; dir += 2)
                {
                    Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Bottom + new Vector2(dir * 40, -20), new Vector2(dir * 18f, 0),
                        ModContent.ProjectileType<GolemShockwave>(), waveDamage, 2f, -1);
                }
            }
        }

        /// <summary>从起点向下探最近地表(向上先让 2 格),返回柱底/波底应贴的位置。</summary>
        private static Vector2 FindGround(Vector2 from)
        {
            Point tile = from.ToTileCoordinates();
            for (int i = 0; i < 40; i++)
            {
                int y = tile.Y + i;
                if (!WorldGen.InWorld(tile.X, y, 8))
                    break;
                if (WorldGen.SolidOrSlopedTile(tile.X, y))
                {
                    return new Vector2(from.X, y * 16);
                }
            }
            return from;
        }

        /// <summary>≤maxTiles 格的小台阶直接抬升;抬不动返回 false(走位面前的真墙)。</summary>
        private bool ClimbStep(int dir, int maxTiles)
        {
            for (int h = 1; h <= maxTiles; h++)
            {
                Vector2 test = NPC.position + new Vector2(dir * 8f, -h * 16);
                if (!Collision.SolidCollision(test, NPC.width, NPC.height))
                {
                    NPC.position.Y -= h * 16;
                    NPC.position.X += dir * 4f;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 当前帧(仅绘制路径):攻击 16 帧分段 1~3 蹲 / 4~8 跃举 / 9~12 砸 / 13~16 收(§2.6 待验项 3),
        /// 行走与冲撞用 8 帧循环(冲撞 3t/帧加速在 AI 里推进计数)。
        /// </summary>
        private Texture2D CurrentFrame()
        {
            switch (state)
            {
                case GolemState.HammerCrouch:
                    return atkFrames[Math.Min(2, stateTimer / 8)];
                case GolemState.HammerJump:
                    return NPC.velocity.Y < 0 ? atkFrames[Math.Min(5, 3 + stateTimer / 7)] : atkFrames[Math.Min(7, 6 + stateTimer / 10)];
                case GolemState.HammerSlam:
                    return atkFrames[Math.Min(11, 8 + stateTimer / 3)];
                case GolemState.HammerRecover:
                    return atkFrames[Math.Min(15, 12 + stateTimer / 10)];
                case GolemState.ChargeWindup:
                    return atkFrames[Math.Min(1, stateTimer / 12)];
                default:
                    return walkFrames[(int)walkAnimCount % 8];
            }
        }

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            if (NPC.IsABestiaryIconDummy)
            {
                return true;
            }
            Texture2D tex = CurrentFrame();
            //底边锚定:走/攻画布高不同(124/140),地平线取画布底对齐
            Vector2 drawPos = new Vector2(NPC.Center.X, NPC.position.Y + NPC.height + 4) - screenPos;
            //美术原朝右,面向左时翻转
            SpriteEffects fx = NPC.direction == -1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            float wobble = state == GolemState.ChargeStagger ? (float)Math.Sin(stateTimer * 0.5f) * 0.06f : 0f;
            spriteBatch.Draw(tex, drawPos, null, drawColor * NPC.Opacity, NPC.rotation + wobble,
                new Vector2(tex.Width / 2, tex.Height), DrawScale, fx, 0);
            return false;
        }

        public override void HitEffect(NPC.HitInfo hit)
        {
            if (NPC.life > 0 || Main.dedServ)
                return;
            for (int i = 0; i < 56; i++)
            {
                var p = PRTLoader.NewParticle<PRT_Void>(NPC.Center,
                    CEUtils.randomRot().ToRotationVector2() * Main.rand.Next(0, 400) * 0.01f, Color.White, 1f);
                p.Opacity = Main.rand.Next(20, 100) * 0.01f;
            }
            for (int i = 0; i < 20; i++)
            {
                Dust.NewDust(NPC.position, NPC.width, NPC.height, DustID.Smoke, hit.HitDirection * 2f, -2f);
            }
        }
    }
}
