// BattleConfig.cs · roadmap step M2 (filled Nov 21, Milestone 2 group 1).
// Everything that defines one battle, and what came out of it (BattleResult).

using System.Collections.Generic;
using UnityEngine;
using Outpost.Content;

namespace Outpost.Sim
{
    /// <summary>
    /// Everything that defines one battle: mode, map, size, difficulty, leader, tower loadout and seed (later: mode
    /// rules, strains, mission). Create() is how the game makes a battle; only rules tests still call new Battle(map).
    /// The Skirmish setup screen fills one in, GameFlow keeps the last one, BattleSession turns it into a Battle.
    /// </summary>
    public class BattleConfig
    {
        public string mode = "skirmish";
        public string mapId = "dune_sea";
        /// <summary>small / medium / large (rts world SIZES). Only medium exists until the map generator (M3).</summary>
        public string size = "medium";
        public string difficulty = "Normal";
        public string leader = "vanguard";
        /// <summary>Player towers that may be built (null = the default loadout). The Bunker is always allowed.</summary>
        public List<string> loadout;
        /// <summary>Seeds UnityEngine.Random for the battle (AI choices); 0 = a new one is picked by Create.</summary>
        public int seed;

        public static readonly string[] Difficulties = { "Easy", "Normal", "Hard", "Legendary" };
        public static readonly string[] Sizes = { "small", "medium", "large" };

        /// <summary>The map sizes that have maps today (the generator brings the others in M3).</summary>
        public static bool SizeAvailable(string size) => size == "medium";

        public BattleConfig Clone()
        {
            var c = (BattleConfig)MemberwiseClone();
            c.loadout = loadout == null ? null : new List<string>(loadout);
            return c;
        }

        /// <summary>A Normal Skirmish on the first map with the default loadout (playing Battle.unity directly).</summary>
        public static BattleConfig Default() => new BattleConfig().Validated();

        /// <summary>Replaces anything unknown with a safe value (map, difficulty, size, leader) and cleans the loadout.</summary>
        public BattleConfig Validated()
        {
            var maps = MapBuilder.Available();
            if (maps.Length > 0 && System.Array.IndexOf(maps, mapId) < 0) mapId = maps[0];
            if (System.Array.IndexOf(Difficulties, difficulty) < 0) difficulty = "Normal";
            if (!SizeAvailable(size)) size = "medium";
            var ids = Battle.LeaderIds();
            if (!ids.Contains(leader)) leader = ids.Count > 0 ? ids[0] : "vanguard";
            loadout = TowerBehaviors.CleanLoadout(loadout ?? TowerBehaviors.DefaultLoadout());
            return this;
        }

        /// <summary>The map's display name (rts.json maps, else the exported map's own name or id).</summary>
        public string MapName => MapNameOf(mapId);

        public static string MapNameOf(string id) => GameData.S(GameData.Rts["maps"]?[id], "name", id);

        /// <summary>
        /// Makes the battle on an already loaded map: seed, leader, loadout, then StandardStart (bases, starting army,
        /// AI). fx is set first so the views see every spawn of the start; beforeStart runs just before StandardStart
        /// (BattleView binds the fog and map props there).
        /// </summary>
        public Battle Create(MapData map, IBattleFx fx = null, System.Action<Battle> beforeStart = null)
        {
            Validated();
            if (seed == 0) seed = Random.Range(1000, 100000);     // Python random.randint(1000, 99999)
            Random.InitState(seed);
            var b = new Battle(map) { fx = fx, config = this };
            b.SetLeader(leader);
            b.SetLoadout(loadout);
            beforeStart?.Invoke(b);
            b.StandardStart(difficulty);
            return b;
        }
    }

    /// <summary>How a battle ended, for the results screen and (from group 3 on) the per-map records.</summary>
    public class BattleResult
    {
        public BattleConfig config;
        /// <summary>player, alien, or null when the player left the battle from the pause menu.</summary>
        public string winner;
        public bool Won => winner == "player";
        public bool Left => winner == null;
        public float time;
        public int kills, losses, gathered, spent, bases;

        /// <summary>The numbers rts.py hands its end screen: kills, losses, gathered, spent, bases standing.</summary>
        public static BattleResult From(Battle b, BattleConfig cfg = null)
        {
            var P = b.Player;
            int standing = 0;
            foreach (var x in b.bases) if (x.Alive && x.faction == "player") standing++;
            return new BattleResult
            {
                config = cfg ?? b.config,
                winner = b.winner,
                time = b.time,
                kills = P.kills,
                losses = P.losses,
                gathered = (int)P.gathered,
                spent = (int)P.spent,
                bases = standing,
            };
        }
    }
}
