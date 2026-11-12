using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Outpost.Sim;
using Outpost.View;

namespace Outpost.UI
{
    /// <summary>
    /// Bottom-left of the command bar: what is selected. One thing shows its portrait, health,
    /// stats and (for buildings) what it is making; several things show a tile per unit type.
    /// </summary>
    public class SelectionPanel
    {
        public readonly VisualElement root;
        readonly VisualElement single, multi, portrait, hpFill, shieldBar, shieldFill, workBar, workFill, queue, groups;
        readonly Label title, hp, line1, line2, line3, multiTitle;
        Battle battle;
        CommandInput input;
        string shownModel, queueSig, groupSig;
        bool portraitDone;

        public SelectionPanel()
        {
            root = Ui.Box("op-panel op-selection", true);

            single = Ui.Box("");
            single.style.flexDirection = FlexDirection.Row;
            single.style.flexGrow = 1;
            var left = Ui.Box("op-sel-left");
            left.Add(portrait = Ui.Box("op-portrait"));
            left.Add(hp = Ui.Text("", "op-hp"));
            left.Add(Ui.Bar(out hpFill));
            left.Add(shieldBar = Ui.Bar(out shieldFill, "op-bar--thin", "op-bar__fill--shield"));
            var body = Ui.Box("op-sel-body");
            body.Add(title = Ui.Text("", "op-title"));
            body.Add(line1 = Ui.Text("", "op-line"));
            body.Add(line2 = Ui.Text("", "op-line op-dim"));
            body.Add(line3 = Ui.Text("", "op-line"));
            body.Add(workBar = Ui.Bar(out workFill, null, "op-bar__fill--work"));
            body.Add(queue = Ui.Box("op-queue"));
            single.Add(left);
            single.Add(body);

            multi = Ui.Box("");
            multi.style.flexGrow = 1;
            multi.Add(multiTitle = Ui.Text("", "op-title"));
            multi.Add(groups = Ui.Box("op-groups"));

            root.Add(single);
            root.Add(multi);
        }

        public void Bind(Battle b, CommandInput inp)
        {
            battle = b;
            input = inp;
            shownModel = queueSig = groupSig = null;
        }

        public void Refresh()
        {
            var sel = input.selection.Where(e => e.alive).ToList();
            Ui.Show(root, sel.Count > 0);
            if (sel.Count == 0) return;
            Ui.Show(single, sel.Count == 1);
            Ui.Show(multi, sel.Count > 1);
            if (sel.Count == 1) Single(sel[0]);
            else Many(sel);
        }

        // ------------------------------------------------------------------ one thing
        void Single(Entity e)
        {
            string model = EntityView.ModelNameFor(e);
            if (model != shownModel)
            {
                shownModel = model;
                portraitDone = false;
            }
            if (!portraitDone) portraitDone = Ui.Portrait(portrait, model, e.name);

            bool mine = e.faction == "player";
            title.text = e.name;
            title.EnableInClassList("op-title--player", mine);
            title.EnableInClassList("op-title--alien", !mine);
            hp.text = $"{Mathf.CeilToInt(e.hp)} / {Mathf.CeilToInt(e.maxHp)}";
            Ui.SetFill(hpFill, e.Ratio);
            hpFill.style.backgroundColor = Color.Lerp(new Color(1f, 0.3f, 0.2f), mine ? new Color(0.45f, 0.92f, 0.55f) : new Color(1f, 0.6f, 0.3f), e.Ratio);
            Ui.Show(shieldBar, e.shield > 0.5f);
            if (e.shield > 0.5f) Ui.SetFill(shieldFill, e.shield / Mathf.Max(1f, e.maxHp * 0.3f));

            float work = -1;
            string l1 = "", l2 = "", l3 = "";
            Building queueOf = null;
            if (e is Unit u)
            {
                string squad = u.squad > 1 ? $"Squad {u.Members}/{u.squad}    " : "";
                string rank = u.rank > 0 ? $"    Rank {u.rank}" : "";
                l1 = u.damage > 0
                    ? $"{squad}Damage {u.damage:0}{(u.squad > 1 ? " each" : "")} x{u.FireRate:0.#}/s    Range {u.AttackRange:0}    Speed {u.Speed:0}{rank}"
                    : $"{squad}Unarmed    Speed {u.Speed:0}{rank}";
                l2 = GameData.S(u.data, "desc", "");
                var notes = new List<string>();
                notes.Add(u.hitsAir ? "Hits air and ground" : "Ground targets only");
                notes.Add($"Armor {u.armor:0}");
                if (u.buffs.Count > 0) notes.Add("<color=#ffd77a>" + string.Join(", ", u.buffs.Select(kv => $"{kv.Key} {Ui.Secs(kv.Value)}")) + "</color>");
                l3 = string.Join("    ", notes);
            }
            else if (e is Tower t)
            {
                l1 = t.damage > 0 ? $"Damage {t.damage:0} x{t.rate:0.#}/s    Range {t.range:0}    {(t.hitsAir ? "Hits air and ground" : "Ground targets only")}" : "";
                l2 = GameData.S(t.data, "desc", "");
                if (!t.Done) { work = 1 - t.buildLeft / t.buildTotal; l3 = $"Building... {Ui.Pct(work)}"; }
            }
            else if (e is Building b)
            {
                l2 = GameData.S(b.data, "desc", "");
                if (!b.Done) { work = 1 - b.buildLeft / b.buildTotal; l1 = $"Building... {Ui.Pct(work)}"; }
                else
                {
                    var bits = new List<string>();
                    if (b.Income > 0) bits.Add($"Income +{b.Income * battle.factions[b.faction].incomeMult:0.0}/s");
                    if (b.IsHq)
                    {
                        var bs = b.baseRef;
                        bits.Add($"Slots {bs.slots.Take(bs.SlotLimit).Count(s => s != null)}/{bs.SlotLimit}");
                        bits.Add($"Turrets {bs.sockets.Count(s => s != null)}/{bs.sockets.Length}");
                    }
                    l1 = string.Join("    ", bits);
                    if (mine && b.queue.Count > 0)
                    {
                        var q = b.queue[0];
                        work = 1 - q.t / q.total;
                        queueOf = b;
                    }
                    else if (mine && b.trains.Length > 0) l3 = "<color=#96a6bc>Right-click the ground to set a rally point</color>";
                }
            }
            line1.text = l1;
            line2.text = l2;
            line3.text = l3;
            Ui.Show(line1, l1.Length > 0);
            Ui.Show(line2, l2.Length > 0);
            Ui.Show(line3, l3.Length > 0);
            Ui.Show(workBar, work >= 0);
            if (work >= 0) Ui.SetFill(workFill, work);
            Queue(queueOf);
        }

