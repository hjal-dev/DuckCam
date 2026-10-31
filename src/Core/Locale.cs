using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace DuckCam.Core
{
    internal sealed class LocaleFile
    {
        public string Format = "DuckCam.Locale";
        public int Version = 1;
        public string Language;
        public string Author;
        public string Notes;
        public Dictionary<string, string> Strings = new Dictionary<string, string>();
    }

    internal static class Locale
    {
        public const string BuiltIn = "English";
        public const string TemplateName = "_template";
        private const int MaxRecorded = 20000;

        private static Dictionary<string, string> _strings = new Dictionary<string, string>();
        private static readonly HashSet<string> _seen = new HashSet<string>();
        private static string _loaded;
        private static List<(string id, string name, string path)> _available;

        public static string Current => _loaded ?? BuiltIn;
        public static int Count => _strings.Count;
        public static int Seen => _seen.Count;
        public static string LastError { get; private set; }

        public static string UserDirectory => Path.Combine(DuckCamPlugin.DataRoot, "Locales");
        public static string PluginDirectory => Path.Combine(Path.GetDirectoryName(typeof(Locale).Assembly.Location) ?? "", "Locales");

        public static string T(string text) => Translate(text, true);

        public static string Show(string text) => Translate(text, false);

        private static readonly Regex Number = new Regex(@"(?<![A-Za-z0-9+.])(?<!\d-)-?\d+(?:[.,]\d+)?(?![A-Za-z0-9])(?!\.\s)", RegexOptions.Compiled);
        private static readonly Regex KeyLabel = new Regex(@"^(?:(?:(?:Left|Right)?(?:Ctrl|Control|Shift|Alt|Command|Windows)\+)+\S+|F\d{1,2})(?: \*)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Placeholder =new Regex(@"\{(\d+)\}", RegexOptions.Compiled);
        private static readonly Regex Guid = new Regex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.Compiled);
        private static readonly Dictionary<string, (string key, string[] numbers)> _shapes = new Dictionary<string, (string, string[])>();

        private static (string key, string[] numbers) Shape(string text)
        {
            if (_shapes.TryGetValue(text, out (string key, string[] numbers) cached))
            {
                return cached;
            }
            var numbers = new List<string>();
            string key = Number.Replace(text, m =>
            {
                numbers.Add(m.Value);
                return "{" + (numbers.Count - 1) + "}";
            });
            var shape = (key, numbers.ToArray());
            if (_shapes.Count > 50000)
            {
                _shapes.Clear();
            }
            _shapes[text] = shape;
            return shape;
        }

        private static bool Worth(string text, string key)
        {
            if (text.IndexOf(":\\", StringComparison.Ordinal) >= 0 || Guid.IsMatch(text))
            {
                return false;
            }
            foreach (char c in Placeholder.Replace(key, ""))
            {
                if (char.IsLetter(c))
                {
                    return true;
                }
            }
            return false;
        }

        private static string Translate(string text, bool record)
        {
            if (string.IsNullOrEmpty(text) || KeyLabel.IsMatch(text))
            {
                return text;
            }
            (string key, string[] numbers) = Shape(text);
            if (record && _seen.Count < MaxRecorded && Worth(text, key))
            {
                _seen.Add(key);
            }
            if (_strings.Count == 0)
            {
                return text;
            }
            if (_strings.TryGetValue(text, out string t) && !string.IsNullOrEmpty(t))
            {
                return t;
            }
            if (numbers.Length > 0 && _strings.TryGetValue(key, out string pattern) && !string.IsNullOrEmpty(pattern))
            {
                return Placeholder.Replace(pattern, m => int.TryParse(m.Groups[1].Value, out int i) && i < numbers.Length ? numbers[i] : m.Value);
            }
            return text;
        }

        public static IReadOnlyList<(string id, string name, string path)> Available()
        {
            if (_available != null)
            {
                return _available;
            }
            var found = new Dictionary<string, (string id, string name, string path)>(StringComparer.OrdinalIgnoreCase);
            foreach (string dir in new[] { PluginDirectory, UserDirectory })
            {
                try
                {
                    if (!Directory.Exists(dir))
                    {
                        continue;
                    }
                    foreach (string file in Directory.GetFiles(dir, "*.json"))
                    {
                        string id = Path.GetFileNameWithoutExtension(file);
                        if (id.StartsWith("_", StringComparison.Ordinal))
                        {
                            continue;
                        }
                        string name = id;
                        try
                        {
                            LocaleFile head = JsonConvert.DeserializeObject<LocaleFile>(File.ReadAllText(file));
                            if (!string.IsNullOrWhiteSpace(head?.Language))
                            {
                                name = head.Language.Trim();
                            }
                        }
                        catch (Exception ex)
                        {
                            name = id + " (unreadable)";
                            PhotoLog.Warn($"Locale {Path.GetFileName(file)} could not be read: {ex.Message}");
                        }
                        found[id] = (id, name, file);
                    }
                }
                catch (Exception ex)
                {
                    PhotoLog.Warn($"Locale folder {dir} could not be listed: {ex.Message}");
                }
            }
            _available = found.Values.OrderBy(v => v.name, StringComparer.OrdinalIgnoreCase).ToList();
            return _available;
        }

        public static void Refresh() => _available = null;

        public static void Load(string id)
        {
            LastError = null;
            _strings = new Dictionary<string, string>();
            _loaded = null;
            if (string.IsNullOrWhiteSpace(id) || string.Equals(id, BuiltIn, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            (string id, string name, string path) entry = Available().FirstOrDefault(a => string.Equals(a.id, id, StringComparison.OrdinalIgnoreCase));
            if (entry.path == null)
            {
                LastError = $"no locale file named '{id}.json' in {PluginDirectory} or {UserDirectory}.";
                PhotoLog.Warn("Locale: " + LastError);
                return;
            }
            try
            {
                LocaleFile file = JsonConvert.DeserializeObject<LocaleFile>(File.ReadAllText(entry.path));
                if (file?.Strings != null)
                {
                    foreach (KeyValuePair<string, string> kv in file.Strings)
                    {
                        if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                        {
                            _strings[kv.Key] = kv.Value;
                        }
                    }
                }
                _loaded = entry.id;
                PhotoLog.Info($"Locale: {entry.name} ({entry.id}.json), {_strings.Count} strings.");
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                PhotoLog.Warn($"Locale {entry.id}.json failed to load: {ex.Message}");
            }
        }

        public static string WriteTemplate()
        {
            Directory.CreateDirectory(UserDirectory);
            string path = Path.Combine(UserDirectory, TemplateName + ".json");
            var file = new LocaleFile
            {
                Language = "Language name shown in the menu",
                Author = "Your name",
                Notes = "Copy this file to <code>.json (for example de.json), set Language and Author, and fill in the values. Leave a value empty to keep the English text. Only strings DuckCam has shown on screen this game session are listed, so open every tab and window before writing the template. Lines with changing numbers or names can be left empty.",
                Strings = new SortedDictionary<string, string>(_seen.ToDictionary(s => s, s => _strings.TryGetValue(s, out string t) ? t : ""), StringComparer.Ordinal)
                    .ToDictionary(kv => kv.Key, kv => kv.Value)
            };
            File.WriteAllText(path, JsonConvert.SerializeObject(file, Formatting.Indented));
            PhotoLog.Info($"Locale template written: {path} ({file.Strings.Count} strings).");
            return path;
        }
    }
}
