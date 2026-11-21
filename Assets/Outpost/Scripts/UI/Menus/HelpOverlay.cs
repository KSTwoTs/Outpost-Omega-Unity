// HelpOverlay.cs · roadmap step M2 (filled Nov 21, Milestone 2 group 4).
// Help in a battle: the controls overlay (written from the Hotkeys table, so rebinds show up), the tooltip text with
// real numbers (Tips: units, buildings, turrets per tier, research), and the first-match hints (FirstMatchHints).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Outpost.Sim;
using Outpost.View;

namespace Outpost.UI
{
    /// <summary>
    /// The controls overlay (F1). Every row comes from Hotkeys.Actions (battle context), so a rebind in Settings
    /// changes it at once (rebuilt when Hotkeys.Version changes). Numbered families (groups 1-9, card buttons,
    /// leader powers) take one row each. Mouse controls are fixed text (they can't be rebound).
    /// </summary>
    public class HelpOverlay
    {
        public readonly VisualElement root;
        readonly VisualElement left, right;
        int version = -1;
        public bool Visible { get; private set; }
        /// <summary>Close button (HudRoot hides the overlay).</summary>
        public Action onClose;

        static readonly string[] LeftGroups = { "Mouse", "Orders", "Selection", "Powers" };

        public HelpOverlay()
        {
            root = Ui.Box("op-panel op-helpov", true);
            var head = Ui.Box("op-helpov__head");
            head.Add(Ui.Text("CONTROLS", "op-helpov__title"));
            head.Add(Ui.TextButton("Close", () => onClose?.Invoke(), "op-button op-helpov__close"));
            root.Add(head);
            var cols = Ui.Box("op-helpov__cols");
            cols.Add(left = Ui.Box("op-helpov__col"));
            cols.Add(right = Ui.Box("op-helpov__col"));
            root.Add(cols);
            root.Add(Ui.Text("Keys can be changed in Settings > Controls (pause menu). Goal: destroy every alien base.", "op-helpov__foot"));
            Ui.Show(root, false);
        }

        public void Show(bool on)
        {
            Visible = on;
            Ui.Show(root, on);
            if (on) Refresh();
        }

        public void Toggle() => Show(!Visible);

        /// <summary>Rebuilds the rows if a key binding changed since the last build.</summary>
        public void Refresh()
        {
            if (version == Hotkeys.Version) return;
            version = Hotkeys.Version;
            left.Clear();
            right.Clear();
            foreach (var s in Sections())
            {
                var col = LeftGroups.Contains(s.group) ? left : right;
                col.Add(Ui.Text(s.group.ToUpper(), "op-helpov__group"));
                foreach (var r in s.rows)
                {
                    var row = Ui.Box("op-helpov__row");
                    row.Add(Ui.Text(r.keys, "op-helpov__key"));
                    row.Add(Ui.Text(r.what, "op-helpov__what"));
                    col.Add(row);
                }
            }
        }

        static readonly Dictionary<string, string> FamilyText = new Dictionary<string, string>
        {
            { "group_", "Recall a control group (tap twice: jump to it)" },
            { "set_group_", "Make the selection a control group" },
            { "power_", "Leader powers (or click the bar at the top), then left click the target" },
            { "card_", "Command card buttons, left to right and top to bottom" },
        };

        static readonly Regex Numbered = new Regex(@"(.*_)(\d+)$");

