using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Outpost.Sim
{
    /// <summary>The alien AI's army: base defense, relay raiders, expansion escorts, attack waves and the Queen.</summary>
    public partial class AlienAI
    {
        // ------------------------------------------------------------------ army
        void Defend(List<Base> bases, List<Unit> army)
        {
            foreach (var bs in bases)
            {
                var at = new Vector2(bs.x, bs.y);
                float tx = 0, ty = 0;
                int n = 0;
                foreach (var u in battle.units)
                    if (u.alive && u.faction == "player" && (u.Pos - at).sqrMagnitude < 420 * 420)
                    {
                        tx += u.x;
                        ty += u.y;
                        n++;
                    }
                if (n == 0) continue;
                var near = army.Where(u => !attackers.Contains(u.id) && (u.target == null || u.order != Order.Attack) &&
                                           (u.Pos - at).sqrMagnitude < 1400 * 1400).ToList();
                if (near.Count > 0) battle.OrderMove(near, new Vector2(tx / n, ty / n), true);
                return;
            }
        }

        /// <summary>Keeps up to two light units out taking relays the Swarm doesn't hold.</summary>
        void Relays(Base home, List<Unit> army)
        {
            if (battle.time < 60) return;
            raiders.RemoveWhere(id => !army.Any(u => u.id == id));
            if (raiders.Count >= 2) return;
            var hp = new Vector2(home.x, home.y);
            var relay = battle.relays.Where(r => r.owner != "alien")
                .OrderBy(r => (new Vector2(r.x, r.y) - hp).sqrMagnitude).FirstOrDefault();
            if (relay == null) return;
            var at = new Vector2(relay.x, relay.y);
            var scout = army.Where(u => !attackers.Contains(u.id) && !raiders.Contains(u.id) && u.order == Order.None &&
                                        (u.cls == "inf" || u.cls == "air"))
                .OrderBy(u => (u.Pos - at).sqrMagnitude).FirstOrDefault();
            if (scout == null) return;
            battle.OrderMove(new[] { scout }, at, true);
            raiders.Add(scout.id);
        }

        /// <summary>When a new Hive is due, part of the army goes to clear and hold the site (wild nests guard some).</summary>
        void Escort(List<Base> bases, List<Unit> army)
        {
            var site = ExpansionDue(bases) ? NextSite(bases[0]) : null;
            if (site == null)
            {
                escorts.Clear();              // nothing to hold: they rejoin the army
                return;
            }
            escorts.RemoveWhere(id => !army.Any(u => u.id == id));
            var at = new Vector2(site.x, site.y);
            if (escorts.Count < 3)
                foreach (var u in army.Where(u => !attackers.Contains(u.id) && !raiders.Contains(u.id) && !escorts.Contains(u.id))
                             .OrderBy(u => (u.Pos - at).sqrMagnitude).Take(4 - escorts.Count))
                    escorts.Add(u.id);
            var away = army.Where(u => escorts.Contains(u.id) && u.order == Order.None && u.target == null &&
                                       (u.Pos - at).sqrMagnitude > 300 * 300).ToList();
            if (away.Count > 0) battle.OrderMove(away, at, true);
        }

        void ManageArmy(Base home, Faction f, List<Unit> army)
        {
            attackers.RemoveWhere(id => !army.Any(u => u.id == id));
            var idle = army.Where(u => !attackers.Contains(u.id) && !raiders.Contains(u.id) && !escorts.Contains(u.id)).ToList();
            var target = TargetBase(home);
            var hp = new Vector2(home.x, home.y);
            var rally = target != null ? hp + (new Vector2(target.x, target.y) - hp).normalized * 300 : hp;

            // gather in front of the Hive, facing the player
            var strays = idle.Where(u => u.order == Order.None && u.target == null && (u.Pos - rally).sqrMagnitude > 260 * 260).ToList();
            if (strays.Count > 0) battle.OrderMove(strays, rally, false);

            // attack once the timer is up and the idle army is worth enough (or it has waited 90 s more)
            float value = idle.Sum(u => GameData.F(u.data, "cost"));
            float need = (500 + 250 * wave) * armyMult;
            if (battle.time >= nextAttack && (value >= need || battle.time >= nextAttack + 90) && target != null && idle.Count > 0)
            {
                int keep = idle.Count > 6 ? 2 : 0;       // a couple stay home as a guard
                var tp = new Vector2(target.x, target.y);
                var group = idle.OrderBy(u => (u.Pos - tp).sqrMagnitude).Take(idle.Count - keep).ToList();
                battle.OrderMove(group, tp, true);
                foreach (var u in group)
                {
                    attackers.Add(u.id);
                    if (u.kind == "burrowers" && battle.AbilityReady(u) && (tp - u.Pos).magnitude < 900) battle.UseAbility(u, tp);
                }
                wave++;
                nextAttack = battle.time + attackEvery;
                battle.fx?.Message("alien", $"Alien attack wave {wave} incoming!");
            }

            // attackers that went idle keep pushing
            if (target != null)
            {
                var lost = army.Where(u => attackers.Contains(u.id) && u.order == Order.None && u.target == null).ToList();
                if (lost.Count > 0) battle.OrderMove(lost, new Vector2(target.x, target.y), true);
            }

            // the Queen leads from the front when there is an attack
            var q = f.hero;
            if (q != null && q.alive && q.order == Order.None && q.target == null)
            {
                if (attackers.Count > 0 && target != null) battle.OrderMove(new[] { q }, new Vector2(target.x, target.y), true);
                else if ((q.Pos - rally).sqrMagnitude > 260 * 260) battle.OrderMove(new[] { q }, rally, false);
            }
        }
    }
}
