using System.Collections.Generic;
using UnityEngine;
using Outpost.Content;
using Outpost.Sim;

namespace Outpost.View
{
    /// <summary>
    /// The things on the map that are not units or buildings: expansion pads, the platform under
    /// every base, relays (their look follows their owner) and supply crates. It watches the battle
    /// a few times a second and shows what is there; it never changes the battle.
    /// </summary>
    public class MapProps : MonoBehaviour
    {
        Battle battle;
        MapData map;
        readonly List<GameObject> pads = new List<GameObject>();
        readonly List<GameObject> aprons = new List<GameObject>();
        readonly List<Base> apronOf = new List<Base>();
        readonly List<GameObject[]> relays = new List<GameObject[]>();     // none / player / alien
        readonly List<GameObject> crates = new List<GameObject>();
        float t;

        public void Bind(Battle b, MapData m)
        {
            battle = b;
            map = m;
            foreach (var s in map.sites)
            {
                pads.Add(Prop("bld_site", s.x, s.y));
                aprons.Add(null);
                apronOf.Add(null);
            }
            foreach (var r in battle.relays)
                relays.Add(new[] { Prop("bld_relay_none", r.x, r.y), Prop("bld_relay_player", r.x, r.y), Prop("bld_relay_alien", r.x, r.y) });
            foreach (var c in battle.crates) crates.Add(Prop("bld_crate", c.x, c.y));
            Refresh();
        }

        GameObject Prop(string model, float x, float y)
        {
            var go = ModelLibrary.Spawn(model, transform);
            if (go != null) go.transform.position = MapBuilder.Ground(map, x, y);
            return go;
        }

        void Update()
        {
            if (battle == null) return;
            t -= Time.deltaTime;
            if (t > 0) return;
            t = 0.2f;
            Refresh();
        }

        /// <summary>Shows what is there. Pads, relays, crates and enemy base platforms only appear once the player has
        /// explored their spot (render.py draws them only where world.explored).</summary>
        public void Refresh()
        {
            for (int i = 0; i < map.sites.Length; i++)
            {
                var s = map.sites[i];
                var b = battle.BaseAt(s);
                bool seen = battle.Explored("player", s.x, s.y);
                if (pads[i] != null && pads[i].activeSelf != (b == null && seen)) pads[i].SetActive(b == null && seen);
                if (b != apronOf[i])
                {
                    if (aprons[i] != null) Destroy(aprons[i]);
                    aprons[i] = b != null ? BaseApron.Build(map, s, b.faction == "player", transform) : null;
                    apronOf[i] = b;
                }
                bool apronOn = b != null && (b.faction == "player" || seen);
                if (aprons[i] != null && aprons[i].activeSelf != apronOn) aprons[i].SetActive(apronOn);
            }
            for (int i = 0; i < relays.Count; i++)
            {
                var r = battle.relays[i];
                string owner = r.owner;
                int show = !battle.Explored("player", r.x, r.y) ? -1 : owner == "player" ? 1 : owner == "alien" ? 2 : 0;
                for (int k = 0; k < 3; k++)
                    if (relays[i][k] != null && relays[i][k].activeSelf != (k == show)) relays[i][k].SetActive(k == show);
            }
            for (int i = 0; i < crates.Count; i++)
            {
                var c = battle.crates[i];
                bool on = c.alive && battle.Explored("player", c.x, c.y);
                if (crates[i] != null && crates[i].activeSelf != on) crates[i].SetActive(on);
            }
        }
    }
}
