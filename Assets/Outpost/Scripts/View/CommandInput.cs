using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Outpost.Content;
using Outpost.Sim;

namespace Outpost.View
{
    /// <summary>
    /// RTS mouse / keyboard controls, matching the Python game:
    /// left click / drag select (Shift adds, double-click selects that type on screen), right click moves
    /// or attacks (sets the rally point with a building selected), A + left click attack-moves,
    /// F unit ability (aimed ones then take a left click), S stop, H hold, Q whole army, E army on screen,
    /// G Commander, Ctrl+1-5 / 1-5 groups, Esc cancels.
    /// </summary>
    public class CommandInput : MonoBehaviour
    {
        public Battle battle;
        public Fx fx;
        public Camera cam;
        public RtsCamera rig;
        public MapData map;

        public readonly List<Entity> selection = new List<Entity>();
        public bool attackMode, abilityMode;
        public MapData.SiteData selectedSite;                      // a free expansion pad the player clicked
        public Building scanFrom;                                  // Sensor Array waiting for a Scan target
        public readonly List<Rect> blockers = new List<Rect>();   // extra blocked rects (GUI coords, top-left origin)
        public System.Func<Vector2, bool> overUi;                  // set by the HUD: is this screen point on a panel?
        public string abilityOnly;                                 // aim mode limited to one ability id (HUD buttons)
        public bool dragging;
        public Vector2 dragStart, dragNow;                        // GUI coords
        public Entity hover;
        public string toast;
        public float toastT;

        readonly List<Entity>[] groups = new List<Entity>[10];
        float lastClickT, lastGroupT;
        Entity lastClicked;
        int lastGroup = -1;

        public IEnumerable<Unit> MyUnits => selection.OfType<Unit>().Where(u => u.alive && u.inBunker == null && u.faction == "player");

        public void Toast(string text)
        {
            toast = text;
            toastT = 2.5f;
        }

        static Vector2 Gui(Vector2 screen) => new Vector2(screen.x, Screen.height - screen.y);

        bool OverHud(Vector2 gui)
        {
            if (overUi != null && overUi(new Vector2(gui.x, Screen.height - gui.y))) return true;
            foreach (var r in blockers) if (r.Contains(gui)) return true;
            return false;
        }

        public bool GroundPoint(Vector2 screen, out Vector2 px)
        {
            px = default;
            if (cam == null) return false;
            if (!Physics.Raycast(cam.ScreenPointToRay(screen), out var hit, 5000f)) return false;
            px = new Vector2(hit.point.x / MapBuilder.Unit, -hit.point.z / MapBuilder.Unit);
            return true;
        }

        /// <summary>Screen position (pixels, bottom-left origin) and on-screen radius of an entity.</summary>
        public bool ScreenOf(Entity e, out Vector2 pos, out float r)
        {
            pos = default;
            r = 0;
            if (!(e.view is EntityView v) || v == null) return false;
            var wp = v.transform.position + Vector3.up * (e.flying ? 0.5f : 0.8f);
            var sp = cam.WorldToScreenPoint(wp);
            if (sp.z <= 0) return false;
            var edge = cam.WorldToScreenPoint(wp + cam.transform.right * e.radius * MapBuilder.Unit);
            pos = sp;
            r = Mathf.Max(10f, Vector2.Distance(sp, edge) * 1.15f);
            return true;
        }

        Entity Pick(Vector2 screen)
        {
            Entity best = null;
            float bd = float.MaxValue;
            foreach (var e in battle.All())
            {
                if (!e.alive || battle.HiddenFrom(e, "player") || !ScreenOf(e, out var p, out var r)) continue;
                float d = Vector2.Distance(p, screen);
                if (d > r) continue;
                float score = d / r + (e.IsUnit ? 0f : 0.6f);    // units win over the building behind them
                if (score < bd) { bd = score; best = e; }
            }
            return best;
        }

        public void Select(IEnumerable<Entity> list, bool add = false)
        {
            var items = list.ToList();
            if (!add)
            {
                foreach (var e in selection) if (e.view is EntityView v && v != null) v.SetSelected(false);
                selection.Clear();
            }
            foreach (var e in items)
                if (e.alive && !selection.Contains(e))
                {
                    selection.Add(e);
                    if (e.view is EntityView v && v != null) v.SetSelected(true);
                }
            attackMode = false;
            abilityMode = false;
            scanFrom = null;
            selectedSite = null;
        }

        /// <summary>F: fires the selection's abilities, or starts aiming when they need a spot on the ground.</summary>
        public void TriggerAbility(string only = null)
        {
            var withAbility = MyUnits.Where(u => u.ability != null && (only == null || GameData.S(u.ability, "id", "") == only)).ToList();
            if (withAbility.Count == 0) return;
            var ready = withAbility.Where(battle.AbilityReady).ToList();
            if (ready.Count == 0)
            {
                string need = GameData.S(withAbility[0].ability, "needs");
                Toast(need != null && !battle.Player.research.Contains(need) ? "Research it first" : "Ability recharging");
                return;
            }
            if (Battle.AbilityAimed(ready[0]))
            {
                abilityMode = true;
                abilityOnly = only;
                attackMode = false;
                return;
            }
            foreach (var u in ready)
                if (!Battle.AbilityAimed(u)) battle.UseAbility(u);
        }

