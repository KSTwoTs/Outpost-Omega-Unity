using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Outpost.Sim;
using Outpost.View;

namespace Outpost.UI
{
    /// <summary>One button on the command card.</summary>
    public class Cmd
    {
        public string id, label, model, tip, key;
        public Func<string> sub;        // the small line under the picture (cost, cooldown, state)
        public Func<bool> enabled;
        public Func<float> fill;        // 0..1 dark overlay from the bottom (cooldown or progress left)
        public Func<bool> lit;          // highlighted (the mode it starts is active)
        public Action click;
    }

    /// <summary>
    /// Bottom-right of the command bar: a 5 x 3 grid of buttons for whatever is selected. Units get
    /// orders and abilities; the Command Post gets buildings, turrets and its upgrade; other
    /// buildings get the units they train, their research and their upgrade.
    /// </summary>
    public class CommandCard
    {
        public readonly VisualElement root, tip;
        readonly Label tipText;
        readonly List<(Cmd cmd, VisualElement el, VisualElement icon, VisualElement fill, Label sub, bool[] pictured)> buttons =
            new List<(Cmd, VisualElement, VisualElement, VisualElement, Label, bool[])>();
        Battle battle;
        CommandInput input;
        string sig;

        static readonly string[] PlayerBuild = { "supply", "barracks", "depot", "reactor", "airpad", "armory", "sensor", "shieldgen", "specops" };
        // until the loadout screen exists (Milestone 2) the four starter turrets are offered
        static readonly string[] PlayerTowers = { "pulse", "missile", "tesla", "railgun" };

        public CommandCard()
        {
            root = Ui.Box("op-panel op-card", true);
            tip = Ui.Box("op-panel op-tip");
            tip.Add(tipText = Ui.Text("", "op-wrap"));
            Ui.Show(tip, false);
        }

        public void Bind(Battle b, CommandInput inp)
        {
            battle = b;
            input = inp;
            sig = null;
        }

        void Report(string error)
        {
            if (error != null) input.Toast(error);
        }

