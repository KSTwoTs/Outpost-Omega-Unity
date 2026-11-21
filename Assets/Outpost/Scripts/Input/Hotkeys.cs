// Hotkeys.cs · roadmap step M2 (filled Nov 21, Milestone 2 group 2).
// Every key in one table (action id, default binding, description), rebinding, and the load/save hooks
// the settings/save layer (Game/Settings.cs, group 3) plugs into.

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Outpost.View
{
    /// <summary>Modifier keys a binding needs.</summary>
    [Flags]
    public enum KeyMods { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }

    /// <summary>One key plus the modifiers it needs. Key.None = unbound.</summary>
    [Serializable]
    public struct KeyBind : IEquatable<KeyBind>
    {
        public Key key;
        public KeyMods mods;

        public KeyBind(Key k, KeyMods m = KeyMods.None)
        {
            key = k;
            mods = m;
        }

        public static readonly KeyBind None = new KeyBind(Key.None);
        public bool IsNone => key == Key.None;
        public bool Equals(KeyBind o) => key == o.key && mods == o.mods;
        public override bool Equals(object o) => o is KeyBind && Equals((KeyBind)o);
        public override int GetHashCode() => ((int)key * 8) ^ (int)mods;
        public override string ToString() => Hotkeys.Label(this);

        /// <summary>Storage form: modifiers then the Input System key name, e.g. "Ctrl+Digit1", "Alt+Q", "None".</summary>
        public string Save()
        {
            if (IsNone) return "None";
            string s = "";
            if ((mods & KeyMods.Ctrl) != 0) s += "Ctrl+";
            if ((mods & KeyMods.Alt) != 0) s += "Alt+";
            if ((mods & KeyMods.Shift) != 0) s += "Shift+";
            return s + key;
        }

        public static bool TryParse(string text, out KeyBind bind)
        {
            bind = None;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.Split('+');
            var m = KeyMods.None;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string p = parts[i].Trim().ToLowerInvariant();
                if (p == "ctrl") m |= KeyMods.Ctrl;
                else if (p == "alt") m |= KeyMods.Alt;
                else if (p == "shift") m |= KeyMods.Shift;
                else return false;
            }
            Key k;
            if (!Enum.TryParse(parts[parts.Length - 1].Trim(), true, out k)) return false;
            bind = new KeyBind(k, k == Key.None ? KeyMods.None : m);
            return true;
        }
    }

    /// <summary>One row of the table.</summary>
    public class HotkeyAction
    {
        public string id, context, group, name, desc;
        public KeyBind[] defaults;
        public string pad;          // gamepad button name shown in help (not rebindable), or null
    }

    /// <summary>
    /// Every key in the game in one table. Code asks for an action by id (Pressed("ability"), Held("cam_left")) and never
    /// names a key itself, so rebinding is one place. Each action has up to two bindings (Python's B or Home, + or Num+).
    /// Contexts: battle (orders, selection, camera, HUD) and menu (the menu screens). Two actions in the same context
    /// never share a binding: Rebind takes the key away from the other action and says which one lost it.
    ///
    /// Saving: Hotkeys only knows a small JSON object of the bindings that differ from the defaults (ToJson / FromJson).
    /// The settings layer sets LoadHook / SaveHook (group 3: settings.json); until then rebinds last for the session.
    /// Modifiers: Ctrl and Alt must match exactly; Shift is ignored unless the binding needs it (Shift+click queues
    /// orders), except when another binding of the same key needs Shift.
    /// </summary>
    public static class Hotkeys
    {
        public const string Battle = "battle", Menu = "menu";

        public static readonly List<HotkeyAction> Actions = new List<HotkeyAction>();
        static readonly Dictionary<string, HotkeyAction> byId = new Dictionary<string, HotkeyAction>();
        static readonly Dictionary<string, KeyBind[]> overrides = new Dictionary<string, KeyBind[]>();
        static readonly HashSet<Key> shiftKeys = new HashSet<Key>();

        /// <summary>Bumped on every change, so the HUD can refresh its key labels.</summary>
        public static int Version { get; private set; }
        /// <summary>True while a settings screen waits for a new key: every action reads as not pressed.</summary>
        public static bool Suspended;
        /// <summary>Group 3: returns the saved bindings JSON (or null). Called by Load.</summary>
        public static Func<string> LoadHook;
        /// <summary>Group 3: stores the bindings JSON. Called after every change.</summary>
        public static Action<string> SaveHook;
        /// <summary>Tests: replace the keyboard. Given a key, is it down now / was it pressed this frame?</summary>
        public static Func<Key, bool> TestHeld, TestPressed;

        static Hotkeys()
        {
            BuildTable();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Suspended = false;
            TestHeld = TestPressed = null;
        }

        static void Add(string ctx, string group, string id, string name, string desc, params KeyBind[] keys) =>
            Add(ctx, group, id, name, desc, null, keys);

        static void Add(string ctx, string group, string id, string name, string desc, string pad, params KeyBind[] keys)
        {
            var a = new HotkeyAction { id = id, context = ctx, group = group, name = name, desc = desc, defaults = keys, pad = pad };
            Actions.Add(a);
            byId[id] = a;
        }

        static KeyBind K(Key k) => new KeyBind(k);
        static KeyBind Ctrl(Key k) => new KeyBind(k, KeyMods.Ctrl);
        static KeyBind Alt(Key k) => new KeyBind(k, KeyMods.Alt);

        /// <summary>The 5 x 3 command card keys, left to right, top to bottom (Alt + the left-hand grid).</summary>
        public static readonly Key[] CardGrid =
        {
            Key.Q, Key.W, Key.E, Key.R, Key.T,
            Key.A, Key.S, Key.D, Key.F, Key.G,
            Key.Z, Key.X, Key.C, Key.V, Key.B,
        };
        public const int Groups = 9;

        static void BuildTable()
        {
            const string B = Battle;
            // orders (Python scenes/rts.py on_key)
            Add(B, "Orders", "attack_move", "Attack-move", "Then left click a target or the ground", K(Key.A));
            Add(B, "Orders", "stop", "Stop", "Drop the current order", K(Key.S));
            Add(B, "Orders", "hold", "Hold position", "Stay put and only shoot what comes into range", K(Key.H));
            Add(B, "Orders", "ability", "Unit ability", "Aimed ones then take a left click", K(Key.F));
            Add(B, "Orders", "recycle", "Recycle / sell", "Recycle the selected building or sell the selected turret", K(Key.Delete));
            // selection
            Add(B, "Selection", "select_army", "Whole army", "Select every combat unit and the Commander", K(Key.Q));
            Add(B, "Selection", "select_screen", "Army on screen", "Select your units in view", K(Key.E));
            Add(B, "Selection", "select_commander", "Commander", "Select the Commander (again: jump to it)", K(Key.G));
            for (int i = 1; i <= Groups; i++)
            {
                var digit = (Key)((int)Key.Digit1 + i - 1);
                Add(B, "Selection", "group_" + i, "Group " + i, "Recall group " + i + " (tap twice to jump to it)", K(digit));
                Add(B, "Selection", "set_group_" + i, "Set group " + i, "Make the selection group " + i, Ctrl(digit));
            }
            // powers
            string[] pk = { "Z", "X", "C", "V" };
            for (int i = 0; i < 4; i++)
                Add(B, "Powers", "power_" + (i + 1), "Leader power " + (i + 1), "Start aiming power " + (i + 1) + " of the power bar",
                    K((Key)Enum.Parse(typeof(Key), pk[i])));
            // command card
            for (int i = 0; i < CardGrid.Length; i++)
                Add(B, "Command card", "card_" + (i + 1), "Card button " + (i + 1),
                    "Button " + (i % 5 + 1) + " of row " + (i / 5 + 1) + " on the command card", Alt(CardGrid[i]));
            // camera
            Add(B, "Camera", "jump_base", "Jump to base", "Main base; press again for the next base", K(Key.B), K(Key.Home));
            Add(B, "Camera", "jump_alert", "Jump to alert", "Last alert; press again for the one before", K(Key.Space));
            Add(B, "Camera", "cam_up", "Camera up", "Move the camera", "left stick", K(Key.UpArrow));
            Add(B, "Camera", "cam_down", "Camera down", "Move the camera", "left stick", K(Key.DownArrow));
            Add(B, "Camera", "cam_left", "Camera left", "Move the camera", "left stick", K(Key.LeftArrow));
            Add(B, "Camera", "cam_right", "Camera right", "Move the camera", "left stick", K(Key.RightArrow));
            Add(B, "Camera", "zoom_in", "Zoom in", "Also the mouse wheel", "right stick up", K(Key.Equals), K(Key.NumpadPlus));
            Add(B, "Camera", "zoom_out", "Zoom out", "Also the mouse wheel", "right stick down", K(Key.Minus), K(Key.NumpadMinus));
            Add(B, "Camera", "cam_rotate_left", "Rotate left", "Hold to turn the camera (or Alt + middle drag)", "right stick left", K(Key.PageUp));
            Add(B, "Camera", "cam_rotate_right", "Rotate right", "Hold to turn the camera", "right stick right", K(Key.PageDown));
            Add(B, "Camera", "cam_reset", "Reset rotation", "Camera faces north again", "right stick press", K(Key.Backspace));
            // game
            Add(B, "Game", "cancel", "Cancel / pause", "Cancel aiming, else pause", "B", K(Key.Escape));
            Add(B, "Game", "pause", "Pause", "Pause or resume", "Start", K(Key.P));
            Add(B, "Game", "game_speed", "Game speed", "x1, x1.5, x2", K(Key.Tab));
            Add(B, "Game", "help", "Controls", "Show or hide the controls panel", "Select", K(Key.F1));
            Add(B, "Game", "reveal_map", "Reveal map (debug)", "Editor and development builds only (Python F9)", K(Key.F9));
            // menus
            Add(Menu, "Menus", "menu_back", "Back", "Back to the previous screen", "B", K(Key.Escape));
            Add(Menu, "Menus", "menu_confirm", "Confirm", "Deploy / select", "A", K(Key.Enter), K(Key.NumpadEnter));
            Add(Menu, "Menus", "menu_up", "Previous", "Previous Commander in the roster", "D-pad up", K(Key.UpArrow), K(Key.W));
            Add(Menu, "Menus", "menu_down", "Next", "Next Commander in the roster", "D-pad down", K(Key.DownArrow), K(Key.S));
            Changed();
        }

        // ------------------------------------------------------------------ reading the table
        public static HotkeyAction Get(string id)
        {
            HotkeyAction a;
            return id != null && byId.TryGetValue(id, out a) ? a : null;
        }

        /// <summary>The action's current bindings (two slots; Key.None = empty).</summary>
        public static KeyBind[] Bindings(string id)
        {
            KeyBind[] b;
            if (overrides.TryGetValue(id, out b)) return b;
            var a = Get(id);
            if (a == null) return new[] { KeyBind.None, KeyBind.None };
            return new[] { a.defaults.Length > 0 ? a.defaults[0] : KeyBind.None, a.defaults.Length > 1 ? a.defaults[1] : KeyBind.None };
        }

        public static bool IsDefault(string id) => !overrides.ContainsKey(id);

        /// <summary>The first binding as shown on buttons ("A", "Ctrl+1", "Alt+Q", "Del"), or "" when unbound.</summary>
        public static string Label(string id)
        {
            foreach (var b in Bindings(id)) if (!b.IsNone) return Label(b);
            return "";
        }

        /// <summary>Every binding of an action, joined ("B / Home").</summary>
        public static string Labels(string id) => string.Join(" / ", Bindings(id).Where(b => !b.IsNone).Select(Label));

        public static string Label(KeyBind b)
        {
            if (b.IsNone) return "";
            string s = "";
            if ((b.mods & KeyMods.Ctrl) != 0) s += "Ctrl+";
            if ((b.mods & KeyMods.Alt) != 0) s += "Alt+";
            if ((b.mods & KeyMods.Shift) != 0) s += "Shift+";
            return s + KeyName(b.key);
        }

        public static string KeyName(Key k)
        {
            if (k >= Key.Digit1 && k <= Key.Digit9) return ((int)(k - Key.Digit1) + 1).ToString();
            if (k == Key.Digit0) return "0";
            if (k >= Key.Numpad0 && k <= Key.Numpad9) return "Num" + (int)(k - Key.Numpad0);
            switch (k)
            {
                case Key.UpArrow: return "Up";
                case Key.DownArrow: return "Down";
                case Key.LeftArrow: return "Left";
                case Key.RightArrow: return "Right";
                case Key.Delete: return "Del";
                case Key.Escape: return "Esc";
                case Key.PageUp: return "PgUp";
                case Key.PageDown: return "PgDn";
                case Key.Equals: return "+";
                case Key.Minus: return "-";
                case Key.NumpadPlus: return "Num+";
                case Key.NumpadMinus: return "Num-";
                case Key.NumpadEnter: return "Num Enter";
                case Key.Backspace: return "Backspace";
                case Key.LeftBracket: return "[";
                case Key.RightBracket: return "]";
                case Key.Semicolon: return ";";
                case Key.Quote: return "'";
                case Key.Comma: return ",";
                case Key.Period: return ".";
                case Key.Slash: return "/";
                case Key.Backslash: return "\\";
                case Key.Backquote: return "Backquote";
            }
            return k.ToString();
        }

        // ------------------------------------------------------------------ reading the keyboard
        // the Input System's dummy IME key (obsolete name, so it is looked up once by name)
        static readonly Key ImeKey = (Key)Enum.Parse(typeof(Key), "IMESelected");

        static bool KeyHeld(Key k)
        {
            if (TestHeld != null) return TestHeld(k);
            var kb = Keyboard.current;
            return kb != null && k != Key.None && k != ImeKey && kb[k].isPressed;
        }

        static bool KeyPressed(Key k)
        {
            if (TestPressed != null) return TestPressed(k);
            var kb = Keyboard.current;
            return kb != null && k != Key.None && k != ImeKey && kb[k].wasPressedThisFrame;
        }

        static bool ModsMatch(KeyBind b)
        {
            bool ctrl = KeyHeld(Key.LeftCtrl) || KeyHeld(Key.RightCtrl);
            bool alt = KeyHeld(Key.LeftAlt) || KeyHeld(Key.RightAlt);
            bool shift = KeyHeld(Key.LeftShift) || KeyHeld(Key.RightShift);
            if (ctrl != ((b.mods & KeyMods.Ctrl) != 0)) return false;
            if (alt != ((b.mods & KeyMods.Alt) != 0)) return false;
            bool needShift = (b.mods & KeyMods.Shift) != 0;
            if (needShift) return shift;
            return !shift || !shiftKeys.Contains(b.key);
        }

        /// <summary>Was the action's key pressed this frame (with the right modifiers)?</summary>
        public static bool Pressed(string id)
        {
            if (Suspended) return false;
            foreach (var b in Bindings(id))
                if (!b.IsNone && KeyPressed(b.key) && ModsMatch(b)) return true;
            return false;
        }

        /// <summary>Is the action's key held down now (camera movement, rotation)?</summary>
        public static bool Held(string id)
        {
            if (Suspended) return false;
            foreach (var b in Bindings(id))
                if (!b.IsNone && KeyHeld(b.key) && ModsMatch(b)) return true;
            return false;
        }

        /// <summary>Settings screen: the key pressed this frame with the modifiers held, or null. Modifier keys alone don't count.</summary>
        public static KeyBind? CaptureKey()
        {
            var kb = Keyboard.current;
            if (kb == null && TestPressed == null) return null;
            foreach (Key k in Enum.GetValues(typeof(Key)))
            {
                if (k == Key.None || k == ImeKey || IsModifier(k)) continue;
                if (!KeyPressed(k)) continue;
                var m = KeyMods.None;
                if (KeyHeld(Key.LeftCtrl) || KeyHeld(Key.RightCtrl)) m |= KeyMods.Ctrl;
                if (KeyHeld(Key.LeftAlt) || KeyHeld(Key.RightAlt)) m |= KeyMods.Alt;
                if (KeyHeld(Key.LeftShift) || KeyHeld(Key.RightShift)) m |= KeyMods.Shift;
                return new KeyBind(k, m);
            }
            return null;
        }

        public static bool IsModifier(Key k) =>
            k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt || k == Key.LeftShift || k == Key.RightShift ||
            k == Key.LeftMeta || k == Key.RightMeta || k == Key.ContextMenu;

        // ------------------------------------------------------------------ rebinding
        /// <summary>Other actions in the same context already using this binding.</summary>
        public static List<string> UsersOf(KeyBind b, string context, string except = null)
        {
            var list = new List<string>();
            if (b.IsNone) return list;
            foreach (var a in Actions)
                if (a.id != except && a.context == context && Bindings(a.id).Contains(b)) list.Add(a.id);
            return list;
        }

        /// <summary>Sets binding slot 0 or 1 of an action. Any other action of the same context that had this binding loses it.
        /// Returns the ids that lost it (empty if none), or null when the action or slot is unknown. Saves through SaveHook.</summary>
        public static List<string> Rebind(string id, int slot, KeyBind bind)
        {
            var a = Get(id);
            if (a == null || slot < 0 || slot > 1) return null;
            var lost = UsersOf(bind, a.context, id);
            foreach (var other in lost)
            {
                var ob = (KeyBind[])Bindings(other).Clone();
                for (int i = 0; i < ob.Length; i++) if (ob[i].Equals(bind)) ob[i] = KeyBind.None;
                Store(other, ob);
            }
            var cur = (KeyBind[])Bindings(id).Clone();
            cur[slot] = bind;
            if (slot == 0 && cur[1].Equals(bind)) cur[1] = KeyBind.None;
            if (slot == 1 && cur[0].Equals(bind)) cur[0] = KeyBind.None;
            Store(id, cur);
            Changed();
            Save();
            return lost;
        }

        /// <summary>Clears binding slot 0 or 1.</summary>
        public static void Unbind(string id, int slot) => Rebind(id, slot, KeyBind.None);

        public static void ResetToDefault(string id)
        {
            overrides.Remove(id);
            Changed();
            Save();
        }

        public static void ResetAll()
        {
            overrides.Clear();
            Changed();
            Save();
        }

        static void Store(string id, KeyBind[] b)
        {
            var a = Get(id);
            var def = new[] { a.defaults.Length > 0 ? a.defaults[0] : KeyBind.None, a.defaults.Length > 1 ? a.defaults[1] : KeyBind.None };
            if (b[0].Equals(def[0]) && b[1].Equals(def[1])) overrides.Remove(id);
            else overrides[id] = b;
        }

        static void Changed()
        {
            shiftKeys.Clear();
            foreach (var a in Actions)
                foreach (var b in Bindings(a.id))
                    if (!b.IsNone && (b.mods & KeyMods.Shift) != 0) shiftKeys.Add(b.key);
            Version++;
        }

        /// <summary>Pairs of actions in one context that share a binding (should always be empty).</summary>
        public static List<string> Conflicts()
        {
            var list = new List<string>();
            foreach (var a in Actions)
                foreach (var b in Bindings(a.id))
                {
                    if (b.IsNone) continue;
                    foreach (var other in UsersOf(b, a.context, a.id))
                        if (string.CompareOrdinal(a.id, other) < 0) list.Add(a.id + " = " + other + " (" + Label(b) + ")");
                }
            return list;
        }

        // ------------------------------------------------------------------ saving
        /// <summary>The bindings that differ from the defaults: {"id": ["Ctrl+Digit1", "None"], ...}.</summary>
        public static string ToJson()
        {
            var o = new JObject();
            foreach (var kv in overrides.OrderBy(k => k.Key))
                o[kv.Key] = new JArray(kv.Value.Select(b => b.Save()));
            return o.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>Replaces every rebind with the ones in this JSON (null or empty = all defaults). Unknown ids and keys
        /// are skipped. Returns false if the text isn't a JSON object.</summary>
        public static bool FromJson(string json)
        {
            overrides.Clear();
            bool ok = true;
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var o = JObject.Parse(json);
                    foreach (var p in o.Properties())
                    {
                        var a = Get(p.Name);
                        var arr = p.Value as JArray;
                        if (a == null || arr == null) continue;
                        var b = Bindings(a.id);
                        b = new[] { b[0], b[1] };
                        for (int i = 0; i < 2 && i < arr.Count; i++)
                        {
                            KeyBind kb;
                            if (KeyBind.TryParse((string)arr[i], out kb)) b[i] = kb;
                        }
                        Store(a.id, b);
                    }
                }
                catch (Exception)
                {
                    overrides.Clear();
                    ok = false;
                }
            }
            Changed();
            return ok;
        }

        /// <summary>Reads the saved bindings through LoadHook (no hook = defaults stay).</summary>
        public static void Load()
        {
            if (LoadHook == null) return;
            FromJson(LoadHook());
        }

        /// <summary>Hands the bindings to SaveHook (no hook = they last for this session only).</summary>
        public static void Save() => SaveHook?.Invoke(ToJson());

        // ------------------------------------------------------------------ gamepad (basic; full co-op pad is M4)
        /// <summary>Left stick (camera pan), dead zone 0.2. Zero without a gamepad.</summary>
        public static Vector2 PadMove()
        {
            var g = Gamepad.current;
            if (g == null || Suspended) return Vector2.zero;
            var v = g.leftStick.ReadValue();
            return v.magnitude < 0.2f ? Vector2.zero : v;
        }

        /// <summary>Right stick: x rotates, y zooms. Dead zone 0.2.</summary>
        public static Vector2 PadLook()
        {
            var g = Gamepad.current;
            if (g == null || Suspended) return Vector2.zero;
            var v = g.rightStick.ReadValue();
            return v.magnitude < 0.2f ? Vector2.zero : v;
        }

        /// <summary>The gamepad button of an action ("pad" column) pressed this frame.</summary>
        public static bool PadPressed(string id)
        {
            var g = Gamepad.current;
            var a = Get(id);
            if (g == null || a == null || a.pad == null || Suspended) return false;
            switch (a.pad)
            {
                case "A": return g.buttonSouth.wasPressedThisFrame;
                case "B": return g.buttonEast.wasPressedThisFrame;
                case "Start": return g.startButton.wasPressedThisFrame;
                case "Select": return g.selectButton.wasPressedThisFrame;
                case "D-pad up": return g.dpad.up.wasPressedThisFrame;
                case "D-pad down": return g.dpad.down.wasPressedThisFrame;
                case "right stick press": return g.rightStickButton.wasPressedThisFrame;
            }
            return false;
        }

        /// <summary>Keyboard or gamepad (for menus and pause).</summary>
        public static bool PressedAny(string id) => Pressed(id) || PadPressed(id);
    }
}