        /// <summary>The overlay's content: (group, rows of (keys, what)) in table order, Mouse first.</summary>
        public static List<(string group, List<(string keys, string what)> rows)> Sections()
        {
            var res = new List<(string, List<(string, string)>)>();
            res.Add(("Mouse", new List<(string, string)>
            {
                ("Left click / drag", "Select (Shift adds, double-click takes every unit of that type)"),
                ("Right click", "Move, attack, garrison a Bunker; on a building: rally point"),
                ("Middle drag", "Pan the camera (Alt + middle drag turns it)"),
                ("Wheel", "Zoom"),
                ("Minimap", "Left click moves the camera, right click sends the selection"),
                ("Hover", "Any button shows its cost and numbers"),
            }));
            bool debug = Application.isEditor || Debug.isDebugBuild;
            var families = new HashSet<string>();
            foreach (var a in Hotkeys.Actions)
            {
                if (a.context != Hotkeys.Battle) continue;
                if (a.id == "reveal_map" && !debug) continue;
                var rows = res.FirstOrDefault(s => s.Item1 == a.group).Item2;
                if (rows == null)
                {
                    rows = new List<(string, string)>();
                    res.Add((a.group, rows));
                }
                if (a.id.StartsWith("cam_") && (a.id == "cam_down" || a.id == "cam_left" || a.id == "cam_right")) continue;
                if (a.id == "cam_up")
                {
                    rows.Add((string.Join("  ", new[] { "cam_up", "cam_left", "cam_down", "cam_right" }.Select(Hotkeys.Label)),
                        "Move the camera (or push the mouse to the screen edge)  <color=#96a6bc>(pad: left stick)</color>"));
                    continue;
                }
                var m = Numbered.Match(a.id);
                if (m.Success && FamilyText.ContainsKey(m.Groups[1].Value))
                {
                    string fam = m.Groups[1].Value;
                    if (!families.Add(fam)) continue;
                    var ids = Hotkeys.Actions.Where(x => x.id.StartsWith(fam) && Numbered.IsMatch(x.id) && Numbered.Match(x.id).Groups[1].Value == fam)
                        .Select(x => x.id).ToList();
                    string keys = ids.Count <= 4
                        ? string.Join("  ", ids.Select(Hotkeys.Label))
                        : Hotkeys.Label(ids[0]) + "  ...  " + Hotkeys.Label(ids[ids.Count - 1]);
                    rows.Add((keys, FamilyText[fam]));
                    continue;
                }
                string what = a.name + (string.IsNullOrEmpty(a.desc) ? "" : ": " + a.desc);
                if (a.pad != null) what += "  <color=#96a6bc>(pad: " + a.pad + ")</color>";
                string k = Hotkeys.Labels(a.id);
                rows.Add((k.Length > 0 ? k : "(unbound)", what));
            }
            return res;
        }

        /// <summary>The whole overlay as plain lines (tests and logs).</summary>
        public static string Text() =>
            string.Join("\n", Sections().Select(s => s.group + ": " + string.Join(" | ", s.rows.Select(r => r.keys + " = " + r.what))));
    }

    /// <summary>
    /// Tooltip text with the numbers a player decides on. Everything is read from the data and the running battle
    /// (leader passives, research, difficulty never touch the player side's numbers), never typed in.
    /// </summary>
    public static class Tips
    {
        public const string Dim = "#96a6bc", Good = "#78eb96", Bad = "#ff8270", Gold = "#ffd77a";
        static readonly string[] Classes = { "inf", "veh", "air", "hero", "bld" };

        public static string ClassName(string c) =>
            c == "inf" ? "infantry" : c == "veh" ? "vehicles" : c == "air" ? "air" : c == "hero" ? "heroes" : c == "bld" ? "buildings" : c;

        static string N(float v) => v >= 100 || Mathf.Approximately(v, Mathf.Round(v)) ? Mathf.RoundToInt(v).ToString() : v.ToString("0.#");
        static string X(float m) => "x" + m.ToString("0.##");

        /// <summary>Damage multiplier of a unit against a target class: counter table x its own bonus table.</summary>
        public static float Against(JToken unit, string target) =>
            GameData.Counter(GameData.S(unit, "class", "inf"), target) * GameData.F(unit?["bonus"], target, 1f);

