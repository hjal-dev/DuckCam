using System;
using DuckCam.Pause;
using UnityEngine;

namespace DuckCam.UI
{
    internal static class InputContext
    {
        private static int _ownFieldId;

        public static bool MenuActive { get; private set; }

        public static bool GameKeysBlocked => FreezeFlags.InputBlocked || FreezeFlags.PlayerCommandsBlocked;

        public static bool Typing => _ownFieldId != 0 && GUIUtility.keyboardControl == _ownFieldId;

        public static void RecordFieldFocus()
        {
            int id = GUIUtility.keyboardControl;
            string name = id != 0 ? GUI.GetNameOfFocusedControl() : null;
            _ownFieldId = name != null && name.StartsWith(HudKit.FieldPrefix, StringComparison.Ordinal) ? id : 0;
        }

        public static void LeaveField()
        {
            if (Typing)
            {
                GUIUtility.keyboardControl = 0;
            }
            _ownFieldId = 0;
        }

        public static void Update(bool sessionActive, bool uiVisible)
        {
            if (!uiVisible)
            {
                LeaveField();
            }
            MenuActive = sessionActive && uiVisible && !Typing;
        }

        public static void Reset()
        {
            LeaveField();
            MenuActive = false;
        }
    }
}
