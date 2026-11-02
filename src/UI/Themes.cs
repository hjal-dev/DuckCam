using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DuckCam.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed class ThemeRole
    {
        public string Id;
        public string Name;
        public Func<HudPalette, Color> Get;
        public Action<HudPalette, Color> Set;
    }

    internal sealed class ThemeFile
    {
        public string Name;
        public string Base = nameof(UiPalette.NeonRed);
        public float Angle = 180f;
        public Dictionary<string, string> Colors = new Dictionary<string, string>();
    }

    internal static class Themes
    {
        public static readonly ThemeRole[] Roles =
        {
            Role("primary", "Titles and labels", p => p.Label, (p, c) => p.Label = c),
            Role("text", "Text and values", p => p.Value, (p, c) => p.Value = c),
            Role("faded", "Faded text", p => p.Dim, (p, c) => p.Dim = c),
            Role("hud", "HUD text", p => p.Hud, (p, c) => p.Hud = c),
            Role("accent", "Accent", p => p.Highlight, (p, c) => p.Highlight = c),
            Role("accent-text", "Text on accent", p => p.HighlightText, (p, c) => p.HighlightText = c),
            Role("keycap", "Key hints", p => p.KeyCap, (p, c) => p.KeyCap = c),
            Role("line", "Lines", p => p.Line, (p, c) => p.Line = c),
            Role("line-bright", "Frames and bright lines", p => p.LineBright, (p, c) => p.LineBright = c),
            Role("row", "Row background", p => p.RowFill, (p, c) => p.RowFill = c),
            Role("row-selected", "Selected row", p => p.RowFillSelected, (p, c) => p.RowFillSelected = c),
            Role("tab", "Tabs and buttons", p => p.TabFill, (p, c) => p.TabFill = c),
            Role("tab-active", "Active tab and hover", p => p.TabFillActive, (p, c) => p.TabFillActive = c),
            Role("value-box", "Value box", p => p.ValueBox, (p, c) => p.ValueBox = c),
            Role("value-box-text", "Value box text", p => p.ValueBoxText, (p, c) => p.ValueBoxText = c),
            Role("card", "Cards and tiles", p => p.Card, (p, c) => p.Card = c),
            Role("window-start", "Window background (start)", p => p.WindowStart, (p, c) => p.WindowStart = c),
            Role("window-end", "Window background (end)", p => p.WindowEnd, (p, c) => p.WindowEnd = c)
        };

        private static ThemeRole Role(string id, string name, Func<HudPalette, Color> get, Action<HudPalette, Color> set)
        {
            return new ThemeRole { Id = id, Name = name, Get = get, Set = set };
        }

        public static string Directory => Path.Combine(DuckCamPlugin.DataRoot, "Themes");

        public static string[] List() => System.IO.Directory.Exists(Directory) ? ContentPacks.List(Directory) : Array.Empty<string>();

        public static UiPalette BaseOf(ThemeFile file)
        {
            return file != null && Enum.TryParse(file.Base, true, out UiPalette p) ? p : UiPalette.NeonRed;
        }

        public static HudPalette Build(ThemeFile file)
        {
            HudPalette palette = HudTheme.PaletteFor(BaseOf(file));
            if (file == null)
            {
                return palette;
            }
            palette.GradientAngle = file.Angle;
            if (file.Colors == null)
            {
                return palette;
            }
            foreach (ThemeRole role in Roles)
            {
                if (file.Colors.TryGetValue(role.Id, out string text) && ThemeColors.TryParse(text, out Color c))
                {
                    role.Set(palette, c);
                }
            }
            return palette;
        }

        public static ThemeFile Capture(HudPalette palette, UiPalette basePalette, string name)
        {
            var file = new ThemeFile { Name = name, Base = basePalette.ToString(), Angle = palette.GradientAngle };
            foreach (ThemeRole role in Roles)
            {
                file.Colors[role.Id] = ThemeColors.ToHex(role.Get(palette));
            }
            return file;
        }

        public static void Derive(HudPalette p, Color primary, Color accent)
        {
            p.Label = ThemeColors.WithAlpha(primary, 1f);
            p.Value = ThemeColors.WithAlpha(ThemeColors.Mix(primary, Color.white, 0.65f), 1f);
            p.Dim = ThemeColors.WithAlpha(primary, 0.5f);
            p.Hud = ThemeColors.WithAlpha(primary, 0.72f);
            p.Line = ThemeColors.WithAlpha(primary, 0.26f);
            p.LineBright = ThemeColors.WithAlpha(primary, 0.95f);
            p.RowFill = ThemeColors.Shade(primary, 0.09f, 0.8f);
            p.RowFillSelected = ThemeColors.Shade(primary, 0.3f, 0.72f);
            p.TabFill = ThemeColors.Shade(primary, 0.08f, 0.82f);
            p.TabFillActive = ThemeColors.Shade(primary, 0.4f, 0.78f);
            p.ValueBox = ThemeColors.Shade(primary, 0.42f, 0.95f);
            p.ValueBoxText = ThemeColors.WithAlpha(ThemeColors.Mix(primary, Color.white, 0.1f), 1f);
            p.Card = ThemeColors.Shade(primary, 0.4f, 0.35f);
            p.Highlight = ThemeColors.WithAlpha(accent, 1f);
            p.KeyCap = ThemeColors.WithAlpha(accent, 1f);
            p.HighlightText = ThemeColors.Luminance(accent) > 0.45f ? ThemeColors.Shade(accent, 0.15f, 1f) : Color.white;
        }

        public static ThemeFile Load(string key)
        {
            try
            {
                return JsonConvert.DeserializeObject<ThemeFile>(File.ReadAllText(ContentPacks.FileOf(Directory, key)));
            }
            catch (Exception ex)
            {
                PhotoLog.WarnOnce("theme:" + key, $"Theme '{key}' could not be read: {ex.Message}");
                return null;
            }
        }

        public static string Save(ThemeFile file, string key)
        {
            string pack = ContentPacks.PackOf(key);
            string name = new string((ContentPacks.NameOf(key) ?? "").Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)).ToArray()).Trim();
            if (name.Length == 0)
            {
                throw new InvalidOperationException("Type a theme name first.");
            }
            string finalKey = pack == null ? name : pack + "/" + name;
            file.Name = name;
            string path = ContentPacks.FileOf(Directory, finalKey);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonConvert.SerializeObject(file, Formatting.Indented));
            ContentPacks.Invalidate(Directory);
            return finalKey;
        }

        public static void Delete(string key)
        {
            string path = ContentPacks.FileOf(Directory, key);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            ContentPacks.Invalidate(Directory);
        }

        public static string ToCss(ThemeFile file)
        {
            var sb = new StringBuilder();
            sb.Append(":root {\n");
            sb.Append($"  --base: {file.Base.ToLowerInvariant()};\n");
            sb.Append($"  --angle: {file.Angle.ToString("0", CultureInfo.InvariantCulture)}deg;\n");
            foreach (ThemeRole role in Roles)
            {
                if (file.Colors.TryGetValue(role.Id, out string value))
                {
                    sb.Append($"  --{role.Id}: {value};\n");
                }
            }
            if (file.Colors.TryGetValue("window-start", out string a) && file.Colors.TryGetValue("window-end", out string b))
            {
                sb.Append($"  --window: linear-gradient({file.Angle.ToString("0", CultureInfo.InvariantCulture)}deg, {a}, {b});\n");
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        public static ThemeFile FromCss(string css, out int read)
        {
            read = 0;
            var file = new ThemeFile();
            if (string.IsNullOrWhiteSpace(css))
            {
                return file;
            }
            foreach (string raw in css.Split(new[] { ';', '\n', '{', '}' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim();
                if (!line.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }
                int colon = line.IndexOf(':');
                if (colon < 0)
                {
                    continue;
                }
                string name = line.Substring(2, colon - 2).Trim().ToLowerInvariant();
                string value = line.Substring(colon + 1).Trim();
                if (name == "base")
                {
                    file.Base = value;
                    read++;
                }
                else if (name == "angle")
                {
                    if (float.TryParse(value.Replace("deg", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float angle))
                    {
                        file.Angle = angle;
                        read++;
                    }
                }
                else if (name == "window" && value.StartsWith("linear-gradient", StringComparison.OrdinalIgnoreCase))
                {
                    read += ReadGradient(value, file);
                }
                else if (Roles.Any(r => r.Id == name) && ThemeColors.TryParse(value, out Color c))
                {
                    file.Colors[name] = ThemeColors.ToHex(c);
                    read++;
                }
            }
            return file;
        }

        private static string StripStop(string part)
        {
            int space = part.LastIndexOf(' ');
            if (space > 0 && part.EndsWith("%", StringComparison.Ordinal) && part.IndexOf(')', space) < 0)
            {
                return part.Substring(0, space).Trim();
            }
            return part;
        }

        private static int ReadGradient(string value, ThemeFile file)
        {
            int open = value.IndexOf('(');
            int close = value.LastIndexOf(')');
            if (open < 0 || close <= open)
            {
                return 0;
            }
            var parts = new List<string>();
            int depth = 0;
            var current = new StringBuilder();
            foreach (char ch in value.Substring(open + 1, close - open - 1))
            {
                if (ch == '(')
                {
                    depth++;
                }
                else if (ch == ')')
                {
                    depth--;
                }
                if (ch == ',' && depth == 0)
                {
                    parts.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
                }
                current.Append(ch);
            }
            parts.Add(current.ToString().Trim());
            int read = 0;
            int first = 0;
            if (parts.Count > 0 && parts[0].EndsWith("deg", StringComparison.OrdinalIgnoreCase)
                && float.TryParse(parts[0].Substring(0, parts[0].Length - 3), NumberStyles.Float, CultureInfo.InvariantCulture, out float angle))
            {
                file.Angle = angle;
                first = 1;
                read++;
            }
            if (parts.Count > first && ThemeColors.TryParse(StripStop(parts[first]), out Color a))
            {
                file.Colors["window-start"] = ThemeColors.ToHex(a);
                read++;
            }
            if (parts.Count > first + 1 && ThemeColors.TryParse(StripStop(parts[parts.Count - 1]), out Color b))
            {
                file.Colors["window-end"] = ThemeColors.ToHex(b);
                read++;
            }
            return read;
        }
    }
}
