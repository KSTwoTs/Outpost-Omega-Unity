using UnityEngine;
using UnityEngine.UIElements;
using Outpost.Content;

namespace Outpost.UI
{
    /// <summary>Small helpers every HUD panel uses, and the one place numbers and times are formatted.</summary>
    public static class Ui
    {
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
