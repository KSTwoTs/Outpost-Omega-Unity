using UnityEngine;
using UnityEngine.UIElements;
using Outpost.Content;

namespace Outpost.UI
{
    /// <summary>Small helpers every HUD panel uses, and the one place numbers and times are formatted.</summary>
    public static class Ui
    {
        // ---- UI scale (Settings > Video > UI scale): every panel made by Document or HudRoot is tracked
        static readonly System.Collections.Generic.List<PanelSettings> panels = new System.Collections.Generic.List<PanelSettings>();
        /// <summary>The UI scale every menu and HUD panel uses (1 = 1920x1080 reference scaled to the screen height).</summary>
        public static float Scale { get; private set; } = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { panels.Clear(); Scale = 1f; }

        /// <summary>Keeps a panel's scale in step with Scale.</summary>
        public static void Track(PanelSettings p)
        {
            if (p == null) return;
            panels.RemoveAll(x => x == null);
            if (!panels.Contains(p)) panels.Add(p);
            p.scale = Scale;
        }

        public static void SetScale(float s)
        {
            Scale = Mathf.Clamp(s, 0.5f, 2f);
            panels.RemoveAll(x => x == null);
            foreach (var p in panels) p.scale = Scale;
        }

        public static int TrackedPanels { get { panels.RemoveAll(x => x == null); return panels.Count; } }

        /// <summary>A plain container. Containers ignore the mouse unless pick is true, so clicks reach the battlefield.</summary>
        public static VisualElement Box(string classes, bool pick = false)
        {
            var e = new VisualElement { pickingMode = pick ? PickingMode.Position : PickingMode.Ignore };
            AddClasses(e, classes);
            return e;
        }

        public static Label Text(string text, string classes = null)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            AddClasses(l, classes);
            return l;
        }

        /// <summary>Something clickable (the theme's own Button is avoided so the look is all ours).</summary>
        public static VisualElement Button(string classes, System.Action onClick)
        {
            var e = new VisualElement { pickingMode = PickingMode.Position };
            AddClasses(e, classes);
            e.AddManipulator(new Clickable(onClick));
            return e;
        }

        /// <summary>
        /// A UI Toolkit document on its own child object, scaled like the HUD (1920 x 1080, by height) and styled by
        /// Resources/UI/Outpost.uss. Returns the root element (class op-root). Used by the menus (GameFlow).
        /// </summary>
        public static VisualElement Document(Transform parent, string name, int sortingOrder = 0)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = name;
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/OutpostTheme");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 1f;
            settings.sortingOrder = sortingOrder;
            doc.panelSettings = settings;
            Track(settings);
            go.SetActive(true);
            var root = doc.rootVisualElement;
            var sheet = Resources.Load<StyleSheet>("UI/Outpost");
            if (sheet != null) root.styleSheets.Add(sheet);
            else Debug.LogError("[Outpost] Resources/UI/Outpost.uss is missing: the menus have no styles.");
            root.AddToClassList("op-root");
            return root;
        }

        /// <summary>A button with a label (and an optional small note). With click == null it is dimmed and does nothing.</summary>
        public static VisualElement TextButton(string label, System.Action click, string classes = "op-button", string note = null)
        {
            var b = Button(classes + (click == null ? " op-button--off" : ""), click ?? (() => { }));
            b.Add(Text(label, "op-button__label"));
            if (note != null) b.Add(Text(note, "op-button__note"));
            return b;
        }

        /// <summary>Marks a toggle button as picked (class op-on).</summary>
        public static void SetOn(VisualElement e, bool on) => e.EnableInClassList("op-on", on);

        /// <summary>A heading, paragraph or bullet list for the menu screens.</summary>
        public static Label Heading(string text, string classes = "op-h2") => Text(text, classes);

        public static Label Para(string text, string classes = "op-p") => Text(text, classes);

        public static VisualElement Bullets(System.Collections.Generic.IEnumerable<string> lines, string mark = "-", string markClass = null, string textClass = null)
        {
            var box = Box("op-col");
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                var row = Box("op-bullet");
                row.Add(Text(mark, "op-bullet__mark " + markClass));
                row.Add(Text(line, "op-bullet__text " + textClass));
                box.Add(row);
            }
            return box;
        }

        public static VisualElement Bar(out VisualElement fill, string barClasses = null, string fillClasses = null)
        {
            var bar = Box("op-bar " + barClasses);
            fill = Box("op-bar__fill " + fillClasses);
            bar.Add(fill);
            return bar;
        }

        public static void SetFill(VisualElement fill, float ratio) =>
            fill.style.width = Length.Percent(Mathf.Clamp01(ratio) * 100f);

        public static void Show(VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

        static void AddClasses(VisualElement e, string classes)
        {
            if (string.IsNullOrEmpty(classes)) return;
            foreach (var c in classes.Split(' '))
                if (c.Length > 0) e.AddToClassList(c);
        }

        /// <summary>
        /// Shows a model's portrait as the element's background, or its initials until the picture is
        /// ready. Returns true once the picture is in place (callers stop asking).
        /// </summary>
        public static bool Portrait(VisualElement e, string model, string name)
        {
            var tex = PortraitLibrary.Get(model);
            if (tex != null)
            {
                e.style.backgroundImage = new StyleBackground(tex);
                var old = e.Q<Label>(className: "op-portrait__initials");
                old?.RemoveFromHierarchy();
                return true;
            }
            e.style.backgroundImage = StyleKeyword.None;
            if (e.Q<Label>(className: "op-portrait__initials") == null && !string.IsNullOrEmpty(name))
                e.Add(Text(Initials(name), "op-portrait__initials"));
            return PortraitLibrary.Missing(model);
        }

        static string Initials(string name)
        {
            var parts = name.Split(' ');
            return parts.Length > 1 ? $"{parts[0][0]}{parts[1][0]}".ToUpper() : name.Substring(0, Mathf.Min(2, name.Length)).ToUpper();
        }

        // ---- formatting
        public static string Clock(float seconds) => $"{(int)(seconds / 60)}:{(int)(seconds % 60):00}";
        public static string Secs(float seconds) => $"{Mathf.CeilToInt(seconds)} s";
        public static string Pct(float ratio) => $"{(int)(100 * Mathf.Clamp01(ratio))}%";
    }
}