        static string QueueName(Building b, QueueItem q) =>
            q.type == "research" ? GameData.S(b.Research?[q.id], "name", q.id)
            : q.type == "upgrade" ? GameData.S(b.Upgrade, "name", "Upgrade")
            : GameData.S(GameData.Unit(q.id), "name", q.id);

        void Queue(Building b)
        {
            Ui.Show(queue, b != null);
            if (b == null)
            {
                queueSig = null;
                return;
            }
            string sig = b.id + ":" + string.Join(",", b.queue.Select(q => q.id));
            if (sig != queueSig)
            {
                queueSig = sig;
                queue.Clear();
                for (int i = 0; i < b.queue.Count; i++)
                {
                    int index = i;
                    var item = Ui.Button("op-queue__item", () => battle.CancelQueue(b, index));
                    item.Add(Ui.Text(QueueName(b, b.queue[i])));
                    item.tooltip = "Click to cancel";
                    queue.Add(item);
                }
            }
            if (queue.childCount > 0 && b.queue.Count > 0)
            {
                var q = b.queue[0];
                queue[0].Q<Label>().text = $"{QueueName(b, q)}  {Ui.Pct(1 - q.t / q.total)}";
            }
        }

        // ------------------------------------------------------------------ several things
        void Many(List<Entity> sel)
        {
            int ready = sel.OfType<Unit>().Count(u => u.faction == "player" && battle.AbilityReady(u));
            multiTitle.text = $"{sel.Count} selected" + (ready > 0 ? $"   <size=14><color=#ffd77a>{ready} abilit{(ready == 1 ? "y" : "ies")} ready</color></size>" : "");
            var byKind = sel.GroupBy(e => e.kind).ToList();
            string sig = string.Join(",", byKind.Select(g => g.Key + g.Count()));
            if (sig != groupSig)
            {
                groupSig = sig;
                groups.Clear();
                foreach (var g in byKind.Take(10))
                {
                    var members = g.ToList();
                    var first = members[0];
                    var tile = Ui.Button("op-group", () => input.Select(members.Where(m => m.alive).ToList()));
                    var icon = Ui.Box("op-cmd__icon");
                    tile.Add(icon);
                    tile.userData = EntityView.ModelNameFor(first);
                    tile.Add(Ui.Text(first.name, "op-cmd__label"));
                    tile.Add(Ui.Text("", "op-cmd__sub"));
                    tile.tooltip = "Click to keep just these";
                    groups.Add(tile);
                }
            }
            int i = 0;
            foreach (var g in byKind.Take(10))
            {
                if (i >= groups.childCount) break;
                var tile = groups[i++];
                float cur = g.Sum(e => e.hp), max = g.Sum(e => e.maxHp);
                tile.Q<Label>(className: "op-cmd__sub").text = $"x{g.Count()}   {Ui.Pct(cur / Mathf.Max(1f, max))}";
                if (tile.userData is string model)
                {
                    if (Ui.Portrait(tile.Q(className: "op-cmd__icon"), model, null)) tile.userData = null;
                }
            }
        }
    }
}
