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
        public string action;           // Hotkeys id whose key the button shows (else its card grid key)
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

        public static readonly string[] PlayerBuild = { "supply", "barracks", "depot", "reactor", "airpad", "armory", "sensor", "shieldgen", "specops" };
        // the turret page offers the player's loadout (picked on the Skirmish setup screen) plus the Bunker
        bool towerPage;     // the Command Post card shows its turret page

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
                    id = "attack", label = "Attack-move", action = "attack_move", tip = "<b>Attack-move</b>\nLeft click a target or the ground. Units fight anything they meet on the way.",
                    lit = () => input.attackMode,
                    click = () => { input.attackMode = true; input.abilityMode = false; },
                });
                list.Add(new Cmd { id = "stop", label = "Stop", action = "stop", tip = "<b>Stop</b>\nDrop the current order.", click = () => battle.OrderStop(input.MyUnits, false) });
                list.Add(new Cmd { id = "hold", label = "Hold", action = "hold", tip = "<b>Hold position</b>\nStay put and only shoot what comes into range.", click = () => battle.OrderStop(input.MyUnits, true) });
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
                        id = "ab:" + aid, label = GameData.S(one.ability, "name", "Ability"), model = one.kind, action = first ? "ability" : null,
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
            if (sel.Count == 1 && sel[0] is Tower tw && tw.faction == "player") return TowerCommands(tw, list, out signature);
            if (sel.Count != 1 || !(sel[0] is Building b) || b.faction != "player") return list;
            if (!b.IsHq) towerPage = false;
            signature = $"b:{b.id}:{b.Done}:{b.upgraded}:{towerPage}";
            if (!b.IsHq)
                list.Add(new Cmd
                {
                    id = "recycle", label = "Recycle", action = "recycle",
                    tip = "<b>Recycle</b>\nRemoves this building and frees its slot. You get half its cost back, or all of it while it is still being built.",
                    sub = () => "+" + Mathf.RoundToInt(battle.BuildCost(b.kind) * (b.Done ? 0.5f : 1f)),
                    click = () => Report(battle.Demolish(b)),
                });
            if (!b.Done) return list;

            if (b.IsHq && towerPage)
            {
                var tb = b.baseRef;
                list.Add(new Cmd { id = "back", label = "Back", tip = "<b>Back</b>\nBack to buildings.", click = () => { towerPage = false; sig = null; } });
                foreach (var k in battle.Player.loadout.Concat(new[] { "bunker" }))
                {
                    bool bunker = k == "bunker";
                    var d = bunker ? (Newtonsoft.Json.Linq.JObject)GameData.Rts["bunker"] : GameData.Tower(k);
                    if (d == null) continue;
                    string kind = k;
                    string desc = bunker ? GameData.S(d, "desc", "") : GameData.S(d, "description", "");
                    list.Add(new Cmd
                    {
                        id = "tower:" + k, label = GameData.S(d, "name", k), model = bunker ? "bld_bunker" : "tur_" + k,
                        tip = Tips.Tower(battle, k),
                        enabled = () => battle.FreeSocket(tb) >= 0,
                        sub = () => battle.FreeSocket(tb) < 0 ? "no sockets" : battle.TowerCost(kind).ToString(),
                        click = () => Report(battle.BuildTower(tb, kind)),
                    });
                }
                return list;
            }

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
                        tip = Tips.Building(battle, k),
                        enabled = () => P.tech >= tech && battle.FreeSlot(bs) >= 0,
                        sub = () => P.tech < tech ? $"Tech {tech}" : battle.FreeSlot(bs) < 0 ? "no slots" : battle.BuildCost(kind).ToString(),
                        click = () => Report(battle.Build(bs, kind)),
                    });
                }
                list.Add(new Cmd
                {
                    id = "towers", label = "Turrets", model = "tur_pulse",
                    tip = "<b>Turrets and Bunker</b>\nBuild on this base's turret sockets.",
                    sub = () => $"{bs.sockets.Count(s => s != null)}/{bs.sockets.Length} sockets",
                    click = () => { towerPage = true; sig = null; },
                });
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
                    tip = Tips.Unit(battle, k),
                    enabled = () => P.tech >= tech,
                    sub = () => P.tech < tech ? $"Tech {tech}" : battle.UnitCost("player", kind).ToString(),
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
                        tip = Tips.Research(battle, rd, rid, research),
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

        /// <summary>A selected turret: upgrade to the next tier, sell; a Bunker: unload.</summary>
        List<Cmd> TowerCommands(Tower t, List<Cmd> list, out string signature)
        {
            signature = $"t:{t.id}:{t.Done}:{t.tier}";
            if (t.MaxTier > 1)
            {
                int next = Mathf.Min(t.tier + 1, t.MaxTier - 1);
                var nst = t.tiers[next];
                list.Add(new Cmd
                {
                    id = "tupgrade", label = t.tier + 1 >= t.MaxTier ? "Max tier" : $"Upgrade to tier {t.tier + 2}", model = EntityView.ModelNameFor(t),
                    tip = Tips.TowerUpgrade(battle, t),
                    enabled = () => t.Done && t.UpgradeCost() >= 0 && battle.Player.res >= t.UpgradeCost(),
                    sub = () => t.UpgradeCost() < 0 ? "max" : !t.Done ? "building" : t.UpgradeCost().ToString(),
                    click = () => Report(battle.UpgradeTower(t)),
                });
            }
            list.Add(new Cmd
            {
                id = "sell", label = "Sell", action = "recycle",
                tip = "<b>Sell</b>\nRemoves the turret and frees its socket. You get 60% of everything paid for it back." +
                      (t.IsBunker ? " Squads inside walk out unhurt." : ""),
                sub = () => "+" + battle.SellValue(t),
                click = () => Report(battle.SellTower(t)),
            });
            if (t.IsBunker)
                list.Add(new Cmd
                {
                    id = "unload", label = "Unload",
                    tip = "<b>Unload</b>\nThe squads inside come out. To garrison, select infantry and right click the Bunker.",
                    enabled = () => t.garrison.Count > 0,
                    sub = () => $"{t.garrison.Count}/{t.capacity}",
                    click = () => battle.Unload(t),
                });
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
            s += "|k" + Hotkeys.Version;
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

        /// <summary>A card hotkey (Alt + grid key): clicks button n (0-14) if it is there and enabled.</summary>
        public bool Press(int index)
        {
            if (input == null || battle == null) return false;
            Refresh();
            if (index < 0 || index >= buttons.Count) return false;
            var c = buttons[index].cmd;
            if (c.enabled != null && !c.enabled()) return false;
            c.click?.Invoke();
            return true;
        }

        /// <summary>Test helper: the ids of the buttons now on the card, in grid order.</summary>
        public List<string> ButtonIds() => buttons.Select(b => b.cmd.id).ToList();

        void Rebuild(List<Cmd> cmds)
        {
            root.Clear();
            buttons.Clear();
            Ui.Show(tip, false);
            int slot = 0;
            foreach (var c in cmds.Take(15))
            {
                var cmd = c;
                string keyText = c.key ?? Hotkeys.Label(c.action ?? "card_" + (slot + 1));
                slot++;
                var el = Ui.Button("op-cmd", () => cmd.click?.Invoke());
                var icon = Ui.Box("op-cmd__icon");
                var fill = Ui.Box("op-cmd__fill");
                var sub = Ui.Text("", "op-cmd__sub");
                el.Add(icon);
                el.Add(fill);
                el.Add(Ui.Text(c.label, "op-cmd__label"));
                el.Add(sub);
                Ui.Show(sub, c.sub != null);
                if (!string.IsNullOrEmpty(keyText)) el.Add(Ui.Text(keyText, "op-cmd__key"));
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