        /// <summary>Strong vs / weak vs / takes extra damage from, from rts.json counters and the unit's bonus table.</summary>
        public static string Matchup(JToken unit, bool shortForm = false)
        {
            if (unit == null) return "";
            string cls = GameData.S(unit, "class", "inf");
            bool armed = GameData.F(unit, "damage") > 0;
            bool air = GameData.B(unit, "air"), ground = GameData.B(unit, "ground", true);
            var strong = new List<string>();
            var weak = new List<string>();
            if (armed)
                foreach (var t in Classes)
                {
                    if (t == "air" && !air) continue;
                    if (t != "air" && !ground) continue;
                    float m = Against(unit, t);
                    if (m >= 1.25f) strong.Add(ClassName(t) + (shortForm ? "" : " " + X(m)));
                    else if (m <= 0.75f) weak.Add(ClassName(t) + (shortForm ? "" : " " + X(m)));
                }
            var hurts = new List<string>();
            foreach (var a in Classes)
            {
                float m = GameData.Counter(a, cls);
                if (m >= 1.25f) hurts.Add(ClassName(a) + (shortForm ? "" : " " + X(m)));
            }
            var parts = new List<string>();
            if (strong.Count > 0) parts.Add($"<color={Good}>Strong vs {string.Join(", ", strong)}</color>");
            if (weak.Count > 0) parts.Add($"<color={Bad}>weak vs {string.Join(", ", weak)}</color>");
            if (armed && !air) parts.Add("can't hit air");
            if (armed && !ground) parts.Add("hits only air");
            if (!shortForm && hurts.Count > 0) parts.Add("takes extra damage from " + string.Join(", ", hurts));
            return string.Join(shortForm ? " · " : "; ", parts);
        }

        /// <summary>A unit the player can train: cost, pop, time, HP, damage, range, speed, counters, ability.</summary>
        public static string Unit(Battle b, string kind)
        {
            var d = GameData.Unit(kind);
            if (d == null) return kind;
            var mods = b.UnitMods("player", kind);
            int squad = Mathf.Max(1, GameData.I(d, "squad", 1));
            float hp = GameData.F(d, "hp", 50) * mods.hp, dmg = GameData.F(d, "damage") * mods.dmg, rate = GameData.F(d, "rate", 1);
            float rng = GameData.F(d, "range", 100) * mods.rng, spd = GameData.F(d, "speed", 60) * mods.spd;
            var lines = new List<string>();
            lines.Add($"<b>{GameData.S(d, "name", kind)}</b>   {b.UnitCost("player", kind)} Supplies · pop {GameData.I(d, "pop", 1)} · {N(GameData.F(d, "time", 10))} s");
            string body = squad > 1 ? $"Squad of {squad} · HP {N(hp)} each ({N(hp * squad)})" : $"HP {N(hp)}";
            body += $" · armor {N(GameData.F(d, "armor"))} · {ClassName(GameData.S(d, "class", "inf"))}";
            lines.Add(body);
            if (dmg > 0)
                lines.Add($"Damage {N(dmg)}{(squad > 1 ? " each" : "")} x{rate:0.#}/s = {N(dmg * rate * squad)} per second · range {N(rng)} · speed {N(spd)}" +
                          (GameData.F(d, "splash") > 0 ? $" · splash {N(GameData.F(d, "splash"))}" : ""));
            else lines.Add($"Unarmed · speed {N(spd)}");
            string mu = Matchup(d);
            if (mu.Length > 0) lines.Add(mu + ".");
            var ab = d["ability"];
            if (ab != null)
            {
                string need = GameData.S(ab, "needs");
                string needName = need == null ? null : FindResearchName(need);
                bool locked = need != null && !b.Player.research.Contains(need);
                lines.Add($"Ability: {GameData.S(ab, "name", "ability")}, cooldown {N(GameData.F(ab, "cooldown", 10))} s" +
                          (locked ? $" <color={Gold}>(needs {needName ?? need})</color>" : ""));
            }
            int tech = GameData.I(d, "tech", 1);
            if (b.Player.tech < tech) lines.Add($"<color={Gold}>Needs Tech {tech} (upgrade the Command Post)</color>");
            string desc = GameData.S(d, "desc", "");
            if (desc.Length > 0) lines.Add($"<color={Dim}>{desc}</color>");
            return string.Join("\n", lines);
        }

