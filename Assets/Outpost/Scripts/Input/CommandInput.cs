using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Outpost.Content;
using Outpost.Sim;

namespace Outpost.View
{
    /// <summary>
    /// RTS mouse / keyboard controls, matching the Python game (scenes/rts.py on_key):
    /// left click / drag select (Shift adds, double-click selects that type on screen), right click moves
    /// or attacks (sets the rally point with a building selected), attack-move + left click, unit ability (aimed ones
    /// then take a left click), stop, hold, whole army, army on screen, Commander, control groups 1-9, leader powers,
    /// jump to base / last alert, game speed. Keys come from Hotkeys (rebindable); Handle(id) runs one action.
    /// Esc / pause, help and the command card keys are HudRoot's; camera keys are RtsCamera's.
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
        public string powerAim;                                    // leader power waiting for its target (Z X C V)
        public bool dragging;
        public Vector2 dragStart, dragNow;                        // GUI coords
        public Entity hover;
        public string toast;
        public float toastT;
        /// <summary>Set by the HUD while paused: no orders or keys.</summary>
        public bool frozen;
        /// <summary>Game speed (Tab: x1, x1.5, x2 as Python); BattleSession multiplies the clock by it.</summary>
        public float speed = 1f;
        /// <summary>Last point the camera was sent to by a key (game pixels), for tests.</summary>
        public Vector2 lastJump;

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
                if (!e.alive || !battle.Seen(e, "player") || !ScreenOf(e, out var p, out var r)) continue;
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

