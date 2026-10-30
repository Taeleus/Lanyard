using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Lanyard;
using Sandbox.ModAPI;

// Test storage is intentionally real disk storage: reads use the same canonical
// path as the production atomic writer, rather than a commit-on-dispose string.
public sealed class GlobalStorageFiles
{
    private readonly string root;
    public GlobalStorageFiles(string root) { this.root = root; }
    public string PathFor(string file) => Path.Combine(root, "Storage", file);
    public string this[string file]
    {
        get => File.ReadAllText(PathFor(file));
        set
        {
            Directory.CreateDirectory(Path.Combine(root, "Storage"));
            File.WriteAllText(PathFor(file), value, new UTF8Encoding(false));
        }
    }
    public bool ContainsKey(string file) => File.Exists(PathFor(file));
    public bool Remove(string file)
    {
        var exists = ContainsKey(file);
        File.Delete(PathFor(file));
        return exists;
    }
    public void Clear()
    {
        var directory = Path.Combine(root, "Storage");
        if (Directory.Exists(directory))
            foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
    }
}

internal static class CooldownTestStorage
{
    private static readonly string root = Path.Combine(Path.GetTempPath(), "Lanyard.CombatLockout.Tests." + Guid.NewGuid().ToString("N"));
    private static int directories;
    public static string CreateRoot()
    {
        var path = Path.Combine(root, (++directories).ToString());
        Directory.CreateDirectory(path);
        return path;
    }
    public static FieldInfo Field(string name) => typeof(CombatCooldownStore).GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
    public static void Initialize()
    {
        var write = (Action<FileStream, byte[]>)Field("writeRecord").GetValue(null);
        Field("writeRecord").SetValue(null, (Action<FileStream, byte[]>)((stream, bytes) =>
        {
            var utilities = MyAPIGateway.Utilities;
            utilities.WriteAttempts++;
            if (utilities.FailWrites) throw new IOException("Injected write failure.");
            utilities.Writes++;
            write(stream, bytes);
        }));
        var flush = (Action<FileStream>)Field("flushRecord").GetValue(null);
        Field("flushRecord").SetValue(null, (Action<FileStream>)(stream =>
        {
            MyAPIGateway.Utilities.Flushes++;
            flush(stream);
        }));
        var publish = (Action<string, string>)Field("publishRecord").GetValue(null);
        Field("publishRecord").SetValue(null, (Action<string, string>)((temporary, path) =>
        {
            MyAPIGateway.Utilities.Publications++;
            publish(temporary, path);
            MyAPIGateway.Utilities.Commits++;
        }));
    }
    public static void Cleanup()
    {
        var path = Path.GetFullPath(root);
        var parent = Path.GetFullPath(Path.GetTempPath());
        if (!path.StartsWith(parent, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(path).StartsWith("Lanyard.CombatLockout.Tests.", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing cleanup outside the owned test root.");
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}

namespace VRage.FileSystem
{
    public static class MyFileSystem
    {
        public static string UserDataPath;
    }
}