        // ------------------------------------------------------------------ what to offer
        List<Cmd> Commands(List<Entity> sel, out string signature)
        {
            var list = new List<Cmd>();
            var P = battle.Player;
            var site = sel.Count == 0 ? input.selectedSite : null;
            if (site != null)
            {
                signature = "site:" + site.id;
                int cost = battle.ExpansionCost("player");
                list.Add(new Cmd
                {
                    id = "expand", label = "Build base", model = "bld_hq2",
                    tip = $"<b>Build a new base here</b>   {cost} Supplies\nNeeds your units nearby and no enemies around the pad. " +
                          "A base adds building slots, turret sockets and +10 population.",
                    enabled = () => battle.CanExpand("player", site) == null,
                    sub = () =>
                    {
                        string why = battle.CanExpand("player", site);
                        return why == null || why.StartsWith("Not enough") ? cost.ToString()
                            : why.StartsWith("Clear") ? "enemies near" : why.StartsWith("Need") ? "bring units" : "taken";
                    },
                    click = () =>
                    {
                        string err = battle.Expand("player", site);
                        Report(err);
                        if (err == null) input.selectedSite = null;
                    },
                });
                return list;
            }
            var units = sel.OfType<Unit>().Where(u => u.faction == "player").ToList();
            if (units.Count > 0)
            {
                signature = "u:" + string.Join(",", units.Select(u => u.kind).Distinct());
                list.Add(new Cmd
                {
                    id = "attack", label = "Attack-move", key = "A", tip = "<b>Attack-move</b>\nLeft click a target or the ground. Units fight anything they meet on the way.",
                    lit = () => input.attackMode,
                    click = () => { input.attackMode = true; input.abilityMode = false; },
                });
                list.Add(new Cmd { id = "stop", label = "Stop", key = "S", tip = "<b>Stop</b>\nDrop the current order.", click = () => battle.OrderStop(input.MyUnits, false) });
                list.Add(new Cmd { id = "hold", label = "Hold", key = "H", tip = "<b>Hold position</b>\nStay put and only shoot what comes into range.", click = () => battle.OrderStop(input.MyUnits, true) });
                bool first = true;
                foreach (var g in units.Where(u => u.ability != null).GroupBy(u => GameData.S(u.ability, "id", "")).Take(6))
                {
                    var one = g.First();
                    string aid = g.Key;
                    string need = GameData.S(one.ability, "needs");
                    float cd = Mathf.Max(1f, GameData.F(one.ability, "cooldown", 10));
                    Func<List<Unit>> mine = () => input.MyUnits.Where(u => u.ability != null && GameData.S(u.ability, "id", "") == aid).ToList();
                    Func<bool> locked = () => need != null && !P.research.Contains(need);
                    list.Add(new Cmd
                    {
                        id = "ab:" + aid, label = GameData.S(one.ability, "name", "Ability"), model = one.kind, key = first ? "F" : null,
                        tip = $"<b>{GameData.S(one.ability, "name", "Ability")}</b>  ({one.name})\n{AbilityText(one)}",
                        enabled = () => !locked() && mine().Any(battle.AbilityReady),
                        sub = () =>
                        {
                            if (locked()) return "needs research";
                            var us = mine();
                            int ready = us.Count(battle.AbilityReady);
                            if (ready > 0) return us.Count > 1 ? $"{ready} ready" : "ready";
                            return us.Count > 0 ? Ui.Secs(us.Min(u => u.abilityCd)) : "";
                        },
                        fill = () =>
                        {
                            var us = mine();
                            return locked() || us.Count == 0 || us.Any(battle.AbilityReady) ? 0f : us.Min(u => u.abilityCd) / cd;
                        },
                        lit = () => input.abilityMode && (input.abilityOnly == null || input.abilityOnly == aid),
                        click = () => input.TriggerAbility(aid),
                    });
                    first = false;
                }
                return list;
            }

            signature = "";
            if (sel.Count != 1 || !(sel[0] is Building b) || b.faction != "player") return list;
            signature = $"b:{b.id}:{b.Done}:{b.upgraded}";
            if (!b.IsHq)
                list.Add(new Cmd
                {
                    id = "recycle", label = "Recycle", key = "Del",
                    tip = "<b>Recycle</b>\nRemoves this building and frees its slot. You get half its cost back, or all of it while it is still being built.",
                    sub = () => "+" + Mathf.RoundToInt(battle.BuildCost(b.kind) * (b.Done ? 0.5f : 1f)),
                    click = () => Report(battle.Demolish(b)),
                });
            if (!b.Done) return list;

            if (b.IsHq)
            {
                var bs = b.baseRef;
                foreach (var k in PlayerBuild)
                {
                    var d = GameData.Building(k);
                    if (d == null) continue;
                    string kind = k;
                    int tech = battle.BuildTech(k);
                    list.Add(new Cmd
                    {
                        id = "build:" + k, label = GameData.S(d, "name", k), model = "bld_" + k,
                        tip = $"<b>{GameData.S(d, "name", k)}</b>   {battle.BuildCost(k)} Supplies, {GameData.F(d, "time", 20):0} s\n{GameData.S(d, "desc", "")}",
                        enabled = () => P.tech >= tech && battle.FreeSlot(bs) >= 0,
                        sub = () => P.tech < tech ? $"Tech {tech}" : battle.FreeSlot(bs) < 0 ? "no slots" : battle.BuildCost(kind).ToString(),
                        click = () => Report(battle.Build(bs, kind)),
                    });
                }
                foreach (var k in PlayerTowers)
                {
                    var d = GameData.Tower(k);
                    if (d == null) continue;
                    string kind = k;
                    list.Add(new Cmd
                    {
                        id = "tower:" + k, label = GameData.S(d, "name", k), model = "tur_" + k,
                        tip = $"<b>{GameData.S(d, "name", k)}</b>   {battle.TowerCost(k)} Supplies\nA turret on one of this base's sockets.\n{GameData.S(d, "desc", "")}",
                        enabled = () => battle.FreeSocket(bs) >= 0,
                        sub = () => battle.FreeSocket(bs) < 0 ? "no sockets" : battle.TowerCost(kind).ToString(),
                        click = () => Report(battle.BuildTower(bs, kind)),
                    });
                }
            }

            foreach (var k in b.trains)
            {
                var d = GameData.Unit(k);
                if (d == null) continue;
                string kind = k;
                int tech = GameData.I(d, "tech", 1);
                list.Add(new Cmd
                {
                    id = "train:" + k, label = GameData.S(d, "name", k), model = k,
                    tip = $"<b>{GameData.S(d, "name", k)}</b>   {GameData.I(d, "cost")} Supplies, pop {GameData.I(d, "pop", 1)}, {GameData.F(d, "time", 10):0} s\n{GameData.S(d, "desc", "")}",
                    enabled = () => P.tech >= tech,
                    sub = () => P.tech < tech ? $"Tech {tech}" : GameData.I(d, "cost").ToString(),
                    click = () => Report(battle.QueueUnit(b, kind)),
                });
            }

            var research = b.Research;
            if (research != null)
                foreach (var kv in research)
                {
                    string rid = kv.Key;
                    var rd = kv.Value;
                    int tech = GameData.I(rd, "tech", 1);
                    string after = GameData.S(rd, "after");
                    Func<bool> chained = () => after != null && !P.research.Contains(after);
                    list.Add(new Cmd
                    {
                        id = "research:" + rid, label = GameData.S(rd, "name", rid),
                        tip = $"<b>Research: {GameData.S(rd, "name", rid)}</b>   {GameData.I(rd, "cost")} Supplies, {GameData.F(rd, "time", 30):0} s\n{GameData.S(rd, "desc", "")}",
                        enabled = () => !P.research.Contains(rid) && !P.researching.Contains(rid) && P.tech >= tech && !chained(),
                        sub = () => P.research.Contains(rid) ? "done" : P.researching.Contains(rid) ? "researching"
                            : P.tech < tech ? $"Tech {tech}" : chained() ? "needs " + GameData.S(research[after], "name", after) : GameData.I(rd, "cost").ToString(),
                        click = () => Report(battle.QueueResearch(b, rid)),
                    });
                }

            var up = b.Upgrade;
            if (up != null && !b.upgraded)
            {
                int tech = GameData.I(up, "tech", 1);
                list.Add(new Cmd
                {
                    id = "upgrade", label = "Upgrade: " + GameData.S(up, "name", "Upgrade"), model = EntityView.ModelNameFor(b),
                    tip = $"<b>Upgrade to {GameData.S(up, "name", "Upgrade")}</b>   {GameData.I(up, "cost")} Supplies, {GameData.F(up, "time", 30):0} s\n" +
                          (b.IsHq ? $"{GameData.I(up, "slots", 7)} building slots, +{GameData.I(up, "hp")} HP, +5 population." : $"+{GameData.F(up, "income"):0.0} income per second."),
                    enabled = () => !b.Upgrading && P.tech >= tech,
                    sub = () => b.Upgrading ? "upgrading" : P.tech < tech ? $"Tech {tech}" : GameData.I(up, "cost").ToString(),
                    click = () => Report(battle.QueueUpgrade(b)),
                });
            }

            if (b.data?["scan"] != null)
            {
                float cd = Mathf.Max(1f, GameData.F(b.data["scan"], "cooldown", 45));
                list.Add(new Cmd
                {
                    id = "scan", label = "Scan", model = "bld_sensor",
                    tip = "<b>Scan</b>\nLeft click anywhere on the map to reveal cloaked aliens there for a while.",
                    enabled = () => b.scanCd <= 0,
                    sub = () => b.scanCd > 0 ? Ui.Secs(b.scanCd) : "ready",
                    fill = () => Mathf.Clamp01(b.scanCd / cd),
                    lit = () => input.scanFrom == b,
                    click = () => input.scanFrom = b,
                });
            }
            return list;
        }

