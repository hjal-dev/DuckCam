# Translating

DuckCam's menus are written in English. A translation is a JSON file that maps each English text to your language. Anything missing stays English, so partial translations work.

## Where the files go

| Folder | Use |
|---|---|
| `BepInEx/plugins/DuckCam/Locales/` | Ship a translation with DuckCam or as your own small mod |
| `Your-SPT-Folder/DuckCam/Locales/` | Your own translations. A file here replaces one with the same name in the plugin folder |

The file name is the language code: `de.json`, `fr.json`, `pt-BR.json`, `ru.json`. Files starting with `_` are ignored (that's where the template goes).

## Make a translation

1. Start a raid, open DuckCam and visit **every tab and window**: the spawner, Pose Mode, the timeline, the keybinds window and so on. DuckCam records each text it shows.
2. **FRAME > Interface > Language > Write Template**. This writes `Your-SPT-Folder/DuckCam/Locales/_template.json` with every text seen this session.
3. Copy it to `<code>.json` in the same folder (for example `de.json`).
4. Fill in `Language` (the name shown in the menu, for example `Deutsch`) and `Author`, then translate the values:

```json
{
  "Format": "DuckCam.Locale",
  "Version": 1,
  "Language": "Deutsch",
  "Author": "Your name",
  "Notes": "",
  "Strings": {
    "Take Screenshot": "Screenshot aufnehmen",
    "Transparent Background": "Transparenter Hintergrund",
    "Off": "Aus",
    "On": "An"
  }
}
```

5. **FRAME > Interface > Language > Rescan Folder**, then pick your language.

## Numbers: `{0}`, `{1}`

Texts with a changing number are listed once, with the numbers swapped for `{0}`, `{1}` and so on. Keep the placeholders and move them wherever your language needs them:

```json
"Choose ({0} hidden)": "Wählen ({0} versteckt)",
"Add character · {0}/{1}": "Figur hinzufügen · {0}/{1}"
```

Numbers that are part of a key or name (F6, Ctrl+F12, 16K) are left as they are.

**Not translated at all:** key names themselves (F6, Alt+1, Ctrl+F12) and the DEBUG report. Everything around them is, including the Keybinds window's section and action names.

## Rules

- Keys must match the English text exactly, including capitals and punctuation. Labels are shown in capitals on screen, but the key is the text as written, which is what the template contains.
- An empty value keeps the English text.
- Texts with changing names (a pose or item name) can't be matched reliably; leave them empty.
- Keep translations short. Labels share a row with their control, and long text gets cut off.

## Updating after a DuckCam update

Select your language, then **Write Template** again. Your existing translations are filled in and only new texts are empty.
