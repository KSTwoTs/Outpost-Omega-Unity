// CommandersScreen.cs · roadmap step M2 (filled Nov 21, Milestone 2 group 1).
// The Commander roster with its four tabs (Python scenes/commander_select.py).

using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Outpost.Sim;

namespace Outpost.UI
{
    /// <summary>
    /// Left: the roster. Right: the highlighted Commander with four tabs (Overview, Command Mode, Classic Defense,
    /// Siege Run). Every number on the mode tabs is read from the data (commanders.json, rts.json leaders / powers /
    /// leader_powers / units, run.json hero_decks), so the screen stays right when balance changes. One Commander is
    /// used in every mode: SELECT reports the pick through onSelect (GameFlow keeps it; group 3 saves it).
    /// Clicking the highlighted row again also selects it, as in Python.
    /// </summary>
    public class CommandersScreen
    {
        public readonly VisualElement root;
        public System.Action<string> onSelect;
        public System.Action onBack;

        static readonly string[] TabKeys = { "overview", "command", "classic", "siege" };
        static readonly string[] TabLabels = { "OVERVIEW", "COMMAND MODE", "CLASSIC DEFENSE", "SIEGE RUN" };

        readonly List<string> ids = new List<string>();
        readonly Dictionary<string, VisualElement> rows = new Dictionary<string, VisualElement>();
        readonly Dictionary<string, Label> activeTags = new Dictionary<string, Label>();
        readonly List<VisualElement> tabs = new List<VisualElement>();
        readonly VisualElement body, pips, portrait, selectButton;
        readonly Label name, role, diffText, activeTag;
        string active, sel, tab = "overview";
        bool pictured;

        public string Highlighted => sel;
        public string Tab => tab;

        public CommandersScreen()
        {
            root = Ui.Box("op-screen op-col", true);
            root.Add(Ui.Text("COMMANDERS", "op-h1"));
            var cols = Ui.Box("op-row op-grow");
            root.Add(cols);

            var roster = Ui.Box("op-roster");
            cols.Add(roster);
            foreach (var p in GameData.Commanders.Properties())
            {
                if (p.Name.StartsWith("_")) continue;
                string id = p.Name;
                ids.Add(id);
                var h = p.Value;
                var row = Ui.Button("op-button op-roster__row", () => ClickRow(id));
                var left = Ui.Box("op-col");
                left.Add(Ui.Text(GameData.S(h, "name", id).ToUpper(), "op-roster__name"));
                left.Add(Ui.Text(GameData.S(h["info"], "role", ""), "op-roster__role"));
                row.Add(left);
                var tag = Ui.Text("ACTIVE", "op-tag-active");
                row.Add(tag);
                activeTags[id] = tag;
                rows[id] = row;
                roster.Add(row);
            }

            var panel = Ui.Box("op-panel op-cpanel");
            cols.Add(panel);
            var head = Ui.Box("op-cpanel__head");
            portrait = Ui.Box("op-portrait op-cpanel__portrait");
            head.Add(portrait);
            var who = Ui.Box("op-col op-grow");
            who.Add(name = Ui.Text("", "op-cpanel__name"));
            who.Add(role = Ui.Text("", "op-cpanel__role"));
            var diff = Ui.Box("op-row");
            diff.style.alignItems = Align.Center;
            diff.style.marginTop = 6;
            diff.Add(Ui.Text("Difficulty  ", "op-dim"));
            pips = Ui.Box("op-row");
            for (int i = 0; i < 3; i++) pips.Add(Ui.Box("op-diff-pip"));
            diff.Add(pips);
            diff.Add(diffText = Ui.Text("", "op-dim"));
            who.Add(diff);
            head.Add(who);
            head.Add(activeTag = Ui.Text("ACTIVE COMMANDER", "op-tag-active"));
            panel.Add(head);

            var tabRow = Ui.Box("op-tabs");
            for (int i = 0; i < TabKeys.Length; i++)
            {
                string key = TabKeys[i];
                var b = Ui.TextButton(TabLabels[i], () => SetTab(key), "op-button op-tab");
                tabs.Add(b);
                tabRow.Add(b);
            }
            panel.Add(tabRow);
            body = Ui.Box("op-row op-grow");
            panel.Add(body);

            var foot = Ui.Box("op-footer");
            foot.Add(Ui.TextButton("BACK", () => onBack?.Invoke(), "op-button op-big"));
            foot.Add(Ui.Text("One Commander is used in every mode", "op-dim"));
            foot.Add(selectButton = Ui.TextButton("SELECT COMMANDER", Select, "op-button op-big"));
            root.Add(foot);
            sel = ids.Count > 0 ? ids[0] : "vanguard";
        }

