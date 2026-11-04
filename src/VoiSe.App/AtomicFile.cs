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
        lock (Sync) VoiSe.Core.AtomicFileTransaction.Write(path, write);
    }

    public static void RenameContent(string? oldPath, string newPath, string content)
    {
        lock (Sync)
        {
            if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase)) { WriteAllText(newPath, content); return; }
            if (File.Exists(newPath)) throw new IOException("The destination already exists.");
            WriteAllText(newPath, content);
            try { if (!string.IsNullOrWhiteSpace(oldPath) && File.Exists(oldPath)) File.Delete(oldPath); }
            catch { File.Delete(newPath); throw; }
        }
    }
    public static T? ReadJson<T>(string path, JsonSerializerOptions options)
    {
        lock (Sync)
        {
            try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), options) ?? throw new JsonException(UiText.L("Empty_JSON_document_92a65111")); }
            catch (JsonException)
            {
                // Retain evidence even when there is no usable backup.
                File.Copy(path, path + ".corrupt-" + Guid.NewGuid().ToString("N"), false);
                var recovered = JsonSerializer.Deserialize<T>(File.ReadAllText(path + ".bak"), options)
                    ?? throw new JsonException(UiText.L("Empty_JSON_backup_89258282"));
                File.Copy(path + ".bak", path, true);
                StartupLog.Write("Recovered JSON backup: " + path);
                return recovered;
            }
        }
    }
}