        static readonly string[] OnePerClick = { "ram", "charge", "frag", "canister", "salvo", "barrage", "napalm" };

        void AimAbility(Vector2 px)
        {
            bool used = false;
            foreach (var u in MyUnits.ToList())
            {
                if (!Battle.AbilityAimed(u) || !battle.AbilityReady(u)) continue;
                if (abilityOnly != null && GameData.S(u.ability, "id", "") != abilityOnly) continue;
                used = battle.UseAbility(u, px) || used;
                // one squad per click for aimed attacks, so a group doesn't waste them all on one spot
                if (used && System.Array.IndexOf(OnePerClick, GameData.S(u.ability, "id", "")) >= 0) break;
            }
            if (!used) Toast("No ability ready");
            else fx.Marker(MapBuilder.Ground(map, px.x, px.y), new Color(1f, 0.75f, 0.25f), 2.6f);
        }

        void Update()
        {
            selection.RemoveAll(e => !e.alive || (e is Unit su && su.inBunker != null));
            if (toastT > 0) toastT -= Time.unscaledDeltaTime;
            if (battle == null || cam == null) return;
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (mouse == null) return;
            var mp = mouse.position.ReadValue();
            var gui = Gui(mp);
            hover = OverHud(gui) ? null : Pick(mp);
            if (battle.winner != null) { dragging = false; return; }

            // ---- left button
            if (mouse.leftButton.wasPressedThisFrame && !OverHud(gui))
            {
                if (scanFrom != null)
                {
                    var sensor = scanFrom;
                    scanFrom = null;
                    if (GroundPoint(mp, out var sp2)) Toast(battle.Scan(sensor, sp2) ?? "Scanning");
                }
                else if (abilityMode)
                {
                    if (GroundPoint(mp, out var ap)) AimAbility(ap);
                    bool again = kb != null && kb.shiftKey.isPressed &&
                                 MyUnits.Any(u => Battle.AbilityAimed(u) && battle.AbilityReady(u));
                    abilityMode = again;
                }
                else if (attackMode)
                {
                    if (hover != null && Battle.Hostile(hover.faction, "player")) battle.OrderAttack(MyUnits, hover);
                    else if (GroundPoint(mp, out var px))
                    {
                        battle.OrderMove(MyUnits, px, true);
                        fx.Marker(MapBuilder.Ground(map, px.x, px.y), EntityView.EnemyColor, 2.2f);
                    }
                    if (kb == null || !kb.shiftKey.isPressed) attackMode = false;
                }
                else
                {
                    dragging = true;
                    dragStart = dragNow = gui;
                }
            }
            if (dragging) dragNow = gui;
            if (dragging && mouse.leftButton.wasReleasedThisFrame)
            {
                dragging = false;
                bool shift = kb != null && kb.shiftKey.isPressed;
                var rect = Rect.MinMaxRect(Mathf.Min(dragStart.x, dragNow.x), Mathf.Min(dragStart.y, dragNow.y),
                    Mathf.Max(dragStart.x, dragNow.x), Mathf.Max(dragStart.y, dragNow.y));
                if (rect.width < 6 && rect.height < 6) ClickSelect(mp, shift);
                else
                {
                    var inBox = battle.units.Where(u => u.alive && u.faction == "player" &&
                                                        ScreenOf(u, out var p, out _) && rect.Contains(Gui(p))).Cast<Entity>().ToList();
                    if (inBox.Count > 0 || !shift) Select(inBox, shift);
                }
            }

            // ---- right button
            if (mouse.rightButton.wasPressedThisFrame && !OverHud(gui))
            {
                var mine = MyUnits.ToList();
                if (mine.Count > 0)
                {
                    if (hover is Tower bunker && bunker.IsBunker && bunker.faction == "player")
                    {
                        // right click on your Bunker: infantry squads garrison it
                        if (bunker.garrison.Count >= bunker.capacity) Toast("Bunker is full");
                        else if (battle.OrderGarrison(mine, bunker) == 0) Toast("Only infantry squads can garrison");
                        else if (bunker.view is EntityView bv && bv != null) fx.Marker(bv.transform.position, EntityView.PlayerColor, bunker.radius * MapBuilder.Unit * 1.4f);
                    }
                    else if (hover != null && Battle.Hostile(hover.faction, "player"))
                    {
                        battle.OrderAttack(mine, hover);
                        if (hover.view is EntityView hv && hv != null) fx.Marker(hv.transform.position, EntityView.EnemyColor, hover.radius * MapBuilder.Unit * 1.4f);
                    }
                    else if (GroundPoint(mp, out var px))
                    {
                        battle.OrderMove(mine, px, false);
                        fx.Marker(MapBuilder.Ground(map, px.x, px.y), EntityView.PlayerColor, 2.2f);
                    }
                }
                else if (selection.Count == 1 && selection[0] is Building b && b.faction == "player" && b.trains.Length > 0
                         && GroundPoint(mp, out var rp))
                {
                    b.rally = rp;
                    fx.Marker(MapBuilder.Ground(map, rp.x, rp.y), new Color(0.4f, 0.8f, 1f), 2.5f);
                    Toast("Rally point set");
                }
            }

            if (kb == null) return;
            bool ctrl = kb.ctrlKey.isPressed;
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (scanFrom != null) scanFrom = null;
                else if (attackMode || abilityMode) attackMode = abilityMode = false;
                else Select(new Entity[0]);
            }
            if (kb.aKey.wasPressedThisFrame && MyUnits.Any())
            {
                attackMode = true;
                abilityMode = false;
            }
            if (kb.fKey.wasPressedThisFrame) TriggerAbility();
            if (kb.sKey.wasPressedThisFrame) battle.OrderStop(MyUnits, false);
            if (kb.hKey.wasPressedThisFrame) battle.OrderStop(MyUnits, true);
            if (kb.deleteKey.wasPressedThisFrame && selection.Count == 1 && selection[0] is Building rb && rb.faction == "player")
            {
                string err = battle.Demolish(rb);
                Toast(err ?? "Recycled");
            }
            else if (kb.deleteKey.wasPressedThisFrame && selection.Count == 1 && selection[0] is Tower st && st.faction == "player")
            {
                int value = battle.SellValue(st);
                string err = battle.SellTower(st);
                Toast(err ?? $"Sold (+{value})");
            }
            if (kb.qKey.wasPressedThisFrame)
                Select(battle.units.Where(u => u.alive && u.faction == "player" && (u.pop > 0 || u.kind == "commander")));
            if (kb.eKey.wasPressedThisFrame)
                Select(battle.units.Where(u => u.alive && u.faction == "player" && OnScreen(u)));
            if (kb.gKey.wasPressedThisFrame)
            {
                var hero = battle.Player.hero;
                if (hero == null || !hero.alive)
                {
                    if (battle.Player.heroRespawn > 0) Toast($"Commander respawns in {Mathf.CeilToInt(battle.Player.heroRespawn)} s");
                }
                else
                {
                    if (selection.Count == 1 && selection[0] == hero) Center(hero);
                    Select(new Entity[] { hero });
                }
            }
            for (int i = 1; i <= 5; i++)
            {
                var key = kb[(Key)((int)Key.Digit1 + i - 1)];
                if (!key.wasPressedThisFrame) continue;
                if (ctrl)
                {
                    groups[i] = selection.Where(e => e.faction == "player").ToList();
                    Toast($"Group {i} set ({groups[i].Count})");
                }
                else if (groups[i] != null)
                {
                    groups[i].RemoveAll(e => !e.alive);
                    Select(groups[i]);
                    if (lastGroup == i && Time.unscaledTime - lastGroupT < 0.35f && groups[i].Count > 0) Center(groups[i][0]);
                    lastGroup = i;
                    lastGroupT = Time.unscaledTime;
                }
            }
        }