        static string FindResearchName(string id)
        {
            if (GameData.Rts["buildings"] is JObject bs)
                foreach (var p in bs.Properties())
                {
                    var r = p.Value?["research"]?[id];
                    if (r != null) return GameData.S(r, "name", id) + " at the " + GameData.S(p.Value, "name", p.Name);
                }
            return null;
        }

        /// <summary>A building the Command Post can place: cost, time, HP, income, what it trains and researches.</summary>
        public static string Building(Battle b, string kind)
        {
            var d = GameData.Building(kind);
            if (d == null) return kind;
            var lines = new List<string>();
            lines.Add($"<b>{GameData.S(d, "name", kind)}</b>   {b.BuildCost(kind)} Supplies · {N(GameData.F(d, "time", 20))} s");
            string body = $"HP {N(GameData.F(d, "hp", 1000) * b.BuildingHpMult("player"))} · armor {N(GameData.F(d, "armor", 2))}";
            if (GameData.F(d, "income") > 0) body += $" · <color={Good}>+{GameData.F(d, "income") * b.Player.incomeMult:0.0#} Supplies/s</color>";
            lines.Add(body);
            if (d["trains"] is JArray tr && tr.Count > 0)
                lines.Add("Trains: " + string.Join(", ", tr.Select(k => GameData.S(GameData.Unit((string)k), "name", (string)k))));
            if (d["research"] is JObject rs && rs.Count > 0)
                lines.Add("Research: " + string.Join(", ", rs.Properties().Select(p => GameData.S(p.Value, "name", p.Name))));
            var up = d["upgrade"];
            if (up != null) lines.Add($"Upgrade: {GameData.S(up, "name", "upgrade")} ({GameData.I(up, "cost")} Supplies)");
            int tech = b.BuildTech(kind);
            if (b.Player.tech < tech) lines.Add($"<color={Gold}>Needs Tech {tech} (upgrade the Command Post)</color>");
            string desc = GameData.S(d, "desc", "");
            if (desc.Length > 0) lines.Add($"<color={Dim}>{desc}</color>");
            return string.Join("\n", lines);
        }

        /// <summary>Research: cost, time, what it needs.</summary>
        public static string Research(Battle b, JToken research, string id, JObject all)
        {
            var lines = new List<string>();
            lines.Add($"<b>Research: {GameData.S(research, "name", id)}</b>   {GameData.I(research, "cost")} Supplies · {N(GameData.F(research, "time", 30))} s");
            string desc = GameData.S(research, "desc", "");
            if (desc.Length > 0) lines.Add(desc);
            int tech = GameData.I(research, "tech", 1);
            string after = GameData.S(research, "after");
            if (b.Player.research.Contains(id)) lines.Add($"<color={Good}>Done</color>");
            else
            {
                if (b.Player.tech < tech) lines.Add($"<color={Gold}>Needs Tech {tech}</color>");
                if (after != null && !b.Player.research.Contains(after))
                    lines.Add($"<color={Gold}>Needs {GameData.S(all?[after], "name", after)} first</color>");
            }
            return string.Join("\n", lines);
        }