        /// <summary>Shows the screen with the active Commander highlighted.</summary>
        public void Open(string activeId)
        {
            active = activeId;
            sel = ids.Contains(activeId) ? activeId : (ids.Count > 0 ? ids[0] : activeId);
            Refresh();
        }

        void ClickRow(string id)
        {
            if (id == sel) { Select(); return; }
            sel = id;
            Refresh();
        }

        public void SetTab(string key)
        {
            if (System.Array.IndexOf(TabKeys, key) < 0) return;
            tab = key;
            Refresh();
        }

        public void Highlight(string id)
        {
            if (!ids.Contains(id)) return;
            sel = id;
            Refresh();
        }

        /// <summary>Menu up / down (keys or D-pad): highlight the previous / next Commander in the roster.</summary>
        public void Step(int dir)
        {
            if (ids.Count == 0) return;
            int i = ids.IndexOf(sel);
            int n = ids.Count;
            Highlight(ids[(((i < 0 ? 0 : i + dir) % n) + n) % n]);
        }

        public void Select()
        {
            active = sel;
            Refresh();
            onSelect?.Invoke(sel);
        }

        /// <summary>Keeps asking for the portrait until PortraitLibrary has photographed it.</summary>
        public void Tick()
        {
            if (!pictured && root.panel != null) pictured = Ui.Portrait(portrait, "commander", name.text);
        }

        void Refresh()
        {
            var cmds = GameData.Commanders;
            foreach (var id in ids)
            {
                Ui.SetOn(rows[id], id == sel);
                Ui.Show(activeTags[id], id == active);
            }
            for (int i = 0; i < tabs.Count; i++) Ui.SetOn(tabs[i], TabKeys[i] == tab);
            var h = cmds[sel];
            var info = h?["info"];
            name.text = GameData.S(h, "name", sel).ToUpper();
            role.text = $"{GameData.S(info, "role", "")}   -   callsign {GameData.S(h, "callsign", "?")}";
            int d = GameData.I(info, "difficulty", 1);
            for (int i = 0; i < 3; i++) pips[i].EnableInClassList("op-diff-pip--on", i < d);
            diffText.text = "  " + new[] { "Easy", "Medium", "Hard" }[Mathf.Clamp(d - 1, 0, 2)];
            Ui.Show(activeTag, sel == active);
            selectButton.EnableInClassList("op-on", sel != active);
            body.Clear();
            var left = Ui.Box("op-col op-half");
            var right = Ui.Box("op-col op-half");
            right.style.marginLeft = new Length(4, LengthUnit.Percent);
            if (tab == "overview") Overview(h, info, left, right);
            else if (tab == "command") Command(info, left, right);
            else if (tab == "classic") Classic(h, info, left, right);
            else Siege(h, info, left, right);
            body.Add(left);
            body.Add(right);
        }