        bool OnScreen(Entity e) =>
            ScreenOf(e, out var p, out _) && p.x >= 0 && p.y >= 0 && p.x <= Screen.width && p.y <= Screen.height;

        public void Center(Entity e)
        {
            if (rig != null) rig.JumpTo(MapBuilder.Ground(map, e.x, e.y));
        }

        void ClickSelect(Vector2 mp, bool shift)
        {
            var e = Pick(mp);
            if (e == null)
            {
                if (!shift) Select(new Entity[0]);
                // a click on a free expansion pad selects the pad
                if (GroundPoint(mp, out var gp))
                    selectedSite = battle.FreeSites().FirstOrDefault(s => (new Vector2(s.x, s.y) - gp).sqrMagnitude < 90 * 90);
                return;
            }
            bool dbl = e == lastClicked && Time.unscaledTime - lastClickT < 0.35f;
            lastClicked = e;
            lastClickT = Time.unscaledTime;
            if (dbl && e is Unit && e.faction == "player")
            {
                Select(battle.units.Where(u => u.alive && u.faction == "player" && u.kind == e.kind && OnScreen(u)), shift);
                return;
            }
            if (shift && e.faction == "player" && e.IsUnit)
            {
                if (selection.Contains(e))
                {
                    selection.Remove(e);
                    if (e.view is EntityView v && v != null) v.SetSelected(false);
                }
                else Select(new[] { e }, true);
                return;
            }
            Select(new[] { e });
        }
    }
}
