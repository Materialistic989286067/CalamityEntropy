using CalamityEntropy.Assets.Register;
using CalamityEntropy.Common;
using CalamityEntropy.Content.Dusts;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using CalamityEntropy.Content.Projectiles.AbyssalWraithProjs;
using CalamityEntropy.Core.Graphics;
using InnoVault;
using InnoVault.PRT;
using ReLogic.Content;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Utilities;

namespace CalamityEntropy.Content.NPCs.VoidInvasion
{
    /// <summary>
    /// 裂隙恶灵(void-invasion.md §3.1):虚空入侵事件小 Boss,深渊亡魂的"事件回声"。
    /// 底本为 AbyssalWraith 当前版裁剪:保留 t0 随机光球/t1 SighterPin 扇形/t4 羽毛喷射/
    /// t6 环形弹幕(削为 2 轮 ×12)/t7 追踪光球,砍 t2/t3/t5 招牌招与 t8 废案;
    /// 传送门追人阈值 4000→1600,技能间歇下限 30→50t。像素扫描死亡与入场动画保留。
    /// 生成路径两条(spawnSource):0 = 50% 脚本(高空传送门坠出+咆哮),1 = 教徒仪式(阵心上浮);
    /// 同屏 ≥3 时仪式停开新阵(护栏在 VoidCultist 侧)。进度 +12% 在 VoidInvasionGNPC.OnKill 结算。
    /// 贴图为深渊亡魂整套的独立副本(RiftWraith/),绘制统一乘蓝紫染色与本体拉开色差;
    /// 大血条走 EntropyBossbar.bigBarMiniBoss(NPC.boss=false,避开原版 Boss 播报语义)。
    /// </summary>
    public class RiftWraith : ModNPC, IVoidInvasionNPC
    {
        public override string Texture => "CalamityEntropy/Content/NPCs/VoidInvasion/RiftWraith/RiftWraith";

        /// <summary>蓝紫染色(§3.1:drawColor 乘 (0.8, 0.75, 1.0),所有绘制分支统一乘)</summary>
        public static readonly Color RiftTint = new Color(204, 191, 255);

        //头图沿用复制过来的深渊亡魂两张(染色后补不阻塞);手动登记,不走 AutoloadBossHead
        public static int icon = -1;
        public static int iconGather = -1;
        public static void loadHead()
        {
            string path = "CalamityEntropy/Content/NPCs/VoidInvasion/RiftWraith/AbyssalWraith_Head_Boss";
            CalamityEntropy.Instance.AddBossHeadTexture(path, -1);
            icon = ModContent.GetModBossHeadSlot(path);
            string pathGather = "CalamityEntropy/Content/NPCs/VoidInvasion/RiftWraith/AbyssalWraith_Head_Boss_GatherWing";
            CalamityEntropy.Instance.AddBossHeadTexture(pathGather, -1);
            iconGather = ModContent.GetModBossHeadSlot(pathGather);
        }
        public override void BossHeadSlot(ref int index)
        {
            if (gatherWing > 0.5f)
            {
                index = iconGather;
            }
            else
            {
                index = icon;
            }
        }

        public int animation = 0;
        public int escape = 0;
        public int wingFrame = 0;
        public int seed = -1;
        public float wingRotLeft = 0;
        public float wingRotRight = 0;
        /// <summary>生成来源(§3.1):0 = 50% 脚本(传送门坠出+咆哮),1 = 教徒仪式(阵心上浮)</summary>
        public byte spawnSource = 0;

        //翅膀帧动画与死亡溶解贴图,加载期由 VaultLoaden 赋值;专用服务器上恒为 null,读取处都在客户端路径或带 dedServ 防护
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/RiftWraith/Wing", 1, 8, AssetMode = AssetMode.TextureValueArray)]
        private static Texture2D[] wingflying;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/RiftWraith/AWDeath")]
        private static Asset<Texture2D> awDeathTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/RiftWraith/WingGathering")]
        private static Asset<Texture2D> wingGatheringTex;
        [VaultLoaden("CalamityEntropy/Content/NPCs/VoidInvasion/RiftWraith/WingGather")]
        private static Asset<Texture2D> wingGatherTex;
        [VaultLoaden("CalamityEntropy/Assets/Extra/SoulVortex")]
        private static Asset<Texture2D> soulVortexTex;

