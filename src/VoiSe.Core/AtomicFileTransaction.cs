namespace VoiSe.Core;

/// <summary>Same-directory commit for JSON and rendered audio. Failed writes preserve the destination.</summary>
public static class AtomicFileTransaction
{
    private static readonly object CommitSync = new();

    public static void Write(string destination, Action<string> write, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            write(temporary);
            cancellationToken.ThrowIfCancellationRequested();
            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                stream.Flush(flushToDisk: true);
            lock (CommitSync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(destination)) File.Replace(temporary, destination, destination + ".bak");
                else File.Move(temporary, destination);
            }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
