using System;
using System.Collections.Generic;
using DuckCam.Core;

namespace DuckCam.UI
{
    internal static class TabHelp
    {
        private static string K(BepInEx.Configuration.ConfigEntry<BepInEx.Configuration.KeyboardShortcut> e) => DuckCamUI.KeyName(e);

        private static readonly Dictionary<string, Func<string>> Texts = new Dictionary<string, Func<string>>
        {
            ["camera"] = () =>
                "1. Fly with WASD, F/R for down/up; hold the right mouse button to look around. The mouse wheel changes field of view.\n"
                + "2. Camera Type: Free flies anywhere, Orbit circles a character, Tripod stays put, First Person rides at someone's eyes.\n"
                + "3. Movement Radius and Collision keep the camera near your PMC or out of walls.\n"
                + "4. Bookmarks: click an empty slot to store the camera spot, click it again to jump back (Alt+1-9).",
            ["motion"] = () =>
                "1. Attach the camera to a character or object so it follows them (Attach Type, Anchor On Characters).\n"
                + "2. Turn With Target decides whether the camera turns with them or keeps its own direction.\n"
                + "3. Handheld Shake adds a natural camera wobble to the free camera.",
            ["paths"] = () =>
                "1. Keyframe path: fly to where the shot starts and press Add Keyframe (" + K(DuckCamConfig.CreateKeyframe) + "), then fly to the next spot and add another.\n"
                + "2. Play glides the camera through every keyframe; Path Duration sets how long the whole move takes.\n"
                + "3. Fix a spot by dragging its numbered tag on screen, or press Edit on that key in the list.\n"
                + "4. Camera recording: press Record, fly by hand, press End. Play repeats your exact flight.\n"
                + "5. To film either one: CAPTURE tab, start recording (" + K(DuckCamConfig.VideoRecordKey) + "), then Play.",
            ["time"] = () =>
                "1. " + K(DuckCamConfig.ToggleKey) + " freezes the raid; " + K(DuckCamConfig.UnsnapKey) + " starts DuckCam with the raid still running.\n"
                + "2. Step Frames lets the world run a few frames, then freezes again (great for mid-action shots).\n"
                + "3. Freeze Bots Only stops every bot while you keep playing. Slow Motion slows the whole raid.\n"
                + "4. Session > End leaves DuckCam and undoes every change.",
            ["dof"] = () =>
                "1. Turn on Depth of Field and choose a Focus Mode: Manual, Screen Center, your PMC, or the attached target.\n"
                + "2. Show Focus Plane while adjusting so you can see exactly what is sharp.\n"
                + "3. Render DoF Photo (frozen scene) takes many frames for real lens-style bokeh.",
            ["effects"] = () =>
                "1. Pick a Preset look, then fine-tune any slider below it.\n"
                + "2. Filter applies a colour filter on top; Reset Effects goes back to the game's look.\n"
                + "3. Save Look As Filter keeps your look under My Filters for later.",
            ["lights"] = () =>
                "1. Choose a Light Type and press Create Light: it appears at the camera.\n"
                + "2. Select a light to change colour, range, angle and shadows, or Move To Camera to place it again.\n"
                + "3. Camera Flash lights only the photo. Save Rig keeps a light setup to reuse around any character.",
            ["environment"] = () =>
                "1. Time Preset changes the time of day; Keep Time After Exit keeps it when you leave.\n"
                + "2. Weather Override sets rain, fog, clouds and wind (maps with dynamic weather only).",
            ["characters"] = () =>
                "1. Press + in Characters to add your PMC or a copy of anyone nearby. Copies keep their pose and weapon.\n"
                + "2. Pick a pose under Library, or Pose Bone By Bone (" + K(DuckCamConfig.PoseEditorKey) + ") for full control.\n"
                + "3. Look At makes the head follow the camera, another character or a point.\n"
                + "4. Spawn Bots adds real bots; Add Any Bot (photo only) adds a posable character that has no AI.\n"
                + "5. Pose > Category > DuckCam lists the poses you saved.",
            ["outfit"] = () =>
                "1. Choose a character, then change head, top and lower body.\n"
                + "2. Gear: pick any slot and choose an item from the Item Database.\n"
                + "3. Show / hide gear hides single slots; Reset Gear goes back to what they wore.",
            ["overlays"] = () =>
                "1. Frame and Stickers use PNG files from the Overlay Folder (each subfolder is a category).\n"
                + "2. Text, Border and Mask are made in DuckCam and drawn inside the photo frame.\n"
                + "3. Everything here appears in the saved photo exactly as previewed.",
            ["frame"] = () =>
                "1. Grid and Horizon Line help composition; they never appear in photos.\n"
                + "2. Frame picks an aspect ratio; Crop Photo To Frame saves exactly the area inside it.\n"
                + "3. Interface: colours, panel side, Tabs (show or hide tabs), Keybinds, language and the Beginner Guide.",
            ["presets"] = () =>
                "1. Save Scene Preset stores the whole look: effects, lens, frame, time, weather, text and lights.\n"
                + "2. Load a preset to get it back; the camera and lights load in place on the same map.\n"
                + "3. Reset puts everything back to default without leaving DuckCam.",
            ["addons"] = () =>
                "1. Open the Add-on Creator and make a pack on the left, with your name as the author.\n"
                + "2. Pick a kind of content at the top, tick your own items and press Move or Copy to add them to the pack.\n"
                + "3. Tick one item to rename it. Share the pack by zipping its folders.",
            ["capture"] = () =>
                "1. Take Screenshot (" + K(DuckCamConfig.ScreenshotKey) + ") saves a PNG with the UI hidden.\n"
                + "2. Photo Resolution goes up to 16K; Transparent Background keeps only the characters.\n"
                + "3. Record Video (" + K(DuckCamConfig.VideoRecordKey) + ") needs FFmpeg; combine it with a camera path for smooth moves.\n"
                + "4. Gallery (" + K(DuckCamConfig.GalleryKey) + ") shows your photos and videos: view, compare two, delete, or open the folder.",
            ["debug"] = () =>
                "1. If something breaks, set Log level to Verbose, repeat the problem and press Copy Report.\n"
                + "2. Send the report together with BepInEx/LogOutput.log."
        };

        public static bool Has(string icon) => icon != null && Texts.ContainsKey(icon);

        public static string Get(string icon)
        {
            if (!Has(icon))
            {
                return null;
            }
            try
            {
                return Texts[icon]() + "\nChange any key in FRAME > Interface > Keybinds.";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