        public override void OnSpawn(IEntitySource source)
        {
            seed = Main.rand.Next(0, 10000);
            if (Main.netMode == NetmodeID.Server)
            {
                NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, NPC.whoAmI);
            }
        }

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[NPC.type] = 1;
            NPCID.Sets.MustAlwaysDraw[NPC.type] = true;
            NPCID.Sets.MPAllowedEnemies[Type] = true;
        }

        public override void SetBestiary(Terraria.GameContent.Bestiary.BestiaryDatabase database, Terraria.GameContent.Bestiary.BestiaryEntry bestiaryEntry)
        {
            bestiaryEntry.Info.AddRange(new Terraria.GameContent.Bestiary.IBestiaryInfoElement[]
            {
                new Terraria.GameContent.Bestiary.FlavorTextBestiaryInfoElement("Mods.CalamityEntropy.RiftWraithBestiary")
            });
        }

        public override void SetDefaults()
        {
            //§3.1 数值档:事件小 Boss,不走原版 Boss 语义(播报/音乐优先级),大血条由 EntropyBossbar 侧注册
            NPC.boss = false;
            NPC.width = 140;
            NPC.height = 140;
            NPC.damage = 180;
            if (Main.expertMode)
            {
                NPC.damage += 20;
            }
            if (Main.masterMode)
            {
                NPC.damage += 20;
            }
            NPC.defense = 100;
            NPC.lifeMax = 1200000;
            NPC.HitSound = SoundID.NPCHit4;
            NPC.DeathSound = SoundID.NPCHit4;
            NPC.value = Item.buyPrice(0, 15, 0, 0);
            NPC.knockBackResist = 0f;
            NPC.noTileCollide = true;
            NPC.noGravity = true;
            NPC.dontCountMe = true;
            NPC.netAlways = true;
            NPC.Entropy().VoidTouchDR = 0.7f;
        }

        public override void SendExtraAI(BinaryWriter writer)
        {
            writer.Write(animation);
            writer.Write(seed);
            writer.Write(deathAnm);
            writer.Write(lbj);
            writer.Write(portal);
            writer.Write(portalTime);
            writer.WriteVector2(portalPos);
            writer.WriteVector2(portalTarget);
            writer.Write(spawnSource);
        }
        public override void ReceiveExtraAI(BinaryReader reader)
        {
            animation = reader.ReadInt32();
            seed = reader.ReadInt32();
            deathAnm = reader.ReadBoolean();
            lbj = reader.ReadSingle();
            portal = reader.ReadBoolean();
            portalTime = reader.ReadInt32();
            portalPos = reader.ReadVector2();
            portalTarget = reader.ReadVector2();
            spawnSource = reader.ReadByte();
        }

        public float anmChange = 0;
        public float anmlerp = 1;
        public long counter = 0;
        public float gatherWing = 0;
        /// <summary>入场动画(§3.1:两种来源都 40t 入场无敌;-=3/t,初值 120)</summary>
        public int spawnAnm = 120;
        public UnifiedRandom random;
        public float alphaPor = 1;
        public float portalAlpha = 0;
        public Color[] pixelData = null;
        public bool deathSoundPlay = true;
        /// <summary>落定/展翼白闪(纯客户端演出,入场收尾拍点)</summary>
        public float landFlash = 0;
        /// <summary>速度门控残影采样(双端各自推,纯视觉)</summary>
        private readonly System.Collections.Generic.List<Vector2> trailPos = new();

        //§3.1 技能取舍:保留 t0/t1/t4/t6/t7,砍 t2 光球墙/t3 保距追踪/t5 巨球(招牌保层级)与 t8 废案
        private static readonly int[] SkillPool = { 0, 1, 4, 6, 7 };

        public override void AI()
        {
            if (deathAnm && deathSoundPlay && !Main.dedServ)
            {
                SoundEngine.PlaySound(new SoundStyle("CalamityEntropy/Assets/Sounds/awdead") { Volume = 0.5f });
                deathSoundPlay = false;
            }
            wingRotLeft *= 0.86f;
            wingRotRight *= 0.86f;
            if (landFlash > 0)
            {
                landFlash -= 0.05f;
            }
            //残影采样(双端各自推,速度门控绘制)
            trailPos.Add(NPC.Center);
            if (trailPos.Count > 8)
            {
                trailPos.RemoveAt(0);
            }

            if (portalTime > 0)
            {
                if (portalAlpha < 1)
                {
                    portalAlpha += 0.05f;
                }
            }
            else
            {
                if (portalAlpha > 0)
                {
                    portalAlpha -= 0.05f;
                }
            }
            if (spawnAnm > 0)
            {
                NPC.dontTakeDamage = true;
                if (spawnAnm == 120)
                {
                    if (spawnSource == 0)
                    {
                        //50% 脚本档:高空门里坠出 + 咆哮(§3.1)。初速抬高+硬刹,总位移与旧值相当,读感更重
                        NPC.velocity = new Vector2(0, 16f);
                        if (!Main.dedServ)
                        {
                            SoundEngine.PlaySound(SoundID.Roar, NPC.Center);
                        }
                    }
                    else
                    {
                        //仪式档:阵心上浮 1s 升起(§3.1)
                        NPC.velocity = new Vector2(0, -2.4f);
                    }
                }
                NPC.velocity *= spawnSource == 0 ? 0.9f : 0.96f;
                if (!Main.dedServ)
                {
                    if (spawnSource == 0)
                    {
                        //坠落撕裂拖尾:身后拉出反向裂隙线 + 紫烟
                        for (int i = 0; i < 2; i++)
                        {
                            PRTLoader.NewParticle<PRT_LineCal>(NPC.Center + CEUtils.randomPointInCircle(60), -NPC.velocity * Main.rand.NextFloat(0.3f, 0.7f),
                                new Color(140, 100, 255), Main.rand.NextFloat(0.5f, 1f)).Configure(false, 16);
                        }
                    }
                    else
                    {
                        //阵心光柱内聚:光线向体心汇聚 + 升腾光点
                        if (Main.rand.NextBool(2))
                        {
                            Vector2 offset = CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(120f, 300f);
                            PRTLoader.NewParticle<PRT_LineCal>(NPC.Center + offset, -offset * 0.07f,
                                new Color(120, 90, 255), Main.rand.NextFloat(0.5f, 0.9f)).Configure(false, 18);
                        }
                        var mote = PRTLoader.NewParticle<PRT_Light>(NPC.Center + new Vector2(Main.rand.NextFloat(-90f, 90f), Main.rand.NextFloat(40f, 160f)),
                            new Vector2(0, -Main.rand.NextFloat(2f, 5f)), new Color(170, 140, 255), 0.5f);
                        mote.Configure(0.8f, lifetime: 22);
                    }
                }
                spawnAnm -= 3;
                if (spawnAnm <= 0)
                {
                    NPC.dontTakeDamage = false;
                    random = new UnifiedRandom(seed);
                    landFlash = 1f;
                    //落定/展翼冲击拍:震屏 + 双脉冲环 + 裂隙碎片爆散
                    if (!Main.dedServ)
                    {
                        SoundEngine.PlaySound(SoundID.Item122 with { Volume = 0.8f, Pitch = -0.35f }, NPC.Center);
                        CEUtils.SetShake(NPC.Center, spawnSource == 0 ? 9f : 6f, 2200);
                        PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, new Color(140, 110, 255), 0.1f).Configure(3.6f, 34);
                        PRTLoader.NewParticle<PRT_PulseRing>(NPC.Center, Vector2.Zero, Color.White, 0.1f).Configure(2.2f, 26);
                        for (int i = 0; i < 16; i++)
                        {
                            PRTLoader.NewParticle<PRT_CrystalGlow>(NPC.Center + CEUtils.randomPointInCircle(50),
                                CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(2f, 8f),
                                new Color(150, 120, 255), Main.rand.NextFloat(0.4f, 0.8f)).Configure(0.9f, true, PRTDrawModeEnum.AdditiveBlend, CEUtils.randomRot(), 26);
                        }
                    }
                }
                return;
            }
            if (NPC.life < 2)
            {
                if (!deathAnm)
                {
                    deathAnm = true;
                    lbj = 6f;
                    NPC.netUpdate = true;
                    if (NPC.netSpam >= 10)
                        NPC.netSpam = 9;
                    if (Main.netMode == NetmodeID.Server)
                    {
                        NetMessage.SendData(MessageID.SyncNPC, -1, -1, null, NPC.whoAmI);
                    }
                }
            }
            Texture2D deathTex = Main.dedServ ? null : awDeathTex.Value;
            if (deathAnm)
            {
                NPC.dontTakeDamage = true;
                NPC.damage = 0;
                lbsize += lbj;
                lbj -= 0.16f;
                if (lbsize <= 0)
                {
                    lbsize = 0;
                }
                animation = 0;
                wingFrame = 0;

                if (pixelData == null && !Main.dedServ)
                {
                    pixelData = new Color[deathTex.Width * deathTex.Height];
                    deathTex.GetData(pixelData);
                }
                if (deathPer >= 1 - 0.005f)
                {
                    deathPer = 1 - 0.005f;
                    Kill();
                    return;
                }
                else
                {
                    deathPer += 0.005f;
                    if (deathPer > 1 - 0.005f)
                    {
                        deathPer = 1 - 0.005f;
                    }
                    if (!Main.dedServ)
                    {
                        if (pixelData.Length > 1)
                        {
                            Color GetPixelColor(Texture2D texture, Color[] pixelData, int x, int y)
                            {
                                int width = texture.Width;
                                int height = texture.Height;

                                if (x < 0 || x >= width || y < 0 || y >= height || y * width + x >= pixelData.Length)
                                    throw new ArgumentOutOfRangeException("x or y is out of bounds of the texture:" + x.ToString() + "," + y.ToString() + "/" + pixelData.Length.ToString());

                                int index = y * width + x;
                                return pixelData[index];
                            }

                            if (deathPer < 1)
                            {
                                for (int i = 0; i < deathTex.Width; i += 6)
                                {
                                    if (i >= deathTex.Width)
                                    {
                                        continue;
                                    }
                                    if (GetPixelColor(deathTex, pixelData, i, (int)(deathTex.Height * deathPer)).A != 0)
                                    {
                                        Dust.NewDust(NPC.Center + (-deathTex.Size() / 2 + new Vector2(i, (deathTex.Height * deathPer))) * NPC.scale, 1, 1, ModContent.DustType<AwDeath>());
                                    }
                                }
                                //扫描线撕裂余料:裂隙碎晶沿扫描沿剥落上飘 + 偶发裂线闪
                                if (counter % 3 == 0)
                                {
                                    Vector2 scanPos = NPC.Center + (-deathTex.Size() / 2 + new Vector2(Main.rand.NextFloat(deathTex.Width), deathTex.Height * deathPer)) * NPC.scale;
                                    PRTLoader.NewParticle<PRT_CrystalGlow>(scanPos, new Vector2(Main.rand.NextFloat(-1f, 1f), -Main.rand.NextFloat(1f, 3f)),
                                        new Color(160, 130, 255), Main.rand.NextFloat(0.3f, 0.6f)).Configure(0.85f, true, PRTDrawModeEnum.AdditiveBlend, CEUtils.randomRot(), 24);
                                }
                                if (counter % 9 == 0)
                                {
                                    Vector2 scanPos = NPC.Center + (-deathTex.Size() / 2 + new Vector2(Main.rand.NextFloat(deathTex.Width), deathTex.Height * deathPer)) * NPC.scale;
                                    PRTLoader.NewParticle<PRT_LineCal>(scanPos, new Vector2(0, -Main.rand.NextFloat(2f, 4f)),
                                        new Color(200, 180, 255), Main.rand.NextFloat(0.4f, 0.8f)).Configure(false, 14);
                                }
                            }
                        }
                        else
                        {
                            pixelData = new Color[deathTex.Width * deathTex.Height];
                            deathTex.GetData(pixelData);
                        }
                    }
                }

                Stand();
                return;
            }
            if (!Main.dedServ)
            {
                spawnParticle();
            }
            checkAnm();
            if (portalTime > 0)
            {
                animation = 1;
                portalTime--;
                if (portalTime <= 0)
                {
                    animation = 0;
                }
            }
            if (animation == 0 && gatherWing <= 0)
            {
                updateWingAnm();
            }
            if (portalTime > 0)
            {
                if (portal)
                {
                    NPC.velocity.X *= 0;
                    NPC.rotation = 0;
                    NPC.velocity.Y += 0.5f;
                    if (NPC.Center.Y > portalPos.Y - 120 && NPC.velocity.Y > 0)
                    {
                        alphaPor -= 0.1f;
                        if (alphaPor <= 0)
                        {
                            alphaPor = 0;
                            portal = false;
                            NPC.Center = portalTarget + new Vector2(0, 60);
                            NPC.velocity.Y *= -1;
                            NPC.netUpdate = true;
                            portalTime = 40;
                            if (NPC.netSpam >= 10)
                                NPC.netSpam = 9;
                        }
                    }
                }
                if (!portal)
                {
                    NPC.velocity *= 0.95f;
                }
            }
            if (!portal && alphaPor < 1)
            {
                alphaPor += 0.1f;
            }
            NPC.target = NPC.FindClosestPlayer();
            if (portalTime <= 0)
            {
                if (NPC.HasValidTarget)
                {
                    Player target = NPC.target.ToPlayer();
                    escape = 0;
                    if (NPC.Entropy().ToFriendly && NPC.Entropy().f_target == -1)
                    {
                        NPC.ai[2] = 0;
                        NPC.ai[3] = 10;
                        animation = 0;
                        KeepDist(200, 400);
                    }
                    if (NPC.ai[2] <= 0)
                    {
                        if (NPC.ai[3] > 0)
                        {
                            NPC.ai[3]--;
                            stayAtPlayerUp();
                        }
                        else
                        {
                            //选招:保留集重映射(§3.1);间歇下限抬到 50t(事件里小怪同场,单体压力让位)
                            int t = SkillPool[random.Next(SkillPool.Length)];
                            NPC.ai[3] = random.Next(50, 100);
                            NPC.netUpdate = true;
                            if (NPC.netSpam >= 10)
                                NPC.netSpam = 9;
                            NPC.ai[1] = t;
                            if (t == 0)
                            {
                                NPC.ai[2] = 80;
                            }
                            if (t == 1)
                            {
                                NPC.ai[2] = 90;
                            }
                            if (t == 4)
                            {
                                NPC.ai[2] = 80;
                            }
                            if (t == 6)
                            {
                                //削档:2 轮 ×12(原 4 轮 ×18 的 160);半速步进下 80 即 160t
                                NPC.ai[2] = 80;
                            }
                            if (t == 7)
                            {
                                NPC.ai[2] = 80;
                            }
                        }
                    }
                    else
                    {
                        if (NPC.ai[1] == 0)
                        {
                            Stand();
                            if (NPC.ai[2] == 80)
                            {
                                animation = 1;
                            }
                            if (NPC.ai[2] == 30)
                            {
                                animation = 0;
                            }
                            if (NPC.ai[2] == 4 || NPC.ai[2] == 18)
                            {
                                for (int i = 0; i < 5; i++)
                                {
                                    ThrowALightBall();
                                }
                            }
                        }
                        if (NPC.ai[1] == 1)
                        {
                            if (NPC.ai[2] == 90)
                            {
                                animation = 1;
                            }
                            if (NPC.ai[2] < 50)
                            {
                                Stand();
                                animation = 0;
                                wingFrame = 7;
                                anmChange = 0;
                            }
                            else
                            {
                                KeepDist(300);
                            }
                            if (addlight < 1)
                            {
                                addlight += 0.05f;
                            }

                            if (NPC.ai[2] > 12 && NPC.ai[2] < 50)
                            {
                                int c = (int)((50 - NPC.ai[2]) / 2f);
                                if (NPC.ai[2] % 9 == 0)
                                {
                                    if (Main.netMode != NetmodeID.MultiplayerClient)
                                    {
                                        float rot = -MathHelper.ToRadians(8f * (float)c / 2f);
                                        for (int i = 1; i <= c; i++)
                                        {
                                            Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, (target.Center - NPC.Center).SafeNormalize(Vector2.One).RotatedBy(rot) * 4, ModContent.ProjectileType<SighterPin>(), NPC.damage / 8, 4);
                                            rot += MathHelper.ToRadians(16);
                                        }
                                    }
                                }
                            }
                        }
                        if (NPC.ai[1] == 4)
                        {
                            Stand();
                            if (NPC.ai[2] == 80)
                            {
                                animation = 1;
                            }
                            if (NPC.ai[2] > 50)
                            {
                                if (addlight < 1)
                                {
                                    addlight += 0.05f;
                                }
                            }
                            if (NPC.ai[2] == 45)
                            {
                                animation = 0;
                            }
                            if (NPC.ai[2] < 40)
                            {
                                wingFrame = 0;
                                anmChange = 0;
                                float j = (float)Math.Cos(NPC.ai[2]) * 0.2f;
                                if (target.Center.X < NPC.Center.X)
                                {
                                    wingRotLeft += j;
                                }
                                else
                                {
                                    wingRotRight += j;
                                }
                            }
                            if (NPC.ai[2] < 30)
                            {
                                for (int i = 0; i < 2; i++)
                                {
                                    if (Main.netMode != NetmodeID.MultiplayerClient)
                                    {
                                        Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center + (target.Center.X > NPC.Center.X ? 1 : -1) * new Vector2(140 + Main.rand.Next(0, 60), 0) + new Vector2(0, -50), (target.Center - NPC.Center).SafeNormalize(Vector2.Zero) * 8 + new Vector2(9 * (Main.rand.NextFloat() - 0.5f), 6 * (Main.rand.NextFloat() - 0.5f)), ModContent.ProjectileType<VoidFeather>(), NPC.damage / 7, 6, -1, 0, NPC.whoAmI);
                                    }
                                }

                                //底本前车之鉴:声音资产只在客户端读取,服务器跳过
                                if (!Main.dedServ)
                                {
                                    SoundEffect se = ModContent.Request<SoundEffect>("CalamityEntropy/Assets/Sounds/feathershot").Value;
                                    if (se != null && NPC.ai[2] % 5 == 0) { se.Play(Main.soundVolume, 0, 0); }
                                }
                            }
                        }
                        if (NPC.ai[1] == 6)
                        {
                            KeepDist(1200);
                            NPC.ai[2] += 0.5f;
                            if (NPC.ai[2] == 80 || NPC.ai[2] == 40)
                            {
                                animation = 1;
                            }
                            if (NPC.ai[2] == 60 || NPC.ai[2] == 20)
                            {
                                animation = 0;
                                float a = CEUtils.randomRot();
                                for (int i = 0; i < 12; i++)
                                {
                                    a += MathHelper.ToRadians(30);
                                    if (Main.netMode != NetmodeID.MultiplayerClient)
                                    {
                                        Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, a.ToRotationVector2() * 48, ModContent.ProjectileType<VoidLightBall>(), (int)(NPC.damage * 0.14f), 6, -1, 0, 1, NPC.whoAmI);
                                    }
                                }
                            }
                        }
                        if (NPC.ai[1] == 7)
                        {
                            Stand();
                            if (NPC.ai[2] == 80)
                            {
                                animation = 1;
                            }
                            if (NPC.ai[2] == 30)
                            {
                                animation = 0;
                            }
                            if (NPC.ai[2] == 4 || NPC.ai[2] == 18)
                            {
                                for (int i = 0; i < 2; i++)
                                {
                                    ThrowALightBall(ModContent.ProjectileType<HomingLightBall>());
                                }
                            }
                        }
                        NPC.ai[2]--;
                    }

                    //传送门追人阈值 4000→1600(§3.1:事件场景更近身)
                    if (counter % 10 == 0 && CEUtils.getDistance(NPC.Center, target.Center) > 1600)
                    {
                        setPortalTo(target.Center + new Vector2(Main.rand.Next(-300, 301), 100));
                    }
                }
                else
                {
                    escape++;
                    NPC.velocity.Y -= 1;
                    NPC.velocity *= 0.98f;
                    animation = 0;
                    if (escape >= 160)
                    {
                        NPC.active = false;
                    }
                }
            }

            NPC.netUpdate = true;
            counter++;
        }

        private void Kill()
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                NPC.StrikeInstantKill();
                NPC.netSpam = 9;
                NPC.netUpdate = true;
            }
        }

        private void ThrowALightBall()
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, CEUtils.randomRot().ToRotationVector2() * (float)Main.rand.Next(100, 200) * 0.1f, ModContent.ProjectileType<VoidLightBall>(), (int)(NPC.damage * 0.14f), 6, -1, 0, 0, NPC.whoAmI);
            }
        }
        private void ThrowALightBall(int type)
        {
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, CEUtils.randomRot().ToRotationVector2() * (float)Main.rand.Next(100, 200) * 0.1f, type, (int)(NPC.damage * 0.14f), 6, -1, 0, 0, NPC.whoAmI);
            }
        }

        private void checkAnm()
        {
            if (animation == 0)
            {
                if (gatherWing > 0)
                {
                    gatherWing -= 0.05f;
                    if (gatherWing < 0)
                    {
                        gatherWing = 0;
                    }
                    wingFrame = 0;
                    anmChange = 0;
                }
                anmlerp = anmlerp + (1 - anmlerp) * 0.1f;
            }
            else if (animation == 1)
            {
                if (gatherWing < 1)
                {
                    gatherWing += 0.05f;
                    if (gatherWing >= 1)
                    {
                        SoundEngine.PlaySound(new SoundStyle("CalamityEntropy/Assets/Sounds/wingflap"), NPC.Center);
                        gatherWing = 1;
                    }
                }

                anmlerp = anmlerp + (0 - anmlerp) * 0.1f;
                wingFrame = 0;
                anmChange = 0;
            }
        }

        public bool portal = false;
        public int portalTime = 0;
        public Vector2 portalPos = Vector2.Zero;
        public Vector2 portalTarget = Vector2.Zero;
        public void setPortalTo(Vector2 targetpos)
        {
            portalTarget = targetpos;
            portal = true;
            portalTime = 360;
            portalPos = NPC.Center + new Vector2(0, 100);
            NPC.velocity = new Vector2(0, -16);
            NPC.netUpdate = true;
            if (NPC.netSpam >= 10)
                NPC.netSpam = 9;
        }
        private void updateWingAnm()
        {
            anmChange += 1.5f;
            if (wingFrame == 0 && anmChange >= 9)
            {
                anmChange = 0;
                wingFrame++;
            }
            if (wingFrame == 1 && anmChange >= 6)
            {
                anmChange = 0;
                wingFrame++;
            }
            if (wingFrame == 2 && anmChange >= 6)
            {
                anmChange = 0;
                wingFrame++;
            }
            if (wingFrame == 3 && anmChange >= 6)
            {
                anmChange = 0;
                wingFrame++;
            }
            if (wingFrame == 4 && anmChange >= 6)
            {
                anmChange = 0;
                wingFrame++;
            }
            if (wingFrame == 5 && anmChange >= 6)
            {
                anmChange = 0;
                wingFrame++;
            }
            if (wingFrame == 6 && anmChange >= 9)
            {
                anmChange = 0;
                wingFrame++;
            }
            if (wingFrame >= 7 && anmChange >= 12)
            {
                anmChange = 0;
                wingFrame = 0;
            }
        }

        public override void BossHeadRotation(ref float rotation)
        {
            rotation = NPC.rotation;
        }
        public void stayAtPlayerUp()
        {
            Player target = NPC.target.ToPlayer();
            Vector2 pos = target.Center - new Vector2(0, 120);
            if (CEUtils.getDistance(NPC.Center, pos) > 240 || NPC.velocity.Length() < 4)
            {
                NPC.velocity += (pos - NPC.Center).SafeNormalize(Vector2.Zero) * 0.4f;
                NPC.rotation = MathHelper.ToRadians(NPC.velocity.X * 1.4f);
                NPC.velocity *= 0.99f;
            }
        }
        public void Stand()
        {
            NPC.velocity *= 0.9f;
            NPC.rotation = MathHelper.ToRadians(NPC.velocity.X * 1.4f);
        }
        public void KeepDist(float dist, float maxDist = -1)
        {
            Player target = NPC.target.ToPlayer();
            if (maxDist >= 0 && CEUtils.getDistance(NPC.Center, target.Center) >= dist && CEUtils.getDistance(NPC.Center, target.Center) <= maxDist)
            {
                NPC.velocity *= 0.94f;
                return;
            }
            Vector2 pos = target.Center + (NPC.Center - target.Center).SafeNormalize(Vector2.One) * dist;
            NPC.velocity += (pos - NPC.Center).SafeNormalize(Vector2.Zero) * 1f;
            NPC.rotation = MathHelper.ToRadians(NPC.velocity.X * 1.4f);
            NPC.velocity *= 0.98f;
        }

        public void spawnParticle()
        {
            //每AI tick 2~3 HeavySmokeCal;主色调偏蓝紫,与深渊亡魂的紫红拉开(§3.1 染色区分)
            for (int i = 0; i < 2; i++)
            {
                Vector2 direction = new Vector2(0, 1).RotatedBy(NPC.rotation);
                Vector2 smokeSpeed = direction.RotatedByRandom(MathHelper.PiOver4 * 0.1f) * Main.rand.NextFloat(10f, 30f) * 0.9f;
                PRTLoader.NewParticle<PRT_HeavySmokeCal>(NPC.Center + direction * 46f, smokeSpeed + NPC.velocity, Color.Lerp(new Color(90, 70, 230), Color.Indigo, (float)Math.Sin(Main.GlobalTimeWrappedHourly * 6f)), Main.rand.NextFloat(0.6f, 1.2f)).Configure(0.8f, 30, 0, false, 0, true);

                //velocity叠加NPC.velocity别改符号,跟旧HeavySmoke trailing语义一致
                if (Main.rand.NextBool(3))
                {
                    PRTLoader.NewParticle<PRT_HeavySmokeCal>(NPC.Center + direction * 46f, smokeSpeed + NPC.velocity, Main.hslToRgb(0.72f, 1, 0.8f), Main.rand.NextFloat(0.4f, 0.7f)).Configure(0.8f, 20, 0.01f, true, 0.01f, true);
                }
            }
            //裂隙身份层:身周漂浮的空间碎晶,缓漂微旋(§3.1 独立身份)
            if (counter % 5 == 0)
            {
                Vector2 pos = NPC.Center + CEUtils.randomPointInCircle(130);
                PRTLoader.NewParticle<PRT_CrystalGlow>(pos, new Vector2(Main.rand.NextFloat(-0.6f, 0.6f), -Main.rand.NextFloat(0.4f, 1.4f)) + NPC.velocity * 0.4f,
                    new Color(140, 115, 255), Main.rand.NextFloat(0.28f, 0.55f)).Configure(0.7f, true, PRTDrawModeEnum.AdditiveBlend, CEUtils.randomRot(), 34);
            }
            //偶发裂隙细线一闪:空间被撕开的暗示
            if (counter % 17 == 0)
            {
                Vector2 pos = NPC.Center + CEUtils.randomPointInCircle(160);
                PRTLoader.NewParticle<PRT_LineCal>(pos, CEUtils.randomRot().ToRotationVector2() * 0.8f,
                    new Color(190, 170, 255), Main.rand.NextFloat(0.6f, 1.1f)).Configure(false, 12);
            }
        }

        public override bool CheckActive()
        {
            return false;
        }
        public float addlight = 1;

        /// <summary>
        /// 自绘入口(EffectLoader 特判调用,PreDraw 恒 false):
        /// RiftCrack 着色器统一承担蓝紫染色(顶点色真正生效,旧 aweffect 会忽略顶点色)
        /// + 体表裂隙纹流光 + 翼缘/体缘辉光 + 白化闪(a/alpha 语义沿用 aweffect)。
        /// 另:速度门控撕裂残影、仪式入场光柱、死亡扫描线的裂隙撕开边缘光。
        /// </summary>
        public void Draw()
        {
            Vector2 drawCenter = NPC.Center - new Vector2(0, 30);
            Texture2D body = TextureAssets.Npc[NPC.type].Value;
            SpriteBatch sb = Main.spriteBatch;
            sb.End();

            //仪式入场:阵心升起的光柱(加法,画在本体身后;spawnAnm 1→0 期间起落)
            if (spawnAnm > 0 && spawnSource == 1)
            {
                sb.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
                float pIn = spawnAnm / 120f;
                float pillar = (float)Math.Sin((1 - pIn) * MathHelper.Pi);
                Texture2D lb = CEExtraAssets.lightball;
                Vector2 pillarPos = NPC.Center - Main.screenPosition;
                sb.Draw(lb, pillarPos, null, new Color(120, 90, 255) * (0.75f * pillar), 0, lb.Size() / 2, new Vector2(1.1f, 9f), SpriteEffects.None, 0);
                sb.Draw(lb, pillarPos, null, Color.White * (0.5f * pillar), 0, lb.Size() / 2, new Vector2(0.4f, 8f), SpriteEffects.None, 0);
                sb.End();
            }

            sb.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);

            Effect fx = CEFxcEffects.Get("RiftCrack");
            fx.CurrentTechnique = fx.Techniques["Technique1"];
            fx.Parameters["time"].SetValue(Main.GlobalTimeWrappedHourly);
            fx.Parameters["noiseTex"].SetValue(CEExtraAssets.TurbulentNoise);
            fx.Parameters["crackColor"].SetValue(new Vector4(0.55f, 0.35f, 1.5f, 0f));
            fx.Parameters["rimColor"].SetValue(new Vector4(0.45f, 0.38f, 1.2f, 0f));
            //收翼施法与落定闪时裂纹更亮
            fx.Parameters["crackStrength"].SetValue(0.55f + gatherWing * 0.35f + landFlash * 0.6f);
            void ApplyFx(Texture2D tex, float alphaValue)
            {
                fx.Parameters["a"].SetValue(Math.Max(addlight, landFlash * 0.8f));
                fx.Parameters["alpha"].SetValue(alphaValue);
                fx.Parameters["pixel"].SetValue(new Vector2(1.5f / tex.Width, 1.5f / tex.Height));
                fx.CurrentTechnique.Passes[0].Apply();
            }

            float alpha = 1;
            if (spawnAnm > 0)
            {
                addlight = 1;
            }
            else
            {
                if (deathAnm)
                {
                    if (addlight < 1)
                    {
                        addlight += 0.02f;
                    }
                    anmlerp = anmlerp + (10 - anmlerp) * 0.01f;
                    Texture2D deathTex = awDeathTex.Value;

                    //剩余残躯:裂纹侵蚀 + 随进度加剧的横向撕裂抖动
                    float jitterX = Main.rand.NextFloat(-1f, 1f) * (1.5f + deathPer * 3f);
                    ApplyFx(deathTex, 1f);
                    sb.Draw(deathTex, NPC.Center - Main.screenPosition + new Vector2(jitterX, deathTex.Height / 2 * deathPer), new Rectangle(0, (int)(deathTex.Height * deathPer), deathTex.Width, (int)(deathTex.Height * (1 - deathPer))), RiftTint, 0, new Vector2(deathTex.Width / 2, deathTex.Height * (1 - deathPer) / 2), NPC.scale, SpriteEffects.None, 0);

                    //扫描线:裂隙撕开的边缘光(宽紫 + 窄白 + 沿线辉光),外加死亡起始的光球爆点
                    sb.End();
                    sb.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
                    float scanY = NPC.Center.Y + (deathTex.Height * deathPer - deathTex.Height / 2f) * NPC.scale;
                    float halfW = deathTex.Width / 2f * NPC.scale;
                    Vector2 scanL = new Vector2(NPC.Center.X - halfW, scanY);
                    Vector2 scanR = new Vector2(NPC.Center.X + halfW, scanY);
                    float flicker = 0.8f + 0.2f * (float)Math.Sin(counter * 0.7f);
                    CEUtils.drawLine(sb, CEExtraAssets.white, scanL, scanR, new Color(150, 110, 255) * (0.5f * flicker), 9);
                    CEUtils.drawLine(sb, CEExtraAssets.white, scanL, scanR, Color.White * (0.75f * flicker), 3);
                    Texture2D glowT = CEExtraAssets.Glow2;
                    sb.Draw(glowT, new Vector2(NPC.Center.X, scanY) - Main.screenPosition, null, new Color(160, 120, 255) * (0.4f * flicker), 0, glowT.Size() / 2, new Vector2(halfW * 2.2f / glowT.Width, 0.3f), SpriteEffects.None, 0);
                    Texture2D htxd = CEExtraAssets.lightball;
                    sb.Draw(htxd, NPC.Center - Main.screenPosition, null, RiftTint * NPC.Opacity, 0, htxd.Size() / 2, 1f * lbsize, SpriteEffects.None, 0);

                    sb.End();
                    sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
                    return;
                }
                else
                {
                    if (addlight > 0)
                    {
                        addlight -= 0.02f;
                    }
                }
            }

            if (spawnAnm > 20)
            {
                alpha = (140f - (float)spawnAnm) / 120f;
            }
            alpha *= NPC.Opacity;
            alpha *= alphaPor;

            //速度门控撕裂残影:冲刺/坠落时身后拉出裂隙色重影
            float spd = NPC.velocity.Length();
            if (spd > 8f && trailPos.Count > 1)
            {
                float ghostBase = MathHelper.Clamp((spd - 8f) / 14f, 0f, 1f) * 0.4f;
                for (int i = 0; i < trailPos.Count - 1; i += 2)
                {
                    float k = (i + 1f) / trailPos.Count;
                    ApplyFx(body, alpha * ghostBase * k);
                    sb.Draw(body, trailPos[i] - new Vector2(0, 30) - Main.screenPosition, null, new Color(120, 90, 255), NPC.rotation, body.Size() / 2, NPC.scale, SpriteEffects.None, 0f);
                }
            }

            if (gatherWing <= 0)
            {
                Texture2D wing = wingflying[wingFrame];

                float rot = 0;
                if (spawnAnm > 0)
                {
                    rot = MathHelper.ToRadians(spawnAnm * 6f);
                }
                if (spawnAnm <= 20)
                {
                    ApplyFx(wing, alpha);
                    Vector2 origin = new Vector2(320, 222);
                    sb.Draw(wing, drawCenter + new Vector2(-64, 0).RotatedBy(NPC.rotation) - Main.screenPosition, null, RiftTint, NPC.rotation - rot - wingRotLeft, origin, NPC.scale, SpriteEffects.None, 0);
                    origin = new Vector2(wing.Width - origin.X, origin.Y);
                    sb.Draw(wing, drawCenter + new Vector2(64, 0).RotatedBy(NPC.rotation) - Main.screenPosition, null, RiftTint, NPC.rotation + rot + wingRotRight, origin, NPC.scale, SpriteEffects.FlipHorizontally, 0);
                }
            }
            else
            {
                if (gatherWing <= 0.2f)
                {
                    float rot = MathHelper.ToRadians(gatherWing * 160);
                    Texture2D wing = wingflying[0];
                    ApplyFx(wing, alpha);
                    Vector2 origin = new Vector2(320, 222);
                    sb.Draw(wing, drawCenter + new Vector2(-64, 0).RotatedBy(NPC.rotation) - Main.screenPosition, null, RiftTint, NPC.rotation - rot, origin, NPC.scale, SpriteEffects.None, 0);
                    origin = new Vector2(wing.Width - origin.X, origin.Y);
                    sb.Draw(wing, drawCenter + new Vector2(64, 0).RotatedBy(NPC.rotation) - Main.screenPosition, null, RiftTint, NPC.rotation + rot, origin, NPC.scale, SpriteEffects.FlipHorizontally, 0);
                }
            }

            ApplyFx(body, alpha);
            sb.Draw(body, drawCenter - Main.screenPosition, null, RiftTint, NPC.rotation, body.Size() / 2, NPC.scale, SpriteEffects.None, 0f);
            if (gatherWing <= 0.2f)
            {

            }
            else if (gatherWing <= 0.5f)
            {
                float rot = 0;
                Texture2D wing = wingflying[6];
                ApplyFx(wing, alpha);
                Vector2 origin = new Vector2(320, 222);
                sb.Draw(wing, drawCenter + new Vector2(-64, 0).RotatedBy(NPC.rotation) - Main.screenPosition, null, RiftTint, NPC.rotation - rot, origin, NPC.scale, SpriteEffects.None, 0);
                origin = new Vector2(wing.Width - origin.X, origin.Y);
                sb.Draw(wing, drawCenter + new Vector2(64, 0).RotatedBy(NPC.rotation) - Main.screenPosition, null, RiftTint, NPC.rotation + rot, origin, NPC.scale, SpriteEffects.FlipHorizontally, 0);
            }
            else if (gatherWing <= 0.8f)
            {
                float rot = 0;
                Texture2D wing = wingGatheringTex.Value;
                ApplyFx(wing, alpha);
                Vector2 origin = new Vector2(320, 222);
                sb.Draw(wing, drawCenter + new Vector2(-64, 0).RotatedBy(NPC.rotation) - Main.screenPosition, null, RiftTint, NPC.rotation - rot, origin, NPC.scale, SpriteEffects.None, 0);
                origin = new Vector2(wing.Width - origin.X, origin.Y);
                sb.Draw(wing, drawCenter + new Vector2(64, 0).RotatedBy(NPC.rotation) - Main.screenPosition, null, RiftTint, NPC.rotation + rot, origin, NPC.scale, SpriteEffects.FlipHorizontally, 0);
            }
            else if (gatherWing <= 0.9f)
            {
                Texture2D wing = wingGatherTex.Value;
                ApplyFx(wing, alpha);
                Vector2 origin = wing.Size() / 2;
                origin.Y = 222;
                sb.Draw(wing, drawCenter - Main.screenPosition, null, RiftTint, NPC.rotation, origin, NPC.scale * new Vector2(1 - (gatherWing - 0.8f) * 10f * 0.5f, 1), SpriteEffects.None, 0);
            }
            else
            {
                Texture2D wing = wingGatherTex.Value;
                ApplyFx(wing, alpha);
                Vector2 origin = wing.Size() / 2;
                origin.Y = 222;
                sb.Draw(wing, drawCenter - Main.screenPosition, null, RiftTint, NPC.rotation, origin, NPC.scale * new Vector2(0.5f + (gatherWing - 0.9f) * 10f * 0.5f, 1), SpriteEffects.None, 0);
            }

            //收翼蓄势提示:翼聚拢时体心紫光渐亮(加法),给"要出招了"一个可读光量
            sb.End();
            sb.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
            if (gatherWing > 0 || landFlash > 0)
            {
                Texture2D glowC = CEExtraAssets.Glow2;
                float glowA = gatherWing * 0.45f + landFlash * 0.5f;
                sb.Draw(glowC, drawCenter - Main.screenPosition, null, new Color(140, 100, 255) * (glowA * alpha), 0, glowC.Size() / 2, 1.6f + gatherWing * 0.5f, SpriteEffects.None, 0);
            }

            sb.End();
            sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.AnisotropicClamp, DepthStencilState.None, RasterizerState.CullNone, null, Main.GameViewMatrix.TransformationMatrix);
        }
        public float lbsize = 0;
        public float lbj = 0;
        public float deathPer = 0;
        public override bool CanHitPlayer(Player target, ref int cooldownSlot)
        {
            return !deathAnm;
        }
        public override bool CheckDead()
        {
            if (deathPer >= 0.9f)
            {
                return true;
            }
            animation = 0;
            gatherWing = 0;
            NPC.damage = 0;
            NPC.life = 1;
            NPC.dontTakeDamage = true;
            NPC.active = true;

            NPC.netUpdate = true;

            if (NPC.netSpam >= 10)
                NPC.netSpam = 9;
            return false;
        }
        public void DrawPortal(Vector2 pos, Color color, float size, float xmul = 0.3f, float aj = 0)
        {
            Texture2D tx = soulVortexTex.Value;
            float angle = MathHelper.ToDegrees(counter * 0.2f + aj);
            Vector2 lu = new Vector2(size, 0).RotatedBy(MathHelper.ToRadians(angle - 135));
            Vector2 ru = new Vector2(size, 0).RotatedBy(MathHelper.ToRadians(angle - 45));
            Vector2 ld = new Vector2(size, 0).RotatedBy(MathHelper.ToRadians(angle + 135));
            Vector2 rd = new Vector2(size, 0).RotatedBy(MathHelper.ToRadians(angle + 45));

            lu.X *= xmul;
            ru.X *= xmul;
            ld.X *= xmul;
            rd.X *= xmul;

            Vector2 dp = pos - Main.screenPosition;
            float rangle = MathHelper.ToRadians(90);
            lu = lu.RotatedBy(rangle);
            ru = ru.RotatedBy(rangle);
            ld = ld.RotatedBy(rangle);
            rd = rd.RotatedBy(rangle);

            CEUtils.drawTextureToPoint(Main.spriteBatch, tx, color, dp + lu, dp + ru, dp + ld, dp + rd);
        }

        public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPosition, Color drawColor)
        {
            if (NPC.IsABestiaryIconDummy)
                return true;
            return false;
        }
        public bool deathAnm = false;

        public override void ModifyNPCLoot(NPCLoot npcLoot)
        {
            //§5.1 事件材料:魂髓 3~5 必掉(底本的深渊亡魂掉落不带过来)
            npcLoot.Add(ItemDropRule.Common(ModContent.ItemType<Items.WraithSoulEssence>(), 1, 3, 5));
        }
    }
}