        // ---------------------------------------------------------------- tabs
        void Overview(JToken h, JToken info, VisualElement left, VisualElement right)
        {
            left.Add(Ui.Para(GameData.S(info, "bio", GameData.S(h, "desc", "")), "op-p op-p--bright"));
            left.Add(Ui.Para(GameData.S(info, "style", "")));
            left.Add(Ui.Heading("STRENGTHS", "op-h3 op-h3--good"));
            left.Add(Ui.Bullets(GameData.List(info, "strengths"), "+", "op-bullet__mark--good"));
            left.Add(Ui.Heading("WEAKNESSES", "op-h3 op-h3--bad"));
            left.Add(Ui.Bullets(GameData.List(info, "weaknesses"), "-", "op-bullet__mark--bad"));
            right.Add(Ui.Heading("AVAILABLE IN", "op-h3"));
            var modes = new List<string>();
            foreach (var m in new[] { "Command Campaign", "Skirmish", "Siege Run", "Command Modes" }) modes.Add("[x] " + m);
            modes.Add((Allowed(h, "campaign") ? "[x] " : "[ ] ") + "Classic Campaign");
            modes.Add((Allowed(h, "endless") ? "[x] " : "[ ] ") + "Classic Endless");
            modes.Add((Allowed(h, "run") ? "[x] " : "[ ] ") + "Classic Siege Run");
            right.Add(Ui.Bullets(modes, ""));
            right.Add(Ui.Para("In this version only Skirmish is playable; the other modes arrive in later milestones."));
        }

        void Command(JToken info, VisualElement left, VisualElement right)
        {
            var rts = GameData.Rts;
            left.Add(Ui.Heading("LEADER PASSIVE", "op-h3"));
            left.Add(Ui.Para(GameData.S(rts["leaders"]?[sel], "desc", ""), "op-p op-p--bright"));
            left.Add(Ui.Bullets(PassiveLines(sel)));
            var hero = rts["units"]?["commander"];
            var ab = hero?["ability"];
            left.Add(Ui.Heading("COMMANDER HERO UNIT", "op-h3"));
            left.Add(Ui.Para($"Fights on the map as a hero and respawns at your HQ after {GameData.I(hero, "respawn", 30)}s. " +
                             $"Ability: {GameData.S(ab, "name", "Rally Cry")} gives nearby units " +
                             $"+{Mathf.RoundToInt(GameData.F(ab, "bonus", 0.3f) * 100)}% for {Num(GameData.F(ab, "duration", 8))}s."));
            left.Add(Ui.Heading("TIPS", "op-h3"));
            left.Add(Ui.Bullets(GameData.List(info?["tips"], "command"), ">", null, "op-bullet__text--dim"));
            right.Add(Ui.Heading("LEADER POWERS", "op-h3"));
            foreach (var r in PowerRows(sel)) right.Add(PowerBox(r));
            right.Add(Ui.Para("Used in the Command Campaign, Skirmish, Command Modes and every Siege Run battle."));
        }

        void Classic(JToken h, JToken info, VisualElement left, VisualElement right)
        {
            left.Add(Ui.Heading("HERO BONUSES", "op-h3"));
            left.Add(Ui.Para(GameData.S(h, "desc", ""), "op-p op-p--bright"));
            left.Add(Ui.Bullets(ClassicLines(h)));
            left.Add(Ui.Heading("WHERE", "op-h3"));
            left.Add(Ui.Para(h?["modes"] is JArray a && a.Count > 0
                ? "Classic Endless and Classic Siege Run only. In the classic campaign, Vanguard deploys instead."
                : "Every classic mode: campaign, Endless, Siege Run and the Daily."));
            right.Add(Ui.Heading("COMMANDER KIT (ALL HEROES)", "op-h3"));
            right.Add(Ui.Bullets(new[]
            {
                "Blaster auto-fires at the nearest alien in range",
                "R: dash, with a moment of invulnerability",
                "V: throw a grenade (B cycles frag / cryo / EMP)",
                "Z: Mech suit ultimate, charged by kills",
                "Weapon pickups, ammo crates and a drone buddy that levels up with you",
                "Gear (helmet, armor, boots) drops from elites and bosses",
            }, "-", null, "op-bullet__text--dim"));
            right.Add(Ui.Heading("TIPS", "op-h3"));
            right.Add(Ui.Bullets(GameData.List(info?["tips"], "classic"), ">", null, "op-bullet__text--dim"));
            right.Add(Ui.Para("Classic Defense is not part of the Unity version yet (whether it is ported is still open)."));
        }

