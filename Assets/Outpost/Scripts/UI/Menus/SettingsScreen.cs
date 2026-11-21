// SettingsScreen.cs · roadmap step M2 (filled Nov 21, Milestone 2 group 3).
// Video, audio, controls (rebinding), gameplay. Python scenes/settings_menu.py + the controls screen.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Outpost.View;

namespace Outpost.UI
{
    /// <summary>One settings row: a label and a list of choices stepped with the arrows.</summary>
    public class SettingOption
    {
        public string tab, id, label, note;
        public string[] choices;
        public System.Func<int> get;
        public System.Action<int> set;
        /// <summary>Toggles and short lists wrap around; long lists stop at the ends.</summary>
        public bool wrap;
    }

    /// <summary>
    /// What the settings screen edits. Game/Settings.cs gives one (the UI layer never reaches up into Game).
    /// BeginEdit starts from the saved settings, the rows change a working copy, Apply uses and saves it, Revert drops it.
    /// </summary>
    public interface ISettingsModel
    {
        void BeginEdit();
        List<SettingOption> Options();
        bool Dirty { get; }
        void Apply();
        void Revert();
    }

    /// <summary>
    /// The settings screen, used from the main menu (GameFlow) and from the pause menu (HudRoot). Tabs VIDEO, AUDIO,
    /// CONTROLS, GAMEPLAY. Rows step through choices with the arrows; APPLY uses and saves the changes, REVERT drops them,
    /// BACK drops unapplied changes. CONTROLS lists every Hotkeys action with two key slots: click a slot, press a key
    /// (Esc cancels, Backspace clears it); a key already used by another action moves to this one and the screen says
    /// which action lost it. Key changes are used at once; REVERT and BACK undo them too until APPLY.
    /// </summary>
    public class SettingsScreen
    {
        public static readonly string[] Tabs = { "VIDEO", "AUDIO", "CONTROLS", "GAMEPLAY" };

        public readonly VisualElement root;
        public System.Action onBack;
        readonly ISettingsModel model;
        readonly Dictionary<string, VisualElement> tabButtons = new Dictionary<string, VisualElement>();
        readonly VisualElement body, applyButton;
        readonly Label status, dirtyNote;
        ScrollView scroll;
        string tab = Tabs[0];
        string listenId;
        int listenSlot, listenEndFrame = -10, builtVersion = -1;
        bool wasDirty;

        public string Tab => tab;
        public bool Listening => listenId != null;
        public string Status => status.text;
        public ISettingsModel Model => model;

        public SettingsScreen(ISettingsModel m)
        {
            model = m;
            root = Ui.Box("op-screen op-col op-settings", true);
            root.Add(Ui.Text("SETTINGS", "op-h1"));
            var tabs = Ui.Box("op-tabs");
            foreach (var t in Tabs)
            {
                string tt = t;
                var b = Ui.TextButton(t, () => SetTab(tt), "op-button op-tab");
                tabButtons[t] = b;
                tabs.Add(b);
            }
            root.Add(tabs);
            body = Ui.Box("op-settings__body");
            root.Add(body);
            root.Add(status = Ui.Text("", "op-settings__status"));
            var foot = Ui.Box("op-settings__foot");
            applyButton = Ui.TextButton("APPLY", Apply);
            foot.Add(applyButton);
            foot.Add(Ui.TextButton("REVERT", Revert));
            foot.Add(Ui.TextButton("BACK", Back));
            foot.Add(dirtyNote = Ui.Text("", "op-settings__note"));
            root.Add(foot);
        }

        /// <summary>Opens on the saved settings.</summary>
        public void Open()
        {
            StopListening();
            model.BeginEdit();
            status.text = "";
            SetTab(tab);
        }

        public void SetTab(string t)
        {
            if (System.Array.IndexOf(Tabs, t) < 0) return;
            StopListening();
            tab = t;
            foreach (var kv in tabButtons) Ui.SetOn(kv.Value, kv.Key == tab);
            Build();
        }

        /// <summary>The rows of the open tab (not CONTROLS).</summary>
        public List<SettingOption> TabOptions() => model.Options().FindAll(o => o.tab == tab);

        /// <summary>Steps row i of the open tab by dir (test helper, also the arrow buttons).</summary>
        public void Step(int i, int dir)
        {
            var rows = TabOptions();
            if (i < 0 || i >= rows.Count) return;
            StepOption(rows[i], dir);
        }

        public void StepId(string id, int dir)
        {
            var o = model.Options().Find(x => x.id == id);
            if (o != null) StepOption(o, dir);
        }

        void StepOption(SettingOption o, int dir)
        {
            int n = o.choices.Length;
            if (n == 0) return;
            int v = o.get() + dir;
            v = o.wrap ? ((v % n) + n) % n : Mathf.Clamp(v, 0, n - 1);
            o.set(v);
            Build();
        }

        public void Apply()
        {
            StopListening();
            bool dirty = model.Dirty;
            model.Apply();
            status.text = dirty ? "Settings applied and saved." : "Nothing to apply.";
            Build();
        }

        public void Revert()
        {
            StopListening();
            bool dirty = model.Dirty;
            model.Revert();
            status.text = dirty ? "Changes dropped." : "";
            Build();
        }

        /// <summary>BACK / Esc: stops a key capture first; otherwise drops unapplied changes and leaves.</summary>
        public void Back()
        {
            if (Listening) { StopListening(); Build(); return; }
            if (Time.frameCount <= listenEndFrame + 1) return;      // the Esc that ended a key capture
            if (model.Dirty) model.Revert();
            onBack?.Invoke();
        }

        public void ResetControls()
        {
            StopListening();
            Hotkeys.ResetAll();
            status.text = "All keys set back to the defaults.";
            Build();
        }

