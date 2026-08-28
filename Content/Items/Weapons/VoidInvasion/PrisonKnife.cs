using CalamityEntropy.Common;
using CalamityEntropy.Content.Particles;
using CalamityEntropy.Content.Particles.CalamityPorts;
using CalamityEntropy.Content.Rarities;
using CalamityEntropy.Core.Weapons;
using InnoVault;
using InnoVault.PRT;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Weapons.VoidInvasion
{
    /// <summary>
    /// 镇狱飞刀(void-invasion.md §5.4):教皇掉落的近战投掷武器(非消耗,rogue 再裁定惯例入近战)。
    /// 常态:掷出炼狱飞刀,穿透 2 目标后回收(回旋镖式);蓄势(充能条 5s)满 → 一次掷出
    /// 6 刀扇形,命中处小型虚空爆。
    /// 伤害定标:TheDeadCut 275@16t(深渊亡魂档投掷近战)↔ Silence 1750@32t(终局档投掷蓄势),
    /// 穿透 + 回程二段判定,取 360@18t 偏下成立。
    /// </summary>
    public class PrisonKnife : ModItem, ICEChargeWeapon
    {
        //充能条 5 秒;扇形 6 刀单刀乘数压到 0.75(数量换单发)
        public CEChargeProfile ChargeProfile => CEChargeProfile.ChargeBar(5f, 0.75f);

        public override void SetDefaults()
        {
            Item.width = 100;
            Item.height = 100;
            Item.damage = 360;
            Item.noMelee = true;
            Item.noUseGraphic = true;
            Item.useAnimation = Item.useTime = 18;
            Item.useStyle = ItemUseStyleID.Swing;
            Item.knockBack = 4.5f;
            Item.UseSound = SoundID.Item1;
            Item.autoReuse = true;
            Item.maxStack = 1;
            Item.value = Item.buyPrice(platinum: 2, gold: 20);
            Item.rare = ModContent.RarityType<VoidPurple>();
            Item.shoot = ModContent.ProjectileType<PurgatoryKnifeProj>();
            Item.shootSpeed = 19f;
            Item.DamageType = DamageClass.Melee;
        }

        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback)
        {
            if (CEChargeWeapon.TryConsume(player, Item))
            {
                //蓄势满:6 刀扇形(±25°),命中带小型虚空爆(弹幕侧 IsEmpowered 分支)
                for (int i = 0; i < 6; i++)
                {
                    float rot = MathHelper.Lerp(-0.44f, 0.44f, i / 5f);
                    int p = Projectile.NewProjectile(source, position, velocity.RotatedBy(rot), type, damage, knockback, player.whoAmI);
                    if (p.WithinBounds(Main.maxProjectiles))
                    {
                        CEChargeWeapon.Empower(p);
                    }
                }
                return false;
            }
            Projectile.NewProjectile(source, position, velocity, type, damage, knockback, player.whoAmI);
            return false;
        }
    }

    /// <summary>
    /// 炼狱飞刀弹幕:飞出穿透 2 目标(或 34t 飞行)后回收;回程仍有判定;
    /// 蓄势版命中处小型虚空爆。贴图刀尖朝上,绘制 +PiOver2 校向,飞行自旋。
    /// </summary>
    public class PurgatoryKnifeProj : ModProjectile
    {
        public override string Texture => "CalamityEntropy/Content/Projectiles/VoidInvasion/PurgatoryKnife";

        [VaultLoaden("CalamityEntropy/Content/Particles/PRT_Light")]
        private static Asset<Texture2D> glowTex;

        /// <summary>去程时长上限(之后自动回收)</summary>
        public const int OutTime = 34;
        /// <summary>去程穿透数(§5.4:穿透 2 目标回收)</summary>
        public const int PierceCount = 2;

        public bool Returning { get => Projectile.ai[0] == 1; set => Projectile.ai[0] = value ? 1 : 0; }
        private int hitCounter = 0;
        private float spin = 0;

        public override void SetDefaults()
        {
            Projectile.FriendlySetDefaults(DamageClass.Melee, false, -1);
            Projectile.width = 26;
            Projectile.height = 26;
            Projectile.timeLeft = 600;
            Projectile.localNPCHitCooldown = 20;
        }

        public override void AI()
        {
            Player player = Projectile.GetOwner();
            if (!Returning)
            {
                Projectile.ai[1]++;
                if (Projectile.ai[1] >= OutTime)
                {
                    Returning = true;
                    Projectile.netUpdate = true;
                }
                Projectile.velocity *= 0.985f;
            }
            else
            {
                //回程:强追玩家,靠近即回收
                Vector2 toOwner = player.MountedCenter - Projectile.Center;
                if (toOwner.Length() < 42f)
                {
                    Projectile.Kill();
                    return;
                }
                Projectile.velocity = Vector2.Lerp(Projectile.velocity, toOwner.SafeNormalize(Vector2.UnitX) * 24f, 0.14f);
            }
            spin += 0.42f * (Projectile.velocity.X >= 0 ? 1 : -1);
            Projectile.rotation = spin;
            Lighting.AddLight(Projectile.Center, 0.35f, 0.12f, 0.55f);
            if (!Main.dedServ && Main.rand.NextBool(4))
            {
                var v = PRTLoader.NewParticle<PRT_Void>(Projectile.Center, -Projectile.velocity * 0.1f, Color.White, 0.7f);
                v.Opacity = 0.4f;
            }
        }

        public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
        {
            EGlobalNPC.AddVoidTouch(target, 90, 2, 600, 20);
            if (Projectile.IsEmpowered())
            {
                //蓄势版:命中处小型虚空爆(所有者端结算)
                CEUtils.SpawnExplotionFriendly(Projectile.GetSource_FromAI(), Projectile.GetOwner(),
                    target.Center, (int)(Projectile.damage * 0.5f), 110, Projectile.DamageType);
                if (!Main.dedServ)
                {
                    PRTLoader.NewParticle<PRT_PulseRing>(target.Center, Vector2.Zero, new Color(150, 70, 230), 0.1f).Configure(0.9f, 10);
                    for (int i = 0; i < 8; i++)
                    {
                        var v = PRTLoader.NewParticle<PRT_Void>(target.Center,
                            CEUtils.randomRot().ToRotationVector2() * Main.rand.NextFloat(2f, 7f), Color.White, 0.9f);
                        v.Opacity = 0.5f;
                    }
                }
            }
            if (!Returning && ++hitCounter >= PierceCount)
            {
                Returning = true;
                Projectile.netUpdate = true;
            }
            if (!Main.dedServ)
            {
                SoundEngine.PlaySound(SoundID.Item71 with { Volume = 0.7f, Pitch = 0.3f }, target.Center);
            }
        }

        public override bool PreDraw(ref Color lightColor)
        {
            Texture2D tex = TextureAssets.Projectile[Projectile.type].Value;
            //速度门控残影
            CEUtils.DrawAfterimage(tex, Projectile.Entropy().odp, Projectile.Entropy().odr, 0.6f);
            Main.spriteBatch.Draw(tex, Projectile.Center - Main.screenPosition, null, lightColor, Projectile.rotation,
                tex.Size() / 2, Projectile.scale, SpriteEffects.None, 0);
            if (Projectile.IsEmpowered())
            {
                Main.spriteBatch.UseAdditive();
                Texture2D glow = glowTex.Value;
                Main.spriteBatch.Draw(glow, Projectile.Center - Main.screenPosition, null,
                    new Color(190, 90, 255) * 0.55f, 0, glow.Size() / 2, 0.45f, SpriteEffects.None, 0);
                CEUtils.ReSetToEndShader();
            }
            return false;
        }
    }
}
