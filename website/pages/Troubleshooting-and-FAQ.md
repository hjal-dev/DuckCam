# Troubleshooting and FAQ

## Reporting a problem

1. **DEBUG > Log level: Verbose**.
2. Reproduce the problem.
3. **DEBUG > Copy Report**.
4. Open an issue with the report and `Your-SPT-Folder/BepInEx/LogOutput.log` attached. A screenshot or short clip helps a lot.

## Common problems

**Nothing happens when I press F6.**
- Check `LogOutput.log` for `DuckCam 1.0.0 loaded`. If it's missing, the DLL isn't in `BepInEx/plugins/DuckCam/`.
- Another mod may use F6. Rebind it in **F12 > DuckCam** or **FRAME > Interface > Keybinds**.

**A key does nothing, or triggers another mod.**
- F5 to F9, F11, Insert, Home, End and Delete are popular. Rebind the DuckCam key; the Keybinds window shows clashes with other mods' configs.

**F11 doesn't hide the menu.**
- Another mod probably uses F11. Use **Tab**, or rebind "Toggle DuckCam UI".

**Text from another mod (a raid timer, a minimap...) shows up in my photos.**
- DuckCam hides its own menu and the game's HUD, but can't hide what other mods draw. Turn that mod's overlay off while taking photos.

**Weather options say "Not on this map".**
- Factory, Labs and the Hideout have no dynamic weather in Tarkov. Time of day still works where the map has a clock.

**A bot reacts to me right after I release Freeze Bots Only.**
- Its AI isn't reset and it remembers nothing from the freeze, but if it can see you when released it reacts like any bot. Move out of sight first.

**Only a few Body Animations show up, or some look odd.**
- They were made for the in-raid player, not the posing mannequin. DuckCam tests each and hides the ones that don't fit. Some only move the upper body or legs; turn on **Show Partial Body**.

**A posed character's weapon clips through its body.**
- Gear Rack poses were made for an empty-handed mannequin. Turn **Weapon In Hands** off, or move the weapon with **WEAPON GIZMO** in Pose Mode.

**A hidden piece of gear leaves bits behind.**
- Some armor and rigs are drawn as part of the clothing and can't be fully hidden. Helmets that cover hair don't give it back when hidden.

**Hand or foot IK looks twisted.**
- The limb reaches for the point you placed while keeping the pose's elbow or knee bend. Place the point closer to where the hand or foot already is.

**Text, border or mask are missing from high-res photos.**
- They currently only appear at **Screen** resolution. Frames and stickers work at any resolution.

**The screen flickers through zoomed-in views when I take a big photo.**
- That's normal: high-res photos are built from tiles. Keep still and frozen until it finishes.

**My video is missing.**
- CAPTURE must show FFmpeg as found. Put `ffmpeg.exe` in `BepInEx/plugins/DuckCam/` or on your PATH.

**Something stayed changed after I left DuckCam.**
- It shouldn't: every change is restored on exit, raid end, or failure. Please report it with **Copy Report** and the log.

## FAQ

**Is it safe? Can it break my profile or inventory?**
Photo characters, outfits and gear are display-only copies, and frozen time is given back to the raid clock. Nobody can be hurt while frozen. Spawned bots are real bots, so treat them like any bot.

**Does it work with Fika?**
Not yet. Freezing a raid other people are playing in isn't supported.

**Does it work in the Hideout?**
Yes. Weather isn't available there.

**Can I pose my real character while I play?**
No. The game's own animation and movement code rewrites your real character every frame, so DuckCam poses a photo copy instead.

**Why can't I move most bones in Pose Mode, only rotate them?**
Tarkov's skeleton is rotation-based; moving bones would stretch the body. Only the Hips move. With **AUTO IK** on, moving a hand or foot bends the whole limb.

**Are there face controls?**
No. Tarkov's heads have no face rig.

**Does it need a numpad?**
No. Every key works on tenkeyless keyboards and non-US layouts.

**Can I use a controller?**
Not yet. It's planned.

**Where are my photos?**
`Your-SPT-Folder/DuckCam/Screenshots/`. See [Installation](Installation.md#where-duckcam-keeps-your-files) for every folder.
