using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Outpost.Sim
{
    /// <summary>
    /// The alien faction's commander, ported from game/rts/ai.py. It plays by the same rules as the
    /// player (Biomass income, base slots, tech, pop cap) through the Battle's public methods;
    /// difficulty only changes its income, army strength and attack timing (rts.json "ai").
    ///
    /// Every second it: works through a list of purchases (build order, Spines, Deep Pools, Elder
    /// Hive) and sets part of its income aside for the next one while the rest keeps going into the
    /// army, keeps its production buildings busy with units that counter the player's army, defends
    /// its base and launches attack waves once the idle army is worth enough.
    /// </summary>
    public partial class AlienAI
    {
        readonly Battle battle;
        readonly string[] buildOrder;
        readonly float attackEvery, armyMult;
        public float nextAttack;
        public int wave;
        /// <summary>Biomass set aside for the next purchase, and what that purchase is (for debugging).</summary>
        public float reserve;
        public string saving;
        float fund;                      // savings carried from one think to the next
        float thinkT = 2f;
        bool queenCalled;
        readonly float expandAt;
        readonly HashSet<int> attackers = new HashSet<int>();
        readonly HashSet<int> raiders = new HashSet<int>();          // units sent to take relays
        readonly HashSet<int> escorts = new HashSet<int>();          // units holding the next expansion site
        bool scouted;
        float nextRaid;
        readonly float passiveUntil;
        /// <summary>How many scouts and raids the AI has sent (headless tests).</summary>
        public int scoutsSent, raidsSent;

        public AlienAI(Battle b, string difficulty = "Normal")
        {
            battle = b;
            var ai = GameData.Rts["ai"];
            buildOrder = GameData.List(ai, "build_order");
            var d = ai["difficulties"]?[difficulty];
            battle.Alien.incomeMult = GameData.F(d, "income", 1f);
            armyMult = GameData.F(d, "army_mult", 1f);
            battle.alienArmyMult = armyMult;
            attackEvery = GameData.F(d, "attack_every", 120);
            nextAttack = GameData.F(d, "first_attack", 220);
            expandAt = GameData.F(d, "expand_at", 270);
            nextRaid = GameData.F(d, "income", 1f) >= 1f ? 330f : 420f;
            passiveUntil = GameData.F(d, "passive_until", 0);
            powerStart = Mathf.Max(GameData.F(d, "first_attack", 220) + 120, GameData.F(d, "passive_until", 0));
        }

        /// <summary>The Swarm's living bases, the oldest (its main Hive) first.</summary>
        List<Base> Bases() => battle.bases.Where(b => b.faction == "alien" && b.Alive).ToList();

        Base Home => battle.bases.FirstOrDefault(b => b.faction == "alien" && b.Alive);

        /// <summary>The player base to hit: the closest, weakest-defended one.</summary>
        Base TargetBase(Base home)
        {
            Base best = null;
            float bs = float.MaxValue;
            foreach (var b in battle.bases)
            {
                if (!b.Alive || b.faction != "player") continue;
                float score = Vector2.Distance(new Vector2(b.x, b.y), new Vector2(home.x, home.y))
                              + b.sockets.Count(t => t != null) * 250 - (b.site.main == null ? 600 : 0);   // expansions are softer
                if (score < bs) { bs = score; best = b; }
            }
            return best;
        }

        public void OnUnit(Unit u) { }

        List<Unit> Army() => battle.units.Where(u => u.alive && u.faction == "alien" && u.lifetime < 0 &&
                                                     u.kind != "queen" && u.kind != "architect").ToList();

        public void Tick(float dt)
        {
            thinkT -= dt;
            if (thinkT > 0) return;
            thinkT = 1f;
            var bases = Bases();
            if (bases.Count == 0) return;
            var home = bases[0];
            var f = battle.Alien;
            var army = Army();
            bool threat = bases.Any(bs => battle.units.Any(u => u.alive && u.faction == "player" &&
                                                                (u.Pos - new Vector2(bs.x, bs.y)).sqrMagnitude < 450 * 450));
            Economy(bases, f, army, threat);
            Produce(f, army, threat);
            Research(f);
            Defend(bases, army);
            ManageArmy(home, f, army);
            ScoutAndRaid(army);
            Relays(home, army);
            Escort(bases, army);
            CastPowers(bases, army);
            if (f.tech >= 3 && !queenCalled && f.hero == null)
            {
                queenCalled = true;
                f.heroRespawn = 1f;
            }
        }
    }
}