        /// <summary>Z X C V / the power bar: starts aiming leader power 0-3, or says why it can't be used.</summary>
        public void TriggerPower(int index)
        {
            if (battle == null || index < 0 || index >= battle.playerPowers.Length) return;
            string pid = battle.playerPowers[index];
            string why = battle.CanPower(pid);
            if (why != null)
            {
                Toast(why);
                return;
            }
            powerAim = pid;
            attackMode = abilityMode = false;
            scanFrom = null;
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
            if (battle.winner != null || frozen) { dragging = false; return; }

            // ---- left button
            if (mouse.leftButton.wasPressedThisFrame && !OverHud(gui))
            {
                if (powerAim != null)
                {
                    string pid = powerAim;
                    powerAim = null;
                    if (GroundPoint(mp, out var pp))
                    {
                        string err = battle.CastPower(pid, pp);
                        if (err != null) Toast(err);
                        else fx.Marker(MapBuilder.Ground(map, pp.x, pp.y), new Color(0.3f, 0.9f, 1f), 3f);
                    }
                }
                else if (scanFrom != null)
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
                powerAim = null;        // a right click drops a power you were aiming
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

            foreach (var id in KeyActions)
                if (Hotkeys.Pressed(id)) Handle(id);
        }

        /// <summary>The battle actions CommandInput answers (Hotkeys ids). Cancel / pause / help / card keys are HudRoot's,
        /// camera keys RtsCamera's.</summary>
        public static readonly string[] KeyActions = BuildKeyActions();

        static string[] BuildKeyActions()
        {
            var l = new List<string>
            {
                "attack_move", "stop", "hold", "ability", "recycle", "select_army", "select_screen", "select_commander",
                "jump_base", "jump_alert", "game_speed", "reveal_map", "power_1", "power_2", "power_3", "power_4",
            };
            for (int i = 1; i <= Hotkeys.Groups; i++)
            {
                l.Add("group_" + i);
                l.Add("set_group_" + i);
            }
            return l.ToArray();
        }

        /// <summary>Runs one action, exactly what its key does (tests call this too). False if it did nothing.</summary>
        public bool Handle(string id)
        {
            if (battle == null || id == null) return false;
            switch (id)
            {
                case "attack_move":
                    if (!MyUnits.Any()) return false;
                    attackMode = true;
                    abilityMode = false;
                    return true;
                case "stop":
                    battle.OrderStop(MyUnits, false);
                    return true;
                case "hold":
                    battle.OrderStop(MyUnits, true);
                    return true;
                case "ability":
                    TriggerAbility();
                    return true;
                case "recycle":
                    return RecycleSelected();
                case "select_army":
                    // Python: combat units (pop > 0), the Commander and the power-dropped Mech
                    Select(battle.units.Where(u => u.alive && u.faction == "player" && u.inBunker == null &&
                                                   (u.pop > 0 || u.kind == "commander" || u.kind == "mech")));
                    return true;
                case "select_screen":
                    Select(battle.units.Where(u => u.alive && u.faction == "player" && u.inBunker == null && OnScreen(u)));
                    return true;
                case "select_commander":
                    return SelectCommander();
                case "jump_base":
                    return JumpBase();
                case "jump_alert":
                    return JumpAlert();
                case "game_speed":
                    CycleSpeed();
                    return true;
                case "reveal_map":
                    return ToggleReveal();
            }
            if (id.StartsWith("power_"))
            {
                TriggerPower(int.Parse(id.Substring(6)) - 1);
                return true;
            }
            if (id.StartsWith("set_group_")) return SetGroup(int.Parse(id.Substring(10)));
            if (id.StartsWith("group_")) return RecallGroup(int.Parse(id.Substring(6)));
            return false;
        }

        /// <summary>Is something being aimed or picked that Esc would drop?</summary>
        public bool HasMode => powerAim != null || scanFrom != null || attackMode || abilityMode || selectedSite != null;

        /// <summary>Esc / P (Python): drops the power, Scan, ability or attack-move being aimed, or the picked pad.
        /// False when there was nothing to drop (then the HUD pauses).</summary>
        public bool CancelMode()
        {
            if (!HasMode) return false;
            powerAim = null;
            scanFrom = null;
            attackMode = abilityMode = false;
            abilityOnly = null;
            selectedSite = null;
            return true;
        }

        public static readonly float[] Speeds = { 1f, 1.5f, 2f };

        /// <summary>Tab: x1 -> x1.5 -> x2 -> x1 (Python).</summary>
        public void CycleSpeed()
        {
            int i = System.Array.IndexOf(Speeds, speed);
            speed = Speeds[(i + 1) % Speeds.Length];
            Toast("Game speed x" + speed.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
        }

        bool RecycleSelected()
        {
            // Python: Del recycles every selected player building and sells every selected player turret
            string err = null;
            int sold = 0, recycled = 0, value = 0;
            foreach (var e in selection.ToList())
            {
                if (!e.alive || e.faction != "player") continue;
                if (e is Tower st)
                {
                    int v = battle.SellValue(st);
                    string r = battle.SellTower(st);
                    if (r == null) { sold++; value += v; }
                    else err = r;
                }
                else if (e is Building rb)
                {
                    string r = battle.Demolish(rb);
                    if (r == null) recycled++;
                    else err = r;
                }
            }
            string done = sold > 0 ? $"Sold (+{value})" : recycled > 1 ? $"Recycled {recycled}" : recycled > 0 ? "Recycled" : null;
            if (err != null || done != null) Toast(err ?? done);
            selection.RemoveAll(e => !e.alive);
            return sold + recycled > 0;
        }

        bool SelectCommander()
        {
            var hero = battle.Player.hero;
            if (hero == null || !hero.alive)
            {
                if (battle.Player.heroRespawn > 0) Toast($"Commander respawns in {Mathf.CeilToInt(battle.Player.heroRespawn)} s");
                return false;
            }
            if (selection.Count == 1 && selection[0] == hero) Center(hero);
            Select(new Entity[] { hero });
            return true;
        }

        bool SetGroup(int n)
        {
            if (n < 1 || n >= groups.Length) return false;
            groups[n] = selection.Where(e => e.faction == "player").ToList();
            Toast($"Group {n} set ({groups[n].Count})");
            return true;
        }

        bool RecallGroup(int n)
        {
            if (n < 1 || n >= groups.Length || groups[n] == null) return false;
            groups[n].RemoveAll(e => !e.alive);
            if (groups[n].Count == 0) return false;
            float now = Time.realtimeSinceStartup;
            // tapped twice: jump to the middle of the group (Python centers on the average position)
            if (lastGroup == n && now - lastGroupT < 0.4f)
                CenterAt(groups[n].Average(e => e.x), groups[n].Average(e => e.y));
            Select(groups[n]);
            lastGroup = n;
            lastGroupT = now;
            return true;
        }

        int baseCycle, alertBack;
        float lastBaseT = -10f, lastAlertT = -10f;

        /// <summary>B / Home: the main base (Python bases_of(player)[0]); pressed again within 2 s, the next base.</summary>
        public bool JumpBase()
        {
            var mine = battle.bases.Where(b => b.faction == "player" && b.Alive).ToList();
            if (mine.Count == 0) return false;
            float now = Time.realtimeSinceStartup;
            baseCycle = now - lastBaseT < 2f ? (baseCycle + 1) % mine.Count : 0;
            lastBaseT = now;
            CenterAt(mine[baseCycle].x, mine[baseCycle].y);
            return true;
        }

        /// <summary>Space: the newest alert (Python alerts[-1]); pressed again within 2 s, the one before it.</summary>
        public bool JumpAlert()
        {
            var a = battle.alerts;
            if (a.Count == 0)
            {
                Toast("No alerts yet");
                return false;
            }
            float now = Time.realtimeSinceStartup;
            alertBack = now - lastAlertT < 2f ? Mathf.Min(alertBack + 1, a.Count - 1) : 0;
            lastAlertT = now;
            var note = a[a.Count - 1 - alertBack];
            CenterAt(note.pos.x, note.pos.y);
            return true;
        }

        /// <summary>F9 in the editor and development builds: lift or drop the fog (Python debug reveal).</summary>
        bool ToggleReveal()
        {
            if (!Application.isEditor && !Debug.isDebugBuild) return false;
            var g = battle.FogOf("player");
            if (g == null) return false;
            if (!g.revealAll)
            {
                battle.RevealMap();
                Toast("Map revealed (debug)");
            }
            else
            {
                foreach (var f in battle.fog.Values) f.revealAll = false;
                battle.UpdateFog();
                Toast("Fog back on (debug)");
            }
            return true;
        }

        bool OnScreen(Entity e) =>
            ScreenOf(e, out var p, out _) && p.x >= 0 && p.y >= 0 && p.x <= Screen.width && p.y <= Screen.height;

        public void Center(Entity e) => CenterAt(e.x, e.y);

        /// <summary>Moves the camera over a point (game pixels). Remembered in lastJump for tests.</summary>
        public void CenterAt(float x, float y)
        {
            lastJump = new Vector2(x, y);
            if (rig != null && map != null) rig.JumpTo(MapBuilder.Ground(map, x, y));
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