        /// <summary>Waits for a key for one slot of an action.</summary>
        public void Listen(string id, int slot)
        {
            if (Hotkeys.Get(id) == null) return;
            listenId = id;
            listenSlot = slot;
            Hotkeys.Suspended = true;
            status.text = $"Press a key for {Hotkeys.Get(id).name} (Esc cancels, Backspace clears).";
            Build();
        }

        void StopListening()
        {
            if (listenId == null) return;
            listenId = null;
            Hotkeys.Suspended = false;
            listenEndFrame = Time.frameCount;
        }

        /// <summary>Gives a captured key to the slot being edited (also used by tests). Returns the actions that lost it.</summary>
        public List<string> Capture(KeyBind b)
        {
            if (listenId == null) return null;
            string id = listenId;
            int slot = listenSlot;
            StopListening();
            if (b.key == UnityEngine.InputSystem.Key.Escape && b.mods == KeyMods.None) { status.text = "Cancelled."; Build(); return new List<string>(); }
            if (b.key == UnityEngine.InputSystem.Key.Backspace && b.mods == KeyMods.None)
            {
                Hotkeys.Unbind(id, slot);
                status.text = $"{Hotkeys.Get(id).name}: slot {slot + 1} cleared.";
                Build();
                return new List<string>();
            }
            var lost = Hotkeys.Rebind(id, slot, b) ?? new List<string>();
            var names = lost.ConvertAll(x => Hotkeys.Get(x).name);
            status.text = $"{Hotkeys.Get(id).name} = {Hotkeys.Label(b)}" + (names.Count > 0 ? "  (taken from: " + string.Join(", ", names) + ")" : "");
            Build();
            return lost;
        }

        /// <summary>Call every frame while the screen is shown: key capture and the Apply button state.</summary>
        public void Tick()
        {
            if (listenId != null)
            {
                var k = Hotkeys.CaptureKey();
                if (k.HasValue) Capture(k.Value);
            }
            bool d = model.Dirty;
            if (d != wasDirty || (tab == "CONTROLS" && builtVersion != Hotkeys.Version && listenId == null)) Build();
        }

        // ------------------------------------------------------------------ building
        void Build()
        {
            float keep = scroll != null ? scroll.scrollOffset.y : 0f;
            body.Clear();
            scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("op-settings__scroll");
            body.Add(scroll);
            if (tab == "CONTROLS") BuildControls();
            else
            {
                foreach (var o in TabOptions()) scroll.Add(Row(o));
                if (tab == "AUDIO") scroll.Add(Ui.Para("Sound comes in Milestone 4; the volumes are kept until then."));
            }
            scroll.scrollOffset = new Vector2(0, keep);
            wasDirty = model.Dirty;
            builtVersion = Hotkeys.Version;
            Ui.SetOn(applyButton, wasDirty);
            dirtyNote.text = wasDirty ? "Unapplied changes (BACK drops them)" : "";
        }

        VisualElement Row(SettingOption o)
        {
            var row = Ui.Box("op-row op-settings__row");
            var left = Ui.Box("op-col op-grow");
            left.Add(Ui.Text(o.label, "op-settings__label"));
            if (!string.IsNullOrEmpty(o.note)) left.Add(Ui.Text(o.note, "op-dim"));
            row.Add(left);
            row.Add(Ui.TextButton("<", () => StepOption(o, -1), "op-button op-chip"));
            int v = o.get();
            row.Add(Ui.Text(v >= 0 && v < o.choices.Length ? o.choices[v] : "?", "op-settings__value"));
            row.Add(Ui.TextButton(">", () => StepOption(o, 1), "op-button op-chip"));
            return row;
        }

        void BuildControls()
        {
            var top = Ui.Box("op-row op-settings__top");
            top.Add(Ui.Para("Click a key slot, then press the new key (with Ctrl / Alt / Shift if wanted). Esc cancels, Backspace clears the slot."));
            top.Add(Ui.TextButton("RESET ALL KEYS", ResetControls, "op-button op-chip"));
            scroll.Add(top);
            string lastGroup = null, lastContext = null;
            foreach (var a in Hotkeys.Actions)
            {
                if (a.context != lastContext)
                {
                    lastContext = a.context;
                    scroll.Add(Ui.Heading(a.context == Hotkeys.Menu ? "Menus" : "Battle", "op-h2"));
                    lastGroup = null;
                }
                if (a.group != lastGroup)
                {
                    lastGroup = a.group;
                    scroll.Add(Ui.Heading(a.group, "op-h3"));
                }
                scroll.Add(KeyRow(a));
            }
        }

        VisualElement KeyRow(HotkeyAction a)
        {
            var row = Ui.Box("op-row op-settings__row op-settings__keyrow");
            var left = Ui.Box("op-col op-grow");
            left.Add(Ui.Text(a.name, "op-settings__label"));
            if (!string.IsNullOrEmpty(a.pad)) left.Add(Ui.Text("Pad: " + a.pad, "op-dim"));
            row.Add(left);
            var b = Hotkeys.Bindings(a.id);
            for (int s = 0; s < 2; s++)
            {
                int slot = s;
                bool here = listenId == a.id && listenSlot == s;
                string text = here ? "press a key..." : b[s].IsNone ? "-" : Hotkeys.Label(b[s]);
                var btn = Ui.TextButton(text, () => Listen(a.id, slot), "op-button op-settings__key");
                Ui.SetOn(btn, here);
                row.Add(btn);
            }
            var reset = Ui.TextButton("default", Hotkeys.IsDefault(a.id) ? null : (System.Action)(() => { Hotkeys.ResetToDefault(a.id); Build(); }), "op-button op-chip");
            row.Add(reset);
            return row;
        }
    }
}