        void Siege(JToken h, JToken info, VisualElement left, VisualElement right)
        {
            var rts = GameData.Rts;
            var lp = rts["leader_powers"]?[sel];
            left.Add(Ui.Heading("SIEGE RUN (COMMAND)", "op-h3"));
            left.Add(Ui.Para("Your leader passive and powers apply in every battle of the run, and your Commander is in every deck. Tech Lab upgrades stack on top of them."));
            var lines = new List<string> { GameData.S(rts["leaders"]?[sel], "desc", "") };
            if (lp != null) lines.Add("Signature power: " + GameData.S(lp, "name", ""));
            left.Add(Ui.Bullets(lines));
            left.Add(Ui.Heading("TIPS", "op-h3"));
            left.Add(Ui.Bullets(GameData.List(info?["tips"], "siege"), ">", null, "op-bullet__text--dim"));
            right.Add(Ui.Heading("CLASSIC SIEGE RUN", "op-h3"));
            if (!Allowed(h, "run"))
            {
                right.Add(Ui.Para("Not available in the classic Siege Run."));
                return;
            }
            right.Add(Ui.Para("Your hero bonuses apply for the whole run, and you start with these tower cards on top of the normal starting deck:"));
            foreach (var kind in GameData.List(GameData.Run["hero_decks"], sel))
            {
                var td = GameData.Tower(kind);
                if (td == null) continue;
                var box = Ui.Box("op-power-box");
                box.Add(Ui.Text(GameData.S(td, "name", kind), "op-power-box__name"));
                box.Add(Ui.Para(GameData.S(td, "description", "")));
                right.Add(box);
            }
            right.Add(Ui.Heading("HERO BONUSES", "op-h3"));
            var cl = ClassicLines(h);
            right.Add(Ui.Bullets(cl.GetRange(0, Mathf.Min(6, cl.Count))));
        }

        static VisualElement PowerBox(string[] r)
        {
            bool sig = r[0] == "V";
            var box = Ui.Box("op-power-box" + (sig ? " op-power-box--sig" : ""));
            var top = Ui.Box("op-row");
            top.style.alignItems = Align.Center;
            top.Add(Ui.Text(r[0], "op-power-box__key"));
            top.Add(Ui.Text(r[1] + (sig ? "  (signature)" : ""), "op-power-box__name"));
            top.Add(Ui.Text(r[2], "op-power-box__stat"));
            box.Add(top);
            box.Add(Ui.Para(r[3]));
            return box;
        }

        // ---------------------------------------------------------------- data -> text (commander_select.py)
        static bool Allowed(JToken hero, string mode)
        {
            var modes = hero?["modes"] as JArray;
            if (modes == null || modes.Count == 0) return true;
            foreach (var m in modes) if ((string)m == mode) return true;
            return false;
        }

        /// <summary>1.35 -> +35%, 0.85 -> -15% (Python pct()).</summary>
        public static string Pct(float mult)
        {
            int v = Mathf.RoundToInt((mult - 1) * 100);
            return (v >= 0 ? "+" : "") + v + "%";
        }

