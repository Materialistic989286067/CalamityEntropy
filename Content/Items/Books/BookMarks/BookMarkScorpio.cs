using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace CalamityEntropy.Content.Items.Books.BookMarks
{
    public class BookMarkScorpio : BookMark
    {
        public override void SetDefaults() {
            base.SetDefaults();
            Item.rare = ItemRarityID.Orange;
            Item.Entropy().stroke = true;
            Item.Entropy().NameColor = Color.LightBlue;
            Item.Entropy().strokeColor = Color.DarkBlue;
            Item.Entropy().tooltipStyle = 4;
            Item.value = Item.buyPrice(gold: 1);
        }
        public override Texture2D UITexture => BookMark.GetUITexture("Scorpio");
        public override Color tooltipColor => Color.LightBlue;
        public override EBookProjectileEffect getEffect() {
            return new ScorpioBMEffect();
        }

    }
    public class ScorpioBMEffect : EBookProjectileEffect
    {
        public override void OnHitNPC(Projectile projectile, NPC target, int damageDone) {
            target.GetGlobalNPC<ScorpioEffectNPC>().effectLevel += 5;
        }
    }
    public class ScorpioEffectNPC : GlobalNPC
    {
        public override bool InstancePerEntity => true;
        public const int TickDamage = 60;
        public const float ArmorPen = 100f;
        public float effectLevel = 0;
        public override void AI(NPC npc) {
            if (effectLevel > 0) {
                effectLevel -= 0.002f;
                effectLevel *= 0.997f;
            }
            if (effectLevel < 0) {
                effectLevel = 0;
            }
            if (effectLevel > 0) {
                if (Main.GameUpdateCount % 30 == 0 && !npc.dontTakeDamage) {
                    NPC.HitModifiers hitMods = npc.GetIncomingStrikeModifiers(DamageClass.Magic, 0);
                    hitMods.ArmorPenetration += ArmorPen;
                    NPC.HitInfo hitInfo = hitMods.ToHitInfo(TickDamage, false, 0f);
                    hitInfo.HideCombatText = true;
                    CombatText.NewText(npc.getRect(), Color.DeepSkyBlue, npc.StrikeNPC(hitInfo));

                }
            }
        }
    }
}