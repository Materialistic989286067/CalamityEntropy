using CalamityEntropy.Content.Buffs;
using CalamityEntropy.Content.Projectiles;
using CalamityEntropy.Content.Rarities;
using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Weapons.Whips
{
    public class Vitalfeather : ModItem
    {
        public override LocalizedText Tooltip => base.Tooltip.WithFormatArgs(DragonWhipDebuff.TagDamage);

        public override void SetDefaults() {
            Item.DefaultToWhip(ModContent.ProjectileType<VitalfeatherProjectile>(), 200, 2, 4, 33);
            Item.rare = ModContent.RarityType<Golden>();
            Item.value = Item.buyPrice(platinum: 2, gold: 40);
            Item.autoReuse = true;
        }

        public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockback) {
            int p = Projectile.NewProjectile(source, position, velocity, type, damage, knockback, player.whoAmI);

            return false;
        }

        public override bool MeleePrefix() {
            return true;
        }

        public override void ModifyTooltips(List<TooltipLine> tooltips) {
            //掉落规则在 VDAftermathGlobalNPC
            tooltips.Replace("[OBT]", Mod.GetLocalization("VitalfeatherObt").Value);
        }
    }
}
