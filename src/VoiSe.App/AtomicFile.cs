using System.Text;
using System.Text.Json;

namespace VoiSe.App;

// Same-volume commit boundary shared by settings, library, presets and scenes.
internal static class AtomicFile
{
    private static readonly object Sync = new();
    public static void WriteAllText(string path, string text) => Commit(path, temp =>
    {
        using var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(Encoding.UTF8.GetBytes(text));
        stream.Flush(flushToDisk: true);
    });
    public static void Copy(string source, string destination)
    {
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase)) return;
        Commit(destination, temp =>
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            output.Flush(flushToDisk: true);
        });
    }
    private static void Commit(string path, Action<string> write)
    {
        lock (Sync)
        {
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                write(temp);
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    public static T? ReadJson<T>(string path, JsonSerializerOptions options)
    {
        lock (Sync)
        {
            try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), options) ?? throw new JsonException("Empty JSON document."); }
            catch (JsonException)
            {
                // Retain evidence even when there is no usable backup.
                File.Copy(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"), false);
                var recovered = JsonSerializer.Deserialize<T>(File.ReadAllText(path + ".bak"), options)
                    ?? throw new JsonException("Empty JSON backup.");
                File.Copy(path + ".bak", path, true);
                StartupLog.Write("Recovered JSON backup: " + path);
                return recovered;
            }
        }
    }
}
