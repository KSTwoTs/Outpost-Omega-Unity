using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Outpost.Content;
using Outpost.Sim;

namespace Outpost.View
{
    /// <summary>
    /// Phase-2 HUD (IMGUI, to be replaced by a proper UI in phase 5): resources, selection panel with
    /// production / base building, health bars, drag box, minimap, alerts, pause and end screens.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        public Battle battle;
        public CommandInput input;
        public Fx fx;
        public Camera cam;
        public RtsCamera rig;
        public MapData map;
        public Texture2D minimapTex;
        public System.Action restart;
        public bool paused;

        static readonly string[] PlayerBuild = { "supply", "barracks", "depot", "reactor", "airpad", "armory", "sensor" };
        static readonly string[] PlayerTowers = { "pulse", "missile", "tesla", "railgun" };

        GUIStyle label, small, title, center, btn;
        bool help = true;

        void Styles()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
            small = new GUIStyle(label) { fontSize = 12 };
            title = new GUIStyle(label) { fontSize = 17, fontStyle = FontStyle.Bold };
            center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 30, fontStyle = FontStyle.Bold };
            btn = new GUIStyle(GUI.skin.button) { fontSize = 12, wordWrap = true, richText = true };
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        void Panel(Rect r)
        {
            Fill(r, new Color(0.04f, 0.06f, 0.09f, 0.82f));
            Fill(new Rect(r.x, r.y, r.width, 2), new Color(0.3f, 0.75f, 1f, 0.6f));
            input.blockers.Add(r);
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null || battle == null) return;
            if (kb.pKey.wasPressedThisFrame && battle.winner == null) paused = !paused;
            if (kb.f1Key.wasPressedThisFrame) help = !help;
        }

        void OnGUI()
        {
            if (battle == null || input == null) return;
            Styles();
            if (Event.current.type == EventType.Layout) input.blockers.Clear();
            HealthBars();
            DragBox();
            TopBar();
            SelectionPanel();
            Minimap();
            Alerts();
            Help();
            EndOrPause();
        }

        // ------------------------------------------------------------------ world overlays
        void HealthBars()
        {
            if (Event.current.type != EventType.Repaint) return;
            foreach (var e in battle.All())
            {
                if (!e.alive || !(e.view is EntityView v) || v == null || battle.HiddenFrom(e, "player")) continue;
                bool sel = v.Selected, hov = input.hover == e;
                bool show = sel || hov || e.hp < e.maxHp - 0.5f || !e.Done || e.shield > 0.5f;
                if (!show) continue;
                var sp = cam.WorldToScreenPoint(v.HeadPosition);
                if (sp.z <= 0) continue;
                float w = Mathf.Clamp(e.radius * 1.4f, 26, 90);
                var r = new Rect(sp.x - w / 2, Screen.height - sp.y - 6, w, 5);
                Fill(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), new Color(0, 0, 0, 0.7f));
                float k = e.Ratio;
                var c = e.faction == "player" ? Color.Lerp(new Color(1f, 0.3f, 0.2f), new Color(0.35f, 1f, 0.45f), k)
                    : Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(1f, 0.55f, 0.3f), k);
                Fill(new Rect(r.x, r.y, r.width * k, r.height), c);
                if (e.shield > 0.5f)
                    Fill(new Rect(r.x, r.y - 3, r.width * Mathf.Clamp01(e.shield / Mathf.Max(1f, e.maxHp * 0.3f)), 2), new Color(0.45f, 0.85f, 1f));
                if (e is Unit u && u.squad > 1)
                    for (int i = 1; i < u.squad; i++) Fill(new Rect(r.x + r.width * i / u.squad, r.y, 1, r.height), new Color(0, 0, 0, 0.8f));
                if (e is Unit ru && ru.rank > 0)
                    GUI.Label(new Rect(r.xMax + 2, r.y - 8, 30, 18), new string('*', ru.rank), small);
                float build = e is Building b && !b.Done ? 1 - b.buildLeft / b.buildTotal : e is Tower t && !t.Done ? 1 - t.buildLeft / t.buildTotal : -1;
                if (build >= 0)
                {
                    Fill(new Rect(r.x, r.yMax + 2, r.width, 3), new Color(0, 0, 0, 0.7f));
                    Fill(new Rect(r.x, r.yMax + 2, r.width * build, 3), new Color(0.4f, 0.8f, 1f));
                }
                if (e is Building qb && qb.queue.Count > 0)
                {
                    var q = qb.queue[0];
                    Fill(new Rect(r.x, r.yMax + 2, r.width, 3), new Color(0, 0, 0, 0.7f));
                    Fill(new Rect(r.x, r.yMax + 2, r.width * (1 - q.t / q.total), 3), new Color(1f, 0.85f, 0.3f));
                }
            }
        }

        void DragBox()
        {
            if (!input.dragging) return;
            var r = Rect.MinMaxRect(Mathf.Min(input.dragStart.x, input.dragNow.x), Mathf.Min(input.dragStart.y, input.dragNow.y),
                Mathf.Max(input.dragStart.x, input.dragNow.x), Mathf.Max(input.dragStart.y, input.dragNow.y));
            if (r.width < 6 && r.height < 6) return;
            var line = new Color(0.35f, 1f, 0.5f, 0.9f);
            Fill(r, new Color(0.35f, 1f, 0.5f, 0.12f));
            Fill(new Rect(r.x, r.y, r.width, 1), line);
            Fill(new Rect(r.x, r.yMax, r.width, 1), line);
            Fill(new Rect(r.x, r.y, 1, r.height), line);
            Fill(new Rect(r.xMax, r.y, 1, r.height), line);
        }

        // ------------------------------------------------------------------ bars and panels
        static string Clock(float t) => $"{(int)(t / 60)}:{(int)(t % 60):00}";

        void TopBar()
        {
            var f = battle.Player;
            bool heroDown = f.hero == null && f.heroRespawn > 0;
            var r = new Rect(0, 0, heroDown ? 900 : 700, 30);
            Panel(r);
            GUI.Label(new Rect(10, 5, r.width - 20, 24),
                $"<color=#ffd77a><b>Supplies {(int)f.res}</b></color> (+{f.income:0.0}/s)     " +
                $"<color=#9fe6ff>Pop {f.popUsed}/{f.popCap}</color>     Tech {f.tech}     {Clock(battle.time)}" +
                (battle.ai != null ? $"     <color=#ff9a7a>Next alien attack {Clock(Mathf.Max(0, battle.ai.nextAttack - battle.time))}</color>" : "") +
                (heroDown ? $"     <color=#ffd77a>Commander back in {Mathf.CeilToInt(f.heroRespawn)} s</color>" : ""),
                label);
            if (input.scanFrom != null)
                GUI.Label(new Rect(Screen.width / 2f - 200, 40, 400, 24), "<color=#70ffa0><b>SCAN: left click anywhere on the map (Esc cancels)</b></color>", label);
            if (input.abilityMode)
                GUI.Label(new Rect(Screen.width / 2f - 200, 40, 400, 24), "<color=#ffc060><b>ABILITY: left click where to use it (Esc cancels)</b></color>", label);
            if (input.attackMode)
                GUI.Label(new Rect(Screen.width / 2f - 170, 40, 340, 24), "<color=#ff8070><b>ATTACK-MOVE: left click a target or ground</b></color>", label);
        }

        void SelectionPanel()
        {
            var sel = input.selection.Where(e => e.alive).ToList();
            if (sel.Count == 0) return;
            float w = 660, h = 170;
            if (sel.Count == 1 && sel[0] is Building rb && rb.Done && rb.faction == "player" && rb.Research != null) h = 226;
            var r = new Rect(Mathf.Max(8, Screen.width / 2f - w / 2 - 110), Screen.height - h, w, h);
            Panel(r);
            GUILayout.BeginArea(new Rect(r.x + 10, r.y + 8, r.width - 20, r.height - 14));
            if (sel.Count == 1) Single(sel[0]);
            else Many(sel);
            GUILayout.EndArea();
        }

        void Single(Entity e)
        {
            string tint = e.faction == "player" ? "#9cf5b0" : "#ff9a8a";
            GUILayout.Label($"<color={tint}>{e.name}</color>   <size=12>{(int)e.hp} / {(int)e.maxHp} HP   armor {e.armor:0}</size>", title);
            if (e is Unit u)
            {
                string squad = u.squad > 1 ? $"Squad {u.Members}/{u.squad}   " : "";
                GUILayout.Label($"{squad}Damage {u.damage:0}{(u.squad > 1 ? " each" : "")}  x{u.rate:0.#}/s   Range {u.range:0}   Speed {u.speed:0}   " +
                                $"{(u.hitsAir ? "Hits air" : "Ground only")}   Class {u.cls}{(u.rank > 0 ? $"   Rank {u.rank}" : "")}", small);
                GUILayout.Label("<color=#aab>" + GameData.S(u.data, "desc", "") + "</color>", small);
                string buffs = u.buffs.Count > 0 ? "   Active: " + string.Join(", ", u.buffs.Select(kv => $"{kv.Key} {Mathf.CeilToInt(kv.Value)} s")) : "";
                GUILayout.Label($"Order: {u.order}{(u.attackMove ? " (attack-move)" : "")}{buffs}", small);
                if (u.ability != null && u.faction == "player")
                {
                    string need = GameData.S(u.ability, "needs");
                    bool locked = need != null && !battle.Player.research.Contains(need);
                    string state = locked ? "needs research" : u.abilityCd > 0 ? $"ready in {Mathf.CeilToInt(u.abilityCd)} s" : "ready";
                    GUI.enabled = !locked && u.abilityCd <= 0;
                    if (GUILayout.Button($"{GameData.S(u.ability, "name", "Ability")} (F)\n<color=#ffd77a>{state}</color>", btn, GUILayout.Width(170), GUILayout.Height(38)))
                        input.TriggerAbility();
                    GUI.enabled = true;
                }
                return;
            }
            if (e is Tower t)
            {
                GUILayout.Label($"Damage {t.damage:0}  x{t.rate:0.#}/s   Range {t.range:0}   {(t.hitsAir ? "Hits air" : "Ground only")}", small);
                if (!t.Done) GUILayout.Label($"Building... {(int)(100 * (1 - t.buildLeft / t.buildTotal))}%", small);
                return;
            }
            if (!(e is Building b)) return;
            GUILayout.Label("<color=#aab>" + GameData.S(b.data, "desc", "") + "</color>", small);
            if (!b.Done)
            {
                GUILayout.Label($"Building... {(int)(100 * (1 - b.buildLeft / b.buildTotal))}%", small);
                return;
            }
            if (b.faction != "player") return;
            if (b.IsHq) HqPanel(b);
            else if (b.trains.Length > 0 || b.Research != null) Production(b);
            if (b.data?["scan"] != null)
            {
                GUI.enabled = b.scanCd <= 0;
                string tip = b.scanCd > 0 ? $"ready in {Mathf.CeilToInt(b.scanCd)} s" : "reveals cloaked aliens";
                if (GUILayout.Button($"Scan\n<color=#ffd77a>{tip}</color>", btn, GUILayout.Width(150), GUILayout.Height(40))) input.scanFrom = b;
                GUI.enabled = true;
            }
            if (b.data?["shield"] != null) GUILayout.Label("Shielding every building and turret in this base.", small);
            else if (b.Income > 0)
            {
                GUILayout.Label($"Income +{b.Income:0.0}/s", small);
                UpgradeButton(b);
            }
        }

        void UpgradeButton(Building b)
        {
            var up = b.Upgrade;
            if (up == null || b.upgraded) return;
            int tech = GameData.I(up, "tech", 1);
            GUI.enabled = !b.Upgrading && battle.Player.tech >= tech;
            string tip = b.Upgrading ? "upgrading..." : tech > battle.Player.tech ? $"Tech {tech}" : $"{GameData.I(up, "cost")}";
            if (GUILayout.Button($"Upgrade: {GameData.S(up, "name", "Upgrade")}\n<color=#ffd77a>{tip}</color>", btn, GUILayout.Width(150), GUILayout.Height(40)))
                Report(battle.QueueUpgrade(b));
            GUI.enabled = true;
        }

        void HqPanel(Building hq)
        {
            var bs = hq.baseRef;
            int used = bs.slots.Take(bs.SlotLimit).Count(s => s != null);
            GUILayout.Label($"Build ({used}/{bs.SlotLimit} slots)", small);
            GUILayout.BeginHorizontal();
            foreach (var k in PlayerBuild)
            {
                var d = GameData.Building(k);
                if (d == null) continue;
                int tech = battle.BuildTech(k);
                GUI.enabled = battle.Player.tech >= tech && battle.FreeSlot(bs) >= 0;
                string tip = tech > battle.Player.tech ? $"Tech {tech}" : $"{battle.BuildCost(k)}";
                if (GUILayout.Button($"{GameData.S(d, "name", k)}\n<color=#ffd77a>{tip}</color>", btn, GUILayout.Width(86), GUILayout.Height(40)))
                    Report(battle.Build(bs, k));
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
            int sockets = bs.sockets.Count(s => s != null);
            GUILayout.Label($"Turrets ({sockets}/{bs.sockets.Length} sockets)", small);
            GUILayout.BeginHorizontal();
            foreach (var k in PlayerTowers)
            {
                var d = GameData.Tower(k);
                if (d == null) continue;
                GUI.enabled = battle.FreeSocket(bs) >= 0;
                if (GUILayout.Button($"{GameData.S(d, "name", k)}\n<color=#ffd77a>{battle.TowerCost(k)}</color>", btn, GUILayout.Width(110), GUILayout.Height(40)))
                    Report(battle.BuildTower(bs, k));
                GUI.enabled = true;
            }
            UpgradeButton(hq);
            GUILayout.EndHorizontal();
        }

        void Production(Building b)
        {
            GUILayout.BeginHorizontal();
            foreach (var k in b.trains)
            {
                var d = GameData.Unit(k);
                if (d == null) continue;
                int tech = GameData.I(d, "tech", 1);
                GUI.enabled = battle.Player.tech >= tech;
                string tip = tech > battle.Player.tech ? $"Tech {tech}" : $"{GameData.I(d, "cost")}  pop {GameData.I(d, "pop", 1)}";
                if (GUILayout.Button($"{GameData.S(d, "name", k)}\n<color=#ffd77a>{tip}</color>", btn, GUILayout.Width(118), GUILayout.Height(44)))
                    Report(battle.QueueUnit(b, k));
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
            var research = b.Research;
            if (research != null)
            {
                var P = battle.Player;
                GUILayout.BeginHorizontal();
                foreach (var kv in research)
                {
                    var rd = kv.Value;
                    bool have = P.research.Contains(kv.Key), busy = P.researching.Contains(kv.Key);
                    int tech = GameData.I(rd, "tech", 1);
                    string after = GameData.S(rd, "after");
                    bool chained = after != null && !P.research.Contains(after);
                    GUI.enabled = !have && !busy && P.tech >= tech && !chained;
                    string tip = have ? "done" : busy ? "researching" : tech > P.tech ? $"Tech {tech}"
                        : chained ? "needs " + GameData.S(research[after], "name", after) : $"{GameData.I(rd, "cost")}";
                    if (GUILayout.Button($"<color=#9fe6ff>{GameData.S(rd, "name", kv.Key)}</color>\n<color=#ffd77a>{tip}</color>", btn, GUILayout.Width(150), GUILayout.Height(40)))
                        Report(battle.QueueResearch(b, kv.Key));
                    GUI.enabled = true;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Label(b.queue.Count == 0 ? (b.trains.Length > 0 ? "Queue empty   (right-click the ground to set a rally point)" : "Queue empty") : "Queue (click to cancel):", small);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < b.queue.Count; i++)
            {
                var q = b.queue[i];
                string pct = i == 0 ? $" {(int)(100 * (1 - q.t / q.total))}%" : "";
                string qn = q.type == "research" ? GameData.S(research?[q.id], "name", q.id) : GameData.S(GameData.Unit(q.id), "name", q.id);
                if (GUILayout.Button(qn + pct, btn, GUILayout.Width(110), GUILayout.Height(26)))
                {
                    battle.CancelQueue(b, i);
                    break;
                }
            }
            GUILayout.EndHorizontal();
        }

        void Many(List<Entity> sel)
        {
            int ready = sel.OfType<Unit>().Count(u => u.faction == "player" && battle.AbilityReady(u));
            GUILayout.Label($"{sel.Count} selected   (click a type to keep just those)" +
                            (ready > 0 ? $"   <size=12><color=#ffc060>{ready} abilit{(ready == 1 ? "y" : "ies")} ready (F)</color></size>" : ""), title);
            GUILayout.BeginHorizontal();
            int n = 0;
            foreach (var g in sel.GroupBy(e => e.kind).ToList())
            {
                var first = g.First();
                float hp = g.Sum(e => e.hp), max = g.Sum(e => e.maxHp);
                if (GUILayout.Button($"{first.name} x{g.Count()}\n<size=10>{(int)(100 * hp / Mathf.Max(1, max))}% HP</size>", btn, GUILayout.Width(118), GUILayout.Height(42)))
                    input.Select(g.ToList());
                if (++n % 5 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
            }
            GUILayout.EndHorizontal();
        }

        void Report(string error)
        {
            if (error != null) input.Toast(error);
        }

        // ------------------------------------------------------------------ minimap
        void Minimap()
        {
            float w = 230, h = w * map.px_h / map.px_w;
            var r = new Rect(Screen.width - w - 8, Screen.height - h - 8, w, h);
            Panel(new Rect(r.x - 4, r.y - 6, r.width + 8, r.height + 10));
            if (minimapTex != null) GUI.DrawTexture(r, minimapTex);
            float sx = w / map.px_w, sy = h / map.px_h;
            foreach (var e in battle.All())
            {
                if (!e.alive || battle.HiddenFrom(e, "player")) continue;
                float s = e.IsUnit ? 3 : e is Building b && b.IsHq ? 7 : 5;
                var c = e.faction == "player" ? new Color(0.35f, 1f, 0.5f) : new Color(1f, 0.35f, 0.3f);
                Fill(new Rect(r.x + e.x * sx - s / 2, r.y + e.y * sy - s / 2, s, s), c);
            }
            if (rig != null)
            {
                var p = rig.transform.position;
                float cx = p.x / MapBuilder.Unit * sx, cy = -p.z / MapBuilder.Unit * sy;
                var vr = new Rect(r.x + cx - 22, r.y + cy - 14, 44, 28);
                Fill(new Rect(vr.x, vr.y, vr.width, 1), Color.white);
                Fill(new Rect(vr.x, vr.yMax, vr.width, 1), Color.white);
                Fill(new Rect(vr.x, vr.y, 1, vr.height), Color.white);
                Fill(new Rect(vr.xMax, vr.y, 1, vr.height), Color.white);
            }
            var ev = Event.current;
            if ((ev.type == EventType.MouseDown || ev.type == EventType.MouseDrag) && ev.button == 0 && r.Contains(ev.mousePosition))
            {
                float px = (ev.mousePosition.x - r.x) / sx, py = (ev.mousePosition.y - r.y) / sy;
                rig.JumpTo(MapBuilder.Ground(map, px, py));
                ev.Use();
            }
            else if (ev.type == EventType.MouseDown && ev.button == 1 && r.Contains(ev.mousePosition))
            {
                var px = new Vector2((ev.mousePosition.x - r.x) / sx, (ev.mousePosition.y - r.y) / sy);
                battle.OrderMove(input.MyUnits, px, false);
                ev.Use();
            }
        }

        // ------------------------------------------------------------------ messages
        void Alerts()
        {
            float y = 40;
            var style = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 16 };
            for (int i = fx.messages.Count - 1; i >= 0 && i >= fx.messages.Count - 3; i--)
            {
                var m = fx.messages[i];
                if (Time.time - m.t > 6f) continue;
                GUI.Label(new Rect(Screen.width / 2f - 450, y + 24, 900, 24),
                    $"<color={(m.faction == "alien" ? "#ff8a7a" : "#9cf5b0")}><b>{m.text}</b></color>", style);
                y += 24;
            }
            if (input.toastT > 0 && !string.IsNullOrEmpty(input.toast))
                GUI.Label(new Rect(Screen.width / 2f - 200, Screen.height - 210, 400, 24),
                    $"<color=#ffd77a><b>{input.toast}</b></color>", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 15 });
        }

        void Help()
        {
            if (!help)
            {
                GUI.Label(new Rect(Screen.width - 108, 6, 100, 20), "<color=#889>F1: controls</color>", small);
                return;
            }
            var r = new Rect(Screen.width - 408, 8, 400, 244);
            Panel(r);
            GUI.Label(new Rect(r.x + 8, r.y + 6, r.width - 16, r.height - 12),
                "<b>Controls</b>  (F1 hides)\n" +
                "Left click / drag: select   Shift: add   Double-click: all of that type\n" +
                "Right click: move / attack   (building selected: rally point)\n" +
                "A + left click: attack-move   S: stop   H: hold\n" +
                "F: unit ability (aimed ones: then left click the spot)\n" +
                "Research: select the Barracks, Depot, Air Pad or Armory\n" +
                "Q: whole army   E: army on screen   G: Commander\n" +
                "Ctrl+1-5: set group   1-5: recall (twice: jump)\n" +
                "Camera: arrows / screen edge / middle drag, wheel zoom,\n" +
                "   Page Up/Down or Alt+middle drag rotate, Home resets\n" +
                "Select your Command Post to build or upgrade. P: pause\n" +
                "The Commander respawns at your Command Post.\n" +
                "Goal: destroy the alien Hive.", small);
        }

        void EndOrPause()
        {
            if (battle.winner == null && !paused) return;
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.45f));
            string text = battle.winner == "player" ? "<color=#9cf5b0>VICTORY</color>"
                : battle.winner == "alien" ? "<color=#ff8a7a>DEFEAT</color>" : "PAUSED";
            GUI.Label(new Rect(0, Screen.height / 2f - 80, Screen.width, 50), text, center);
            if (battle.winner != null)
            {
                GUI.Label(new Rect(0, Screen.height / 2f - 30, Screen.width, 24), $"Time {Clock(battle.time)}",
                    new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
                if (GUI.Button(new Rect(Screen.width / 2f - 80, Screen.height / 2f + 10, 160, 36), "Play again")) restart?.Invoke();
            }
            else if (GUI.Button(new Rect(Screen.width / 2f - 80, Screen.height / 2f, 160, 36), "Resume")) paused = false;
        }
    }
}
