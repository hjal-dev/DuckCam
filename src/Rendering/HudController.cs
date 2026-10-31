using EFT.UI;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.Rendering
{
    internal static class HudController
    {
        public static string Status { get; private set; } = "not changed";

        public static void Hide(RestoreStack stack)
        {
            CommonUI ui = MonoBehaviourSingleton<CommonUI>.Instance;
            GameObject hud = ui != null && ui.EftBattleUIScreen != null ? ui.EftBattleUIScreen.gameObject : null;
            if (hud == null)
            {
                Status = "UNAVAILABLE (EftBattleUIScreen not found)";
                PhotoLog.Warn("Battle HUD not found.");
                return;
            }

            bool wasActive = hud.activeSelf;
            stack.Push("battle HUD", () =>
            {
                if (hud != null)
                {
                    hud.SetActive(wasActive);
                }
                Status = "restored";
            });
            hud.SetActive(false);
            Status = "hidden";
        }
    }
}
