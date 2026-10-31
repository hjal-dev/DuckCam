using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DuckCam.UI
{
    internal readonly struct Chord
    {
        public readonly KeyCode Key;
        public readonly bool Ctrl;
        public readonly bool Shift;
        public readonly bool Alt;

        public Chord(KeyCode key, bool ctrl = false, bool shift = false, bool alt = false)
        {
            Key = key;
            Ctrl = ctrl;
            Shift = shift;
            Alt = alt;
        }

        public static bool CtrlHeld => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        public static bool ShiftHeld => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        public static bool AltHeld => Input.GetKey(KeyCode.LeftAlt) || (Input.GetKey(KeyCode.RightAlt) && !CtrlHeld);

        public bool Down => Key != KeyCode.None && Input.GetKeyDown(Key) && Ctrl == CtrlHeld && Shift == ShiftHeld && Alt == AltHeld;

        public override string ToString()
        {
            var sb = new StringBuilder();
            if (Ctrl) sb.Append("Ctrl+");
            if (Shift) sb.Append("Shift+");
            if (Alt) sb.Append("Alt+");
            sb.Append(Name(Key));
            return sb.ToString();
        }

        public static string Name(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.LeftBracket: return "[";
                case KeyCode.RightBracket: return "]";
                case KeyCode.BackQuote: return "`";
                case KeyCode.Comma: return ",";
                case KeyCode.Period: return ".";
                case KeyCode.Delete: return "Del";
                case KeyCode.Return: return "Enter";
                case KeyCode.Escape: return "Esc";
                case KeyCode.KeypadPeriod: return "Numpad .";
                default: return k.ToString();
            }
        }
    }

    internal enum PoseAction
    {
        Move,
        Rotate,
        Scale,
        ClearLocation,
        ClearRotation,
        ClearScale,
        SelectAll,
        DeselectAll,
        InvertSelection,
        SelectParent,
        SelectParentExtend,
        SelectChild,
        SelectChildExtend,
        SelectMirror,
        BoxSelect,
        Hide,
        HideUnselected,
        Reveal,
        CopyPose,
        PastePose,
        PastePoseFlipped,
        Undo,
        Redo,
        RedoAlt,
        ContextMenu,
        ViewMenu,
        ViewAll,
        ViewSelected,
        OrientationMenu,
        ToggleGizmo,
        Help
    }

    internal static class PoseKeymap
    {
        public static readonly Dictionary<PoseAction, Chord> Keys = new Dictionary<PoseAction, Chord>
        {
            { PoseAction.Move, new Chord(KeyCode.G) },
            { PoseAction.Rotate, new Chord(KeyCode.R) },
            { PoseAction.Scale, new Chord(KeyCode.S) },
            { PoseAction.ClearLocation, new Chord(KeyCode.G, alt: true) },
            { PoseAction.ClearRotation, new Chord(KeyCode.R, alt: true) },
            { PoseAction.ClearScale, new Chord(KeyCode.S, alt: true) },
            { PoseAction.SelectAll, new Chord(KeyCode.A) },
            { PoseAction.DeselectAll, new Chord(KeyCode.A, alt: true) },
            { PoseAction.InvertSelection, new Chord(KeyCode.I, ctrl: true) },
            { PoseAction.SelectParent, new Chord(KeyCode.LeftBracket) },
            { PoseAction.SelectParentExtend, new Chord(KeyCode.LeftBracket, shift: true) },
            { PoseAction.SelectChild, new Chord(KeyCode.RightBracket) },
            { PoseAction.SelectChildExtend, new Chord(KeyCode.RightBracket, shift: true) },
            { PoseAction.SelectMirror, new Chord(KeyCode.M, ctrl: true, shift: true) },
            { PoseAction.BoxSelect, new Chord(KeyCode.B) },
            { PoseAction.Hide, new Chord(KeyCode.H) },
            { PoseAction.HideUnselected, new Chord(KeyCode.H, shift: true) },
            { PoseAction.Reveal, new Chord(KeyCode.H, alt: true) },
            { PoseAction.CopyPose, new Chord(KeyCode.C, ctrl: true) },
            { PoseAction.PastePose, new Chord(KeyCode.V, ctrl: true) },
            { PoseAction.PastePoseFlipped, new Chord(KeyCode.V, ctrl: true, shift: true) },
            { PoseAction.Undo, new Chord(KeyCode.Z, ctrl: true) },
            { PoseAction.Redo, new Chord(KeyCode.Z, ctrl: true, shift: true) },
            { PoseAction.RedoAlt, new Chord(KeyCode.Y, ctrl: true) },
            { PoseAction.ContextMenu, new Chord(KeyCode.W) },
            { PoseAction.ViewMenu, new Chord(KeyCode.BackQuote) },
            { PoseAction.ViewAll, new Chord(KeyCode.Home) },
            { PoseAction.ViewSelected, new Chord(KeyCode.KeypadPeriod) },
            { PoseAction.OrientationMenu, new Chord(KeyCode.Comma) },
            { PoseAction.ToggleGizmo, new Chord(KeyCode.BackQuote, ctrl: true) },
            { PoseAction.Help, new Chord(KeyCode.F1) }
        };

        public static bool Down(PoseAction a) => Keys.TryGetValue(a, out Chord c) && c.Down;

        public static string Of(PoseAction a) => Keys.TryGetValue(a, out Chord c) ? c.ToString() : "";
    }
}
