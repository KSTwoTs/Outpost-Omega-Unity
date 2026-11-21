using UnityEngine;
using UnityEngine.UIElements;
using Outpost.Sim;

namespace Outpost.UI
{
    /// <summary>Top-left bar: Supplies and income, population, tech, the clock, the next alien attack, Commander respawn.</summary>
    public class ResourceBar
    {
        public readonly VisualElement root;
        readonly Label supplies, pop, tech, clock, attack, hero, speedText;

        public ResourceBar()
        {
            root = Ui.Box("op-panel op-resbar", true);
            root.Add(supplies = Ui.Text("", "op-res op-res--gold"));
            root.Add(pop = Ui.Text("", "op-res op-res--cyan"));
            root.Add(tech = Ui.Text("", "op-res op-res--plain"));
            root.Add(clock = Ui.Text("", "op-res op-res--plain"));
            root.Add(attack = Ui.Text("", "op-res op-res--bad"));
            root.Add(hero = Ui.Text("", "op-res op-res--gold"));
            root.Add(speedText = Ui.Text("", "op-res op-res--bad"));
        }

        /// <summary>speed: the game speed (Tab); shown as x1.5 / x2 when not x1 (Python draws it by the clock).</summary>
        public void Refresh(Battle b, float speed = 1f)
        {
            Ui.Show(speedText, speed != 1f);
            if (speed != 1f) speedText.text = "x" + speed.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            var f = b.Player;
            supplies.text = $"Supplies {(int)f.res}  <size=14>(+{f.income:0.0}/s)</size>";
            pop.text = $"Pop {f.popUsed}/{f.popCap}";
            tech.text = $"Tech {f.tech}";
            clock.text = Ui.Clock(b.time);
            Ui.Show(attack, b.ai != null);
            if (b.ai != null) attack.text = $"Next attack {Ui.Clock(Mathf.Max(0, b.ai.nextAttack - b.time))}";
            bool down = f.hero == null && f.heroRespawn > 0;
            Ui.Show(hero, down);
            if (down) hero.text = $"Commander back in {Ui.Secs(f.heroRespawn)}";
        }
    }
}
