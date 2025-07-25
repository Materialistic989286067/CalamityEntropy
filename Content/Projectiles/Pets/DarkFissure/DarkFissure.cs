﻿using CalamityEntropy.Content.Buffs.Pets;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Projectiles.Pets.DarkFissure
{
    public class DarkFissure : ModProjectile
    {
        public float counter = 0;
        public override void SetStaticDefaults()
        {
            Main.projFrames[Projectile.type] = 1;
            Main.projPet[Projectile.type] = true;
            base.SetStaticDefaults();

        }
        public override void SetDefaults()
        {
            Projectile.CloneDefaults(ProjectileID.ZephyrFish);
            Projectile.aiStyle = -1;
            Projectile.tileCollide = false;
            Projectile.width = 24;
            Projectile.height = 40;
        }
        public override bool PreDraw(ref Color lightColor)
        {
            bool hat = Projectile.owner.ToPlayer().Entropy().PetsHat;

            if (Main.gameMenu)
            {
                Texture2D txd = ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/DarkFissure").Value;
                Main.EntitySpriteDraw(txd, Projectile.Center - Main.screenPosition, null, lightColor, Projectile.rotation, new Vector2(txd.Width, txd.Height) / 2, Projectile.scale, SpriteEffects.FlipHorizontally, 0);

                return false;
            }
            Player player = Main.player[Projectile.owner];
            List<Texture2D> list = new List<Texture2D>();
            if (counter > 36)
            {
                counter -= 36;
            }
            if (Projectile.ai[1] == 1)
            {
                if (hat)
                {
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/s/DarkFissure").Value);
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/s/DarkFissure2").Value);
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/s/DarkFissure3").Value);
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/s/DarkFissure4").Value);
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/s/DarkFissure5").Value);
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/s/DarkFissure6").Value);

                }
                else
                {
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/DarkFissure").Value);
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/DarkFissure2").Value);
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/DarkFissure3").Value);
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/DarkFissure4").Value);
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/DarkFissure5").Value);
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/DarkFissure6").Value);

                }
            }
            else
            {
                if (hat)
                {
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/s/walk1").Value);
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/s/walk2").Value);
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/s/walk3").Value);
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/s/walk4").Value);
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/s/walk5").Value);
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/s/walk6").Value);

                }
                else
                {
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/walk1").Value);
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/walk2").Value);
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/walk3").Value);
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/walk4").Value);
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/walk5").Value);
                    list.Add(ModContent.Request<Texture2D>("CalamityEntropy/Content/Projectiles/Pets/DarkFissure/walk6").Value);
                }
            }
            Texture2D tx = list[(((int)counter / 6) % list.Count)];
            if (Projectile.velocity.X > -2 && Projectile.velocity.X < 2f)
            {
                if (player.Center.X > Projectile.Center.X)
                {
                    Projectile.direction = 1;
                }
                else
                {
                    Projectile.direction = -1;
                }
            }
            if (Projectile.direction == -1)
            {
                Main.EntitySpriteDraw(tx, Projectile.Center - Main.screenPosition, null, lightColor, Projectile.rotation, new Vector2(tx.Width, tx.Height) / 2, Projectile.scale, SpriteEffects.FlipHorizontally, 0);

            }
            else
            {
                Main.EntitySpriteDraw(tx, Projectile.Center - Main.screenPosition, null, lightColor, Projectile.rotation, new Vector2(tx.Width, tx.Height) / 2, Projectile.scale, SpriteEffects.None, 0);
            }


            return false;

        }
        void MoveToTarget(Vector2 targetPos)
        {
            if (CEUtils.getDistance(Projectile.Center, targetPos) > 1400)
            {
                Projectile.Center = Main.player[Projectile.owner].Center - new Vector2(0, 50);
            }
            if (Projectile.ai[1] == 1)
            {
                counter++;
                Projectile.tileCollide = false;
                Projectile.rotation = MathHelper.ToRadians((Projectile.velocity.X * 1.4f));
                if (CEUtils.getDistance(Projectile.Center, targetPos) > 90)
                {
                    Vector2 px = targetPos - Projectile.Center;
                    px.Normalize();
                    Projectile.velocity += px * 1.2f;

                    Projectile.velocity *= 0.96f;

                }
                if (Projectile.Center.Y < targetPos.Y - 16 && CEUtils.getDistance(Projectile.Center, targetPos) < 100 && !(CEUtils.isAir(Projectile.owner.ToPlayer().Center + new Vector2(0, Projectile.owner.ToPlayer().height / 2 + 2), true)))
                {
                    Projectile.ai[1] = 0;
                }
                if (Projectile.velocity.X > 0)
                {
                    Projectile.direction = 1;
                }
                else
                {
                    Projectile.direction = -1;
                }
            }
            else
            {
                if (Projectile.velocity.Y == 0)
                {
                    counter += Math.Abs(Projectile.velocity.X / 4);
                }
                Projectile.tileCollide = true;
                Projectile.rotation = 0;
                Projectile.velocity.Y += 0.5f;
                if (CEUtils.getDistance(targetPos, Projectile.Center) > 340 || (Math.Abs(targetPos.Y - Projectile.Center.Y) > 60 && Projectile.owner.ToPlayer().velocity.Y == 0))
                {
                    Projectile.ai[1] = 1;
                }
                else if (CEUtils.getDistance(targetPos * new Vector2(1, 0), Projectile.Center * new Vector2(1, 0)) > 120)
                {
                    if (targetPos.X > Projectile.Center.X)
                    {
                        Projectile.velocity.X += 1f;
                    }
                    else
                    {
                        Projectile.velocity.X -= 1f;
                    }
                    Projectile.velocity.X *= 0.95f;
                }
                else
                {
                    Projectile.velocity.X *= 0.9f;
                }
                if (targetPos.X > Projectile.Center.X)
                {
                    Projectile.direction = 1;
                }
                else
                {
                    Projectile.direction = -1;
                }

                if (Math.Abs(Projectile.velocity.X) > 0.3f && !CEUtils.isAir(Projectile.Center + (Projectile.velocity * new Vector2(1, 0)).SafeNormalize(Vector2.Zero) * 14 + new Vector2(0, 18)))
                {
                    Projectile.velocity.Y -= 1.5f;
                }
            }

        }
        public override bool PreAI()
        {
            Player player = Main.player[Projectile.owner];

            player.zephyrfish = false;
            return true;
        }

        public override void AI()
        {

            Player player = Main.player[Projectile.owner];
            MoveToTarget(player.Center + new Vector2(0, 0));
            if (!player.dead && (player.HasBuff(ModContent.BuffType<DevourerAndTheApostles>()) || player.HasBuff(ModContent.BuffType<WeakGravity>())))
            {
                Projectile.timeLeft = 2;
            }

        }


    }
}