        /// <summary>A number the way Python's :g writes it (16 -> 16, 25.6 -> 25.6).</summary>
        public static string Num(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        static string Title(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

        /// <summary>The Command-mode leader passive as readable lines (Python command_lines).</summary>
        public static List<string> PassiveLines(string cid)
        {
            var rts = GameData.Rts;
            var L = rts["leaders"]?[cid] as JObject ?? new JObject();
            var units = rts["units"];
            var hero = units?["commander"];
            var o = new List<string>();
            if (L["hero_hp"] != null || L["hero_damage"] != null)
            {
                float hp = GameData.F(L, "hero_hp", 1f), dm = GameData.F(L, "hero_damage", 1f);
                float baseHp = GameData.F(hero, "hp", 700), baseDmg = GameData.F(hero, "damage", 16);
                o.Add($"Commander HP {Num(baseHp)} -> {(int)(baseHp * hp + 1e-3f)}, damage {Num(baseDmg)} -> {Num(baseDmg * dm)}");
            }
            if (L["build_cost"] != null) o.Add($"Buildings, expansions, upgrades and towers cost {Pct(GameData.F(L, "build_cost", 1))}");
            if (L["repair_heal"] != null)
                o.Add($"Repair Wave heals {Mathf.RoundToInt(GameData.F(L, "repair_heal", 1) * 100)}% instead of {Mathf.RoundToInt(GameData.F(rts["powers"]?["repair"], "heal", 0.5f) * 100)}%");
            if (L["power_cooldown"] != null) o.Add($"All leader powers recharge {Mathf.RoundToInt((1 - GameData.F(L, "power_cooldown", 1)) * 100)}% faster");
            if (L["power_damage"] != null) o.Add($"Power damage {Pct(GameData.F(L, "power_damage", 1))}");
            if (L["unit_damage"] is JObject ud) foreach (var p in ud.Properties()) o.Add($"{GameData.S(units?[p.Name], "name", p.Name)} damage {Pct((float)p.Value)}");
            if (L["unit_range"] is JObject ur) foreach (var p in ur.Properties()) o.Add($"{GameData.S(units?[p.Name], "name", p.Name)} range {Pct((float)p.Value)}");
            if (L["class_damage"] is JObject cd) foreach (var p in cd.Properties()) o.Add($"{Title(p.Name)} units damage {Pct((float)p.Value)}");
            if (L["class_cost"] is JObject cc) foreach (var p in cc.Properties()) o.Add($"{Title(p.Name)} units cost {Pct((float)p.Value)}");
            if (GameData.B(L, "orbital_burn")) o.Add("Orbital Strike leaves a patch of fire behind");
            if (L["cryo_time"] != null) o.Add($"Cryo Bomb slows and freeze effects last {Pct(GameData.F(L, "cryo_time", 1))} longer");
            if (L["slowed_damage"] != null) o.Add($"Slowed aliens take {Pct(GameData.F(L, "slowed_damage", 1))} damage");
            if (L["building_hp"] != null) o.Add($"Buildings and towers HP {Pct(GameData.F(L, "building_hp", 1))}");
            if (L["start_supplies"] != null) o.Add($"+{GameData.I(L, "start_supplies")} starting Supplies");
            if (L["income"] != null) o.Add($"Income {Pct(GameData.F(L, "income", 1))}");
            return o;
        }

        /// <summary>{key, name, cost and cooldown, detail} for Z X C and the leader's own V power (Python power_rows).</summary>
        public static List<string[]> PowerRows(string cid)
        {
            var rts = GameData.Rts;
            var L = rts["leaders"]?[cid];
            float cdm = GameData.F(L, "power_cooldown", 1f), dm = GameData.F(L, "power_damage", 1f);
            var P = rts["powers"];
            var rows = new List<string[]>();
            System.Func<JToken, string> stat = p =>
            {
                int cost = GameData.I(p, "cost");
                return (cost > 0 ? cost + " Supplies" : "Free") + "  |  " + Mathf.RoundToInt(GameData.F(p, "cooldown") * cdm) + "s";
            };
            var o = P?["orbital"];
            rows.Add(new[] { "Z", GameData.S(o, "name", "Orbital Strike"), stat(o),
                $"{(int)(GameData.F(o, "damage") * dm + 1e-3f)} damage in a wide blast" + (GameData.B(L, "orbital_burn") ? ", leaves fire" : "") });
            var c = P?["cryo"];
            rows.Add(new[] { "X", GameData.S(c, "name", "Cryo Bomb"), stat(c),
                $"Slows by {Mathf.RoundToInt(GameData.F(c, "slow") * 100)}% for {Num(GameData.F(c, "slow_time") * GameData.F(L, "cryo_time", 1f))}s" });
            var r = P?["repair"];
            rows.Add(new[] { "C", GameData.S(r, "name", "Repair Wave"), stat(r),
                $"Heals {Mathf.RoundToInt(GameData.F(L, "repair_heal", GameData.F(r, "heal", 0.5f)) * 100)}% HP to nearby units and buildings" });
            var v = rts["leader_powers"]?[cid];
            if (v != null) rows.Add(new[] { "V", GameData.S(v, "name", ""), stat(v), GameData.S(v, "desc", "") });
            return rows;
        }

        static readonly Dictionary<string, string> StatNames = new Dictionary<string, string>
        {
            { "damage", "damage" }, { "dps", "damage" }, { "burn_dps", "burn damage" }, { "range", "range" },
            { "slow", "slow" }, { "drones", "drones" }, { "max_mines", "mines" }, { "rate", "fire rate" },
        };

        /// <summary>The classic hero bonuses as readable lines (Python classic_lines).</summary>
        public static List<string> ClassicLines(JToken hero)
        {
            var o = new List<string>();
            if (!(hero?["effects"] is JArray effects)) return o;
            foreach (var e in effects)
            {
                string t = GameData.S(e, "type", "");
                float m = GameData.F(e, "mult", 1f), add = GameData.F(e, "add", 0f);
                bool hasMult = e["mult"] != null;
                string tower = GameData.S(e, "tower");
                string tname = tower == null ? "" : GameData.S(GameData.Tower(tower), "name", tower);
                switch (t)
                {
                    case "cmd_hp": o.Add($"Commander HP {Pct(m)}"); break;
                    case "cmd_speed": o.Add($"Commander move speed {Pct(m)}"); break;
                    case "cmd_armor": o.Add($"Commander takes {Mathf.RoundToInt((1 - m) * 100)}% less damage"); break;
                    case "blaster_damage": o.Add($"Blaster damage {Pct(m)}"); break;
                    case "blaster_range": o.Add($"Blaster range {Pct(m)}"); break;
                    case "tower_cost": o.Add($"{(tower == null || tower == "*" ? "All towers" : tname)} cost {Pct(m)}"); break;
                    case "repair_speed": o.Add($"Repairs {Num(m)}x faster"); break;
                    case "orbital_cooldown": o.Add($"Orbital Strike cooldown {Pct(m)}"); break;
                    case "orbital_damage": o.Add($"Orbital Strike damage {Pct(m)}"); break;
                    case "tower_stat":
                    {
                        string raw = GameData.S(e, "stat", "");
                        string stat = StatNames.ContainsKey(raw) ? StatNames[raw] : raw;
                        if (hasMult) o.Add($"{tname} {stat} {Pct(m)}");
                        else if (raw == "slow") o.Add($"{tname} slow +{Mathf.RoundToInt(add * 100)}%");
                        else o.Add($"{tname} +{Num(add)} {(add == 1 && stat.EndsWith("s") ? stat.Substring(0, stat.Length - 1) : stat)}");
                        break;
                    }
                    case "slowed_damage": o.Add($"Slowed aliens take {Pct(m)} damage"); break;
                    case "tower_hp": o.Add($"Tower HP {Pct(m)}"); break;
                    case "wave_heal": o.Add($"Core repairs {Num(add)} HP after every wave"); break;
                    case "start_energy": o.Add($"+{Num(add)} starting Energy"); break;
                    case "wave_energy": o.Add($"+{Num(add)} Energy every wave"); break;
                    case "kill_reward": o.Add($"Energy per kill {Pct(m)}"); break;
                    case "upgrade_discount": o.Add($"Tower upgrades cost {Pct(m)}"); break;
                    case "crit_chance": o.Add($"Towers get a {Mathf.RoundToInt(add * 100)}% chance to crit"); break;
                    default: o.Add(t.Replace("_", " ")); break;
                }
            }
            return o;
        }
    }
}