        /// <summary>
        /// A turret as the battle would build it at a tier: a throwaway Tower (Random state kept so the tooltip never
        /// changes the battle's dice) with the player's HP bonuses; damage through Battle.TowerDamage (weapon research).
        /// </summary>
        public static (float hp, float damage, float rate, float range, int upCost, bool air, Tower t) TowerAt(Battle b, string kind, int tier)
        {
            var keep = UnityEngine.Random.state;
            var t = new Tower(kind, "player", 0, 0, null, -1, b.Player.research.Contains("turret_plating") && kind != "bunker" ? 1.4f : 1f);
            UnityEngine.Random.state = keep;
            t.tier = Mathf.Clamp(tier, 0, t.MaxTier - 1);
            t.ApplyTier();
            float hp = t.maxHp * (kind == "bunker" ? 1f : b.BuildingHpMult("player"));
            float dmg = t.group == TowerBehaviors.Group.Attack ? b.TowerDamage(t, TowerBehaviors.ShotDamage(t.behavior, t.stats)) : 0f;
            return (hp, dmg, t.rate, t.range, t.UpgradeCost(), t.hitsAir, t);
        }

        static string TowerLine(Battle b, (float hp, float damage, float rate, float range, int upCost, bool air, Tower t) s)
        {
            var t = s.t;
            var bits = new List<string> { $"HP {N(s.hp)}" };
            if (s.range > 0) bits.Add($"range {N(s.range)}");
            switch (t.group)
            {
                case TowerBehaviors.Group.Attack:
                    bits.Add($"damage {N(s.damage)} x{s.rate:0.#}/s = {N(s.damage * s.rate)} per second");
                    if (t.splash > 0) bits.Add($"splash {N(t.splash)}");
                    if (t.chain > 0) bits.Add($"chains to {t.chain}");
                    bits.Add(s.air ? "hits air" : "ground only");
                    break;
                case TowerBehaviors.Group.Slow:
                    float slow = TowerBehaviors.SlowAmount(t.behavior, t.stats), dps = TowerBehaviors.SlowDps(t.stats);
                    if (slow > 0) bits.Add($"slows {Mathf.RoundToInt(slow * 100)}%");
                    if (dps > 0) bits.Add($"{N(b.TowerDamage(t, dps))} damage per second to all in range");
                    break;
                case TowerBehaviors.Group.Income:
                    bits.Add($"<color={Good}>+{GameData.F(t.stats, "income") / 20f:0.0#} Supplies/s</color>");
                    break;
                case TowerBehaviors.Group.Bounty:
                    bits.Add($"+{N(GameData.F(t.stats, "per_kill", 2) * 8)} Supplies per kill in range");
                    break;
            }
            return string.Join(" · ", bits);
        }

        /// <summary>A loadout turret (or the Bunker) on the build page: cost, tier 1 numbers, what upgrades give.</summary>
        public static string Tower(Battle b, string kind)
        {
            bool bunker = kind == "bunker";
            var d = bunker ? GameData.Rts["bunker"] : GameData.Tower(kind);
            if (d == null) return kind;
            var lines = new List<string>();
            lines.Add($"<b>{GameData.S(d, "name", kind)}</b>   {b.TowerCost(kind)} Supplies · {N(bunker ? GameData.F(d, "time", 12) : GameData.F(GameData.Rts["towers"], "build_time", 15))} s");
            var t1 = TowerAt(b, kind, 0);
            if (bunker)
            {
                lines.Add($"HP {N(t1.hp)} · range {N(t1.range)} · holds {t1.t.capacity} infantry squads (+{Mathf.RoundToInt((GameData.F(d, "damage_bonus", 1.25f) - 1) * 100)}% damage)");
                lines.Add($"<color={Dim}>{GameData.S(d, "desc", "")}</color>");
                return string.Join("\n", lines);
            }
            lines.Add($"{TowerBehaviors.CategoryOf(kind)} turret on a base socket.".Substring(0, 1).ToUpper() + $"{TowerBehaviors.CategoryOf(kind)} turret on a base socket.".Substring(1));
            int max = t1.t.MaxTier;
            for (int i = 0; i < max; i++)
            {
                var s = i == 0 ? t1 : TowerAt(b, kind, i);
                string cost = i == 0 ? "" : $" <color={Gold}>(+{TowerAt(b, kind, i - 1).upCost})</color>";
                lines.Add($"Tier {i + 1}{cost}: {TowerLine(b, s)}");
            }
            string desc = GameData.S(d, "description", "");
            if (desc.Length > 0) lines.Add($"<color={Dim}>{desc}</color>");
            return string.Join("\n", lines);
        }

