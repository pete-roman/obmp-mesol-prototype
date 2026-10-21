using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace MESOL.Server.Storage;

/// <summary>
/// Writes files on its own thread, so disk IO never holds up the game thread. Only the newest content per path is
/// written. Every write is atomic: a temporary file first, then a swap that keeps the previous file as
/// <c>.bak</c>, so a crash never leaves half a card behind.
/// </summary>
public sealed class FileWriter : IDisposable
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly object _pendingGate = new();
    private readonly object _diskGate = new();
    private readonly Dictionary<string, string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly AutoResetEvent _wake = new(false);
    private readonly Action<string, Exception> _onError;
    private readonly Thread _thread;
    private volatile bool _stopping;

    public FileWriter(Action<string, Exception> onError)
    {
        _onError = onError;
        _thread = new Thread(Run) { IsBackground = true, Name = "MESOL file writer" };
        _thread.Start();
    }

    public void Write(string path, string content)
    {
        lock (_pendingGate)
        {
            _pending[path] = content;
        }
        _wake.Set();
    }

    /// <summary>Writes everything still waiting, on the calling thread.</summary>
    public void Flush() => Drain();

    public void Dispose()
    {
        _stopping = true;
        _wake.Set();
        _thread.Join(TimeSpan.FromSeconds(2));
        Drain();
    }

    private void Run()
    {
        while (!_stopping)
        {
            _wake.WaitOne();
            Drain();
        }
    }

    private void Drain()
    {
        // One drain at a time, so an older copy of a file can never land after a newer one.
        lock (_diskGate)
        {
            KeyValuePair<string, string>[] batch;
            lock (_pendingGate)
            {
                batch = _pending.ToArray();
                _pending.Clear();
            }
            foreach ((string path, string content) in batch)
            {
                try
                {
                    WriteAtomic(path, content);
                }
                catch (Exception exception)
                {
                    _onError(path, exception);
                }
            }
        }
    }

    internal static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, content, Utf8);
        if (File.Exists(path))
        {
            File.Replace(temporary, path, path + ".bak");
        }
        else
        {
            File.Move(temporary, path);
        }
    }
}
