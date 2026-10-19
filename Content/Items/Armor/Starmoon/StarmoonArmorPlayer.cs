using Microsoft.Xna.Framework.Graphics;
using System;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Armor.Starmoon
{
    public class StarmoonArmorPlayer : ModPlayer
    {
        public const int CrossBurstCooldown = 180;
        public const int CrossBurstBaseDamage = 1800;
        public const float MarkRange = 100 * 16;
        public const int StarlightLockTime = 90;

        public bool MagicSet;
        public bool RangedSet;
        public int CrossBurstTimer;
        /// <summary>月亮标记的 NPC 下标,只在本机玩家上维护,-1 为无</summary>
        public int MarkedNPC = -1;

        public override void ResetEffects() {
            MagicSet = false;
            RangedSet = false;
        }

        public override void PostUpdate() {
            if (CrossBurstTimer > 0) {
                CrossBurstTimer--;
            }
            if (Player.whoAmI != Main.myPlayer) {
                return;
            }
            if (!RangedSet) {
                MarkedNPC = -1;
                return;
            }
            if (Main.GameUpdateCount % 10 == 0 || !IsMarkable(MarkedNPC)) {
                MarkedNPC = FindNearestTarget();
            }
        }

        private bool IsMarkable(int index) {
            return index >= 0 && index < Main.maxNPCs && Main.npc[index].CanBeChasedBy()
                && Main.npc[index].DistanceSQ(Player.Center) <= MarkRange * MarkRange;
        }

        private int FindNearestTarget() {
            int best = -1;
            float bestDist = MarkRange * MarkRange;
            foreach (NPC npc in Main.ActiveNPCs) {
                if (!npc.CanBeChasedBy()) {
                    continue;
                }
                float dist = npc.DistanceSQ(Player.Center);
                if (dist < bestDist) {
                    bestDist = dist;
                    best = npc.whoAmI;
                }
            }
            return best;
        }

        public override void OnHitNPCWithItem(Item item, NPC target, NPC.HitInfo hit, int damageDone) {
            OnAnyHit(target, hit);
        }

        public override void OnHitNPCWithProj(Projectile proj, NPC target, NPC.HitInfo hit, int damageDone) {
            if (proj.type == ModContent.ProjectileType<StarCrossBurst>()) {
                return;
            }
            OnAnyHit(target, hit);
        }

        private void OnAnyHit(NPC target, NPC.HitInfo hit) {
            if (Player.whoAmI != Main.myPlayer) {
                return;
            }
            if (MagicSet && hit.Crit && CrossBurstTimer <= 0 && hit.DamageType.CountsAsClass(DamageClass.Magic)) {
                CrossBurstTimer = CrossBurstCooldown;
                int damage = (int)Player.GetTotalDamage(DamageClass.Magic).ApplyTo(CrossBurstBaseDamage);
                Projectile.NewProjectile(Player.GetSource_FromThis(), target.Center, Vector2.Zero, ModContent.ProjectileType<StarCrossBurst>(),
                    damage, 0f, Player.whoAmI, 0f, Main.rand.NextFloat(-0.25f, 0.25f));
            }
            if (RangedSet && target.whoAmI == MarkedNPC && hit.DamageType.CountsAsClass(DamageClass.Ranged)) {
                Player.AddBuff(ModContent.BuffType<StarlightLock>(), StarlightLockTime);
            }
        }

        public override bool CanConsumeAmmo(Item weapon, Item ammo) {
            return !(RangedSet && Main.rand.NextFloat() < 0.25f);
        }

        /// <summary>自己射出的远程爆炸物(火箭、榴弹)炸到自己时免疫</summary>
        public override bool ImmuneTo(PlayerDeathReason damageSource, int cooldownCounter, bool dodgeable) {
            if (!RangedSet || damageSource.SourcePlayerIndex != Player.whoAmI) {
                return false;
            }
            int index = damageSource.SourceProjectileLocalIndex;
            if (index < 0 || index >= Main.maxProjectiles) {
                return false;
            }
            Projectile proj = Main.projectile[index];
            return proj.active && proj.owner == Player.whoAmI && proj.DamageType.CountsAsClass(DamageClass.Ranged)
                && (proj.aiStyle == ProjAIStyleID.Explosive || ProjectileID.Sets.Explosive[proj.type]);
        }
    }

    public class StarlightLock : ModBuff
    {
        public override void SetStaticDefaults() {
            Main.buffNoSave[Type] = true;
        }

        public override void Update(Player player, ref int buffIndex) {
            player.GetDamage(DamageClass.Ranged) += 0.2f;
            player.GetCritChance(DamageClass.Ranged) += 20;
        }
    }

    /// <summary>星月射手套的箭矢不下坠,各端按拥有者的套装状态各自压住重力计时</summary>
    public class StarmoonArrowGlobalProjectile : GlobalProjectile
    {
        public override bool PreAI(Projectile projectile) {
            //原版箭矢 ai[0] 是重力计时,到 15 开始下坠
            if (projectile.arrow && projectile.friendly && projectile.aiStyle == ProjAIStyleID.Arrow && projectile.ai[0] > 1f
                && projectile.owner >= 0 && projectile.owner < Main.maxPlayers) {
                Player owner = Main.player[projectile.owner];
                if (owner.active && owner.GetModPlayer<StarmoonArmorPlayer>().RangedSet) {
                    projectile.ai[0] = 1f;
                }
            }
            return true;
        }
    }

    /// <summary>月亮标记只画给本机玩家看</summary>
    public class StarmoonMarkGlobalNPC : GlobalNPC
    {
        public override void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) {
            StarmoonArmorPlayer starmoon = Main.LocalPlayer.GetModPlayer<StarmoonArmorPlayer>();
            if (!starmoon.RangedSet || starmoon.MarkedNPC != npc.whoAmI) {
                return;
            }
            Texture2D mark = CEUtils.RequestTex("CalamityEntropy/Content/Items/Armor/Starmoon/StarmoonMark");
            Texture2D soft = CEUtils.getExtraTex("Glow");
            float time = Main.GlobalTimeWrappedHourly;
            bool locked = Main.LocalPlayer.HasBuff<StarlightLock>();
            Vector2 pos = new Vector2(npc.Center.X, npc.Top.Y - 26f + MathF.Sin(time * 3f) * 3f + npc.gfxOffY) - screenPos;
            float glow = locked ? 0.75f : 0.4f;
            spriteBatch.Draw(soft, pos, null, (new Color(110, 140, 255) * glow) with { A = 0 }, 0f, soft.Size() / 2f, 0.28f, SpriteEffects.None, 0f);
            spriteBatch.Draw(mark, pos, null, Color.White * 0.95f, 0f, mark.Size() / 2f, 1f, SpriteEffects.None, 0f);
        }
    }
}