        /// <summary>A placed turret's next tier (the Upgrade button).</summary>
        public static string TowerUpgrade(Battle b, Tower t)
        {
            if (t.UpgradeCost() < 0) return "<b>Fully upgraded</b>";
            var now = TowerAt(b, t.kind, t.tier);
            var next = TowerAt(b, t.kind, t.tier + 1);
            return $"<b>Upgrade to tier {t.tier + 2}</b>   {t.UpgradeCost()} Supplies\n" +
                   $"Now: {TowerLine(b, now)}\nTier {t.tier + 2}: {TowerLine(b, next)}";
        }
    }

    /// <summary>
    /// The first-match hints: a short checklist for a player's first Skirmish (in the spirit of the Python tutorial,
    /// systems/tutorial.py: one step at a time, ticked off when the player has done it). Shown top centre with Next and
    /// Hide hints. BattleSession starts it only when the save says the hints were not finished yet and the setting is
    /// on; onFinished runs once when the last step is closed, the player hides them, or the battle ends with a winner.
    /// </summary>
    public class FirstMatchHints
    {
        public class Hint
        {
            public Func<string> text;
            public Func<bool> done;
        }

        public readonly VisualElement root;
        readonly Label head, body;
        readonly Label nextText;
        Battle battle;
        CommandInput input;
        List<Hint> steps = new List<Hint>();
        int step;
        float flash, startTime;
        int startPop;
        Action onFinished;
        public bool Active { get; private set; }
        public int Step => step;
        public int Count => steps.Count;
        public string CurrentText => Active && step < steps.Count ? steps[step].text() : null;

        public FirstMatchHints()
        {
            root = Ui.Box("op-panel op-hints", true);
            root.Add(head = Ui.Text("", "op-hints__head"));
            root.Add(body = Ui.Text("", "op-hints__text op-wrap"));
            var buttons = Ui.Box("op-hints__buttons");
            var next = Ui.Button("op-button op-hints__button", Next);
            next.Add(nextText = Ui.Text("Next"));
            buttons.Add(next);
            buttons.Add(Ui.TextButton("Hide hints", Dismiss, "op-button op-hints__button"));
            root.Add(buttons);
            Ui.Show(root, false);
        }

        static string K(string id) => "<color=#ffd77a>" + Hotkeys.Labels(id) + "</color>";

        string Name(string building) => GameData.S(GameData.Building(building), "name", building);

        int CardKeyOf(string building)
        {
            // the Command Post card lists the buildings in this order (CommandCard.PlayerBuild)
            int i = Array.IndexOf(CommandCard.PlayerBuild, building);
            return i < 0 ? 1 : i + 1;
        }

