using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using VRage.FileSystem;
using VRage.Utils;

namespace Lanyard;

/// <summary>Local presentation and automation preferences; never changes safety policy.</summary>
internal static class LanyardPreferences
{
    private const int MaximumLength = 32768;
    private static LanyardSettings current = new LanyardSettings();
    private static bool loaded;
    public static event Action Changed;
    public static LanyardSettings Current { get { EnsureLoaded(); return current; } }

    public static void EnsureLoaded()
    {
        if (loaded || string.IsNullOrEmpty(MyFileSystem.UserDataPath)) return;
        loaded = true;
        current = Load(PathFor());
    }

    public static void Change(Action<LanyardSettings> change)
    {
        if (change == null) return;
        EnsureLoaded();
        var updated = current.Copy();
        change(updated);
        Apply(updated);
    }

    public static void ResetDefaults()
    {
        EnsureLoaded();
        Apply(new LanyardSettings());
    }

    private static void Apply(LanyardSettings updated)
    {
        updated.Normalize();
        current = updated;
        try { Save(PathFor(), updated); }
        catch (Exception exception)
        {
            MyLog.Default?.WriteLineAndConsole("Lanyard preferences apply for this session but could not be saved: " + exception.Message);
        }
        try { Changed?.Invoke(); }
        catch (Exception exception) { MyLog.Default?.WriteLineAndConsole("Lanyard preference refresh failed: " + exception); }
    }

    private static string PathFor()
    {
        if (string.IsNullOrEmpty(MyFileSystem.UserDataPath)) throw new InvalidOperationException("Game user storage is not available.");
        return Path.Combine(MyFileSystem.UserDataPath, "Storage", "Lanyard.Preferences.v1.xml");
    }

    internal static LanyardSettings Load(string path)
    {
        var result = new LanyardSettings();
        try
        {
            if (!File.Exists(path)) return result;
            var document = new XmlDocument { XmlResolver = null };
            using (var stream = File.OpenRead(path))
            {
                if (stream.Length > MaximumLength) throw new InvalidDataException("Preference file is too large.");
                using (var reader = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumLength
                })) document.Load(reader);
            }
            var root = document.DocumentElement;
            if (root == null || root.Name != "LanyardPreferences" || root.GetAttribute("Version") != "1")
                throw new InvalidDataException("Preference file version is unsupported.");
            result.WelderSupply = ReadBool(root, "WelderSupply", true);
            result.GrinderReturns = ReadBool(root, "GrinderReturns", true);
            result.DrillReturns = ReadBool(root, "DrillReturns", true);
            result.StartingComponents = ReadBool(root, "StartingComponents", true);
            result.DrillActiveOnly = ReadBool(root, "DrillActiveOnly", false);
            result.ManualPaused = ReadBool(root, "ManualPaused", false);
            result.HudMode = ReadEnum(root, "HudMode", HudMode.Off);
            result.HudPosition = ReadEnum(root, "HudPosition", HudPosition.TopLeft);
            if (float.TryParse(root.GetAttribute("HudScale"), NumberStyles.Float, CultureInfo.InvariantCulture, out var scale)) result.HudScale = scale;
            if (float.TryParse(root.GetAttribute("HudOpacity"), NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity)) result.HudOpacity = opacity;
            result.HudCustomPosition = ReadBool(root, "HudCustomPosition", false);
            if (float.TryParse(root.GetAttribute("HudX"), NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) result.HudX = x;
            if (float.TryParse(root.GetAttribute("HudY"), NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) result.HudY = y;
            foreach (ShortcutAction action in Enum.GetValues(typeof(ShortcutAction)))
            {
                var node = root.SelectSingleNode("Shortcuts/" + action) as XmlElement;
                if (node == null) continue;
                int.TryParse(node.GetAttribute("Key"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var key);
                result.SetBinding(action, new ShortcutBinding
                {
                    Key = key, Control = ReadBool(node, "Control", false), Alt = ReadBool(node, "Alt", false), Shift = ReadBool(node, "Shift", false)
                });
            }
            result.Normalize();
            return result;
        }
        catch (Exception exception)
        {
            MyLog.Default?.WriteLineAndConsole("Lanyard preferences ignored: " + exception.Message);
            return new LanyardSettings();
        }
    }

    internal static void Save(string path, LanyardSettings settings)
    {
        var value = settings.Copy();
        value.Normalize();
        var document = new XmlDocument();
        var root = document.CreateElement("LanyardPreferences");
        document.AppendChild(root);
        root.SetAttribute("Version", "1");
        root.SetAttribute("WelderSupply", value.WelderSupply.ToString());
        root.SetAttribute("GrinderReturns", value.GrinderReturns.ToString());
        root.SetAttribute("DrillReturns", value.DrillReturns.ToString());
        root.SetAttribute("StartingComponents", value.StartingComponents.ToString());
        root.SetAttribute("DrillActiveOnly", value.DrillActiveOnly.ToString());
        root.SetAttribute("ManualPaused", value.ManualPaused.ToString());
        root.SetAttribute("HudMode", value.HudMode.ToString());
        root.SetAttribute("HudPosition", value.HudPosition.ToString());
        root.SetAttribute("HudScale", value.HudScale.ToString("R", CultureInfo.InvariantCulture));
        root.SetAttribute("HudOpacity", value.HudOpacity.ToString("R", CultureInfo.InvariantCulture));
        root.SetAttribute("HudCustomPosition", value.HudCustomPosition.ToString());
        root.SetAttribute("HudX", value.HudX.ToString("R", CultureInfo.InvariantCulture));
        root.SetAttribute("HudY", value.HudY.ToString("R", CultureInfo.InvariantCulture));
        var shortcuts = document.CreateElement("Shortcuts");
        root.AppendChild(shortcuts);
        foreach (ShortcutAction action in Enum.GetValues(typeof(ShortcutAction)))
        {
            var binding = value.Binding(action);
            var node = document.CreateElement(action.ToString());
            node.SetAttribute("Key", binding.Key.ToString(CultureInfo.InvariantCulture));
            node.SetAttribute("Control", binding.Control.ToString());
            node.SetAttribute("Alt", binding.Alt.ToString());
            node.SetAttribute("Shift", binding.Shift.ToString());
            shortcuts.AppendChild(node);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, CloseOutput = false }))
                    document.Save(writer);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static bool ReadBool(XmlElement node, string name, bool fallback) =>
        bool.TryParse(node.GetAttribute(name), out var value) ? value : fallback;

    private static T ReadEnum<T>(XmlElement node, string name, T fallback) where T : struct =>
        Enum.TryParse(node.GetAttribute(name), out T value) && Enum.IsDefined(typeof(T), value) ? value : fallback;
}
