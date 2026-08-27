using CalamityEntropy.Content.NPCs;
using System;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Common
{
    //熵灾模式对原版Boss的强化(灾厄Boss分支已随脱离灾厄整体裁撤)
    public class EntropyModeGNPC : GlobalNPC
    {
        public override bool InstancePerEntity => true;

        public override void PostAI(NPC npc)
        {
            if (CalamityEntropy.EntropyMode)
            {
                if (npc.type == NPCID.WallofFleshEye)
                {
                    if (npc.Entropy().counter % 400 < 60 && npc.Entropy().counter % 6 == 0)
                    {
                        Vector2 lookAt = Main.player[npc.target].Center + Main.player[npc.target].velocity * 10;

                        float velocity = 26;
                        int projectileType = ProjectileID.EyeLaser;
                        int damage = npc.GetProjectileDamage(projectileType);
                        if (Main.netMode != NetmodeID.MultiplayerClient)
                        {
                            {
                                Vector2 projectileVelocity = (lookAt - npc.Center).SafeNormalize(Vector2.UnitY).RotatedBy(MathHelper.ToRadians(10)) * velocity;
                                Vector2 projectileSpawn = npc.Center + projectileVelocity.SafeNormalize(Vector2.UnitY) * 150f;

                                int proj = Projectile.NewProjectile(npc.GetSource_FromAI(), projectileSpawn, projectileVelocity, projectileType, damage, 0f, Main.myPlayer, 1f, 0f);
                                Main.projectile[proj].timeLeft = 900;

                                Main.projectile[proj].tileCollide = false;
                            }
                            {
                                Vector2 projectileVelocity = (lookAt - npc.Center).SafeNormalize(Vector2.UnitY).RotatedBy(MathHelper.ToRadians(-10)) * velocity;
                                Vector2 projectileSpawn = npc.Center + projectileVelocity.SafeNormalize(Vector2.UnitY) * 150f;

                                int proj = Projectile.NewProjectile(npc.GetSource_FromAI(), projectileSpawn, projectileVelocity, projectileType, damage, 0f, Main.myPlayer, 1f, 0f);
                                Main.projectile[proj].timeLeft = 900;

                                Main.projectile[proj].tileCollide = false;
                            }
                        }
                    }
                }
                if (npc.type == NPCID.EyeofCthulhu)
                {
                    if (init)
                    {
                        npc.scale *= 1.4f;
                    }
                }
                if (npc.type == NPCID.PlanterasHook)
                {
                    if (Main.GameUpdateCount % 40 == 0 && Main.rand.NextBool(2))
                    {
                        Projectile.NewProjectile(npc.GetSource_FromAI(), npc.Center, (NPC.plantBoss.ToNPC().target.ToPlayer().Center - npc.Center).normalize() * 22, ProjectileID.SeedPlantera, (int)(npc.GetProjectileDamage(ProjectileID.SeedPlantera) * 0.6f), 2, Main.myPlayer);
                    }
                }
                if (npc.type == NPCID.KingSlime)
                {
                    npc.MaxFallSpeedMultiplier *= 36f;

                    if (this.ksFlag && npc.velocity.Y != 0f && npc.velocity.Y < 0f)
                    {
                        this.ksFlag2 = false;
                        npc.velocity.Y = npc.velocity.Y * 3f;
                        npc.velocity.X = npc.velocity.X * 1.4f;
                        if (Utils.NextBool(Main.rand, 3))
                        {
                            npc.velocity.Y = npc.velocity.Y * 1.4f;
                            this.ksFlag2 = true;
                        }
                    }
                    if (npc.velocity.X != 0f && npc.velocity.Y != 0f && this.ksFlag2 && Math.Sign(npc.velocity.X) != Math.Sign(npc.target.ToPlayer().Center.X - npc.Center.X))
                    {
                        npc.velocity.X = npc.velocity.X * 0.1f;
                        npc.velocity.Y = -4f;
                        this.ksFlag2 = false;
                    }
                    if (!this.ksFlag && npc.velocity.Y == 0f && !Main.dedServ)
                    {
                        CEUtils.PlaySound("ksLand", 1f, new Vector2?(npc.Center), 2, 1f);
                        //脱离灾厄:原灾厄GeneralScreenShakePower距离衰减震屏(1000~2000px内0~12),改用自有等价
                        CEUtils.SetShake(npc.Center, 12f, 2000f);
                    }
                    this.ksFlag = (npc.velocity.Y == 0f);
                    if (npc.velocity.Y == 0f)
                    {
                        this.vyAdd = 0f;
                    }
                    if (npc.velocity.Y != 0f)
                    {
                        this.vyAdd = 0.65f;
                        if (this.ksFlag2)
                        {
                            this.vyAdd = 0.4f;
                        }
                        npc.velocity.Y = npc.velocity.Y + this.vyAdd;
                    }
                    if (SpawnAtHalfLife)
                    {
                        SpawnAtHalfLife = false;
                        Vector2 vector = npc.Center + new Vector2(0, -40 * npc.scale);
                        if (Main.netMode != NetmodeID.MultiplayerClient)
                            NPC.NewNPC(npc.GetSource_FromAI(), (int)vector.X, (int)vector.Y, ModContent.NPCType<TopazJewel>());

                    }
                }
            }
            init = false;
        }

        public bool SpawnAtHalfLife = true;

        public bool ksFlag;

        public float vyAdd;

        public bool init = true;

        public bool ksFlag2;
    }
}