        List<Hint> Build()
        {
            var P = battle.Player;
            Func<string, bool> has = k => battle.buildings.Any(x => x.alive && x.faction == "player" && x.kind == k);
            return new List<Hint>
            {
                new Hint { text = () => "Welcome, Commander. Your goal: destroy every alien base. These hints show in your first Skirmish only. Do what they say, or press Next.",
                           done = () => battle.time > startTime + 10f },
                new Hint { text = () => $"Select your Command Post: left click it, or press {K("jump_base")} to jump to your base.",
                           done = () => input.selection.Any(e => e is Building bl && bl.faction == "player" && bl.IsHq) },
                new Hint { text = () => $"Build a {Name("barracks")}: with the Command Post selected, click it on the command card (bottom right) or press {K("card_" + CardKeyOf("barracks"))}. Hover any button for its cost and numbers.",
                           done = () => has("barracks") },
                new Hint { text = () => $"Build a {Name("supply")} too: it raises your income ({K("card_" + CardKeyOf("supply"))}). Supplies and income are at the top left.",
                           done = () => has("supply") },
                new Hint { text = () => $"When the {Name("barracks")} is finished, select it and train a squad. Right click the ground with it selected to set a rally point.",
                           done = () => P.popUsed > startPop },
                new Hint { text = () => $"Select your army: drag a box around it, or press {K("select_army")} for the whole army.",
                           done = () => input.selection.Count(e => e is Unit u && u.faction == "player") >= 2 },
                new Hint { text = () => $"Right click the ground to move. {K("attack_move")} then a left click attack-moves: units fight anything on the way.",
                           done = () => battle.units.Any(u => u.faction == "player" && u.attackMove) },
                new Hint { text = () => "More bases mean more income: bring units to a yellow pad, click the pad and Build base. Relays (white squares on the minimap) also pay while you hold them.",
                           done = () => battle.bases.Count(x => x.faction == "player" && x.Alive) >= 2 || battle.relays.Any(r => r.owner == "player") },
                new Hint { text = () => $"Leader powers: {K("power_1")} {K("power_2")} {K("power_3")} {K("power_4")} or the bar at the top, then left click the target. They cost Supplies and recharge.",
                           done = () => P.powerCd.Values.Any(v => v > 0) },
                new Hint { text = () => "Turrets guard a base: select the Command Post, open Turrets and pick one for a free socket. Select a turret later to upgrade or sell it.",
                           done = () => battle.towers.Any(t => t.alive && t.faction == "player") },
                new Hint { text = () => $"That's the basics. {K("help")} shows every control, {K("pause")} pauses (Settings are in the pause menu). Good luck!",
                           done = () => false },
            };
        }

        /// <summary>Starts the hints for this battle. onDone runs once when they are finished or hidden.</summary>
        public void Begin(Battle b, CommandInput inp, Action onDone)
        {
            battle = b;
            input = inp;
            onFinished = onDone;
            steps = Build();
            step = 0;
            flash = 0;
            startTime = b.time;
            startPop = b.Player.popUsed;
            Active = true;
            Ui.Show(root, true);
            Draw();
        }

        /// <summary>Stops without marking them seen (a new battle starts, or hints are off).</summary>
        public void Stop()
        {
            Active = false;
            onFinished = null;
            Ui.Show(root, false);
        }

        /// <summary>Next button: skips the current step (on the last one: closes and marks them seen).</summary>
        public void Next()
        {
            if (!Active) return;
            if (step >= steps.Count - 1) Finish();
            else Advance();
        }

        /// <summary>Hide hints: closes them for good (marked seen).</summary>
        public void Dismiss()
        {
            if (Active) Finish();
        }

        void Advance()
        {
            step++;
            flash = 0.6f;
            if (step == 4) startPop = battle.Player.popUsed;     // the train step counts from when it is shown
            Draw();
        }

        void Finish()
        {
            Active = false;
            Ui.Show(root, false);
            var f = onFinished;
            onFinished = null;
            f?.Invoke();
        }

        /// <summary>Called by the HUD a few times a second; visible = not paused or ended.</summary>
        public void Tick(float dt, bool visible)
        {
            if (!Active || battle == null) return;
            if (battle.winner != null)
            {
                Finish();
                return;
            }
            Ui.Show(root, visible);
            if (!visible) return;
            flash = Mathf.Max(0f, flash - dt);
            if (step < steps.Count - 1 && steps[step].done()) Advance();
            Draw();
        }

        void Draw()
        {
            if (step >= steps.Count) return;
            head.text = $"FIRST BATTLE  {step + 1}/{steps.Count}";
            head.EnableInClassList("op-hints__head--flash", flash > 0);
            body.text = steps[step].text();
            nextText.text = step >= steps.Count - 1 ? "Got it" : "Next";
        }
    }
}