        static string AbilityText(Unit u)
        {
            var ab = u.ability;
            var bits = new List<string>();
            if (GameData.F(ab, "damage") > 0) bits.Add($"Damage {GameData.F(ab, "damage"):0}");
            if (GameData.F(ab, "radius") > 0) bits.Add($"radius {GameData.F(ab, "radius"):0}");
            if (GameData.F(ab, "range") > 0) bits.Add($"range {GameData.F(ab, "range"):0}");
            if (GameData.F(ab, "duration") > 0) bits.Add($"lasts {GameData.F(ab, "duration"):0} s");
            bits.Add($"cooldown {GameData.F(ab, "cooldown", 10):0} s");
            string aim = Battle.AbilityAimed(u) ? "Then left click where to use it." : "Goes off at once.";
            return string.Join(", ", bits) + ".\n" + aim;
        }

        // ------------------------------------------------------------------ drawing
        public void Refresh()
        {
            var sel = input.selection.Where(e => e.alive).ToList();
            var cmds = Commands(sel, out string s);
            Ui.Show(root, cmds.Count > 0);
            if (s != sig)
            {
                sig = s;
                Rebuild(cmds);
            }
            foreach (var b in buttons)
            {
                var c = b.cmd;
                b.el.SetEnabled(c.enabled == null || c.enabled());
                if (c.sub != null) b.sub.text = c.sub();
                if (c.fill != null) b.fill.style.height = Length.Percent(Mathf.Clamp01(c.fill()) * 100f);
                b.el.EnableInClassList("op-cmd--lit", c.lit != null && c.lit());
                if (c.model != null && !b.pictured[0]) b.pictured[0] = Ui.Portrait(b.icon, c.model, null);
            }
        }

        void Rebuild(List<Cmd> cmds)
        {
            root.Clear();
            buttons.Clear();
            Ui.Show(tip, false);
            foreach (var c in cmds.Take(15))
            {
                var cmd = c;
                var el = Ui.Button("op-cmd", () => cmd.click?.Invoke());
                var icon = Ui.Box("op-cmd__icon");
                var fill = Ui.Box("op-cmd__fill");
                var sub = Ui.Text("", "op-cmd__sub");
                el.Add(icon);
                el.Add(fill);
                el.Add(Ui.Text(c.label, "op-cmd__label"));
                el.Add(sub);
                Ui.Show(sub, c.sub != null);
                if (c.key != null) el.Add(Ui.Text(c.key, "op-cmd__key"));
                el.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    tipText.text = cmd.tip ?? cmd.label;
                    Ui.Show(tip, true);
                });
                el.RegisterCallback<PointerLeaveEvent>(_ => Ui.Show(tip, false));
                root.Add(el);
                buttons.Add((c, el, icon, fill, sub, new bool[1]));
            }
        }
    }
}
