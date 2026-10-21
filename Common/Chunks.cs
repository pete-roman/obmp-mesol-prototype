using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MESOL.Common;

/// <summary>Cuts a text into pieces for <see cref="MesolRpc"/>.</summary>
public static class Chunks
{
    /// <summary>
    /// Characters per piece. Polish letters take two bytes in UTF-8 and some symbols three, so 400 characters stay
    /// under about 1200 bytes, inside one packet.
    /// </summary>
    public const int Size = 400;

    /// <summary>The most pieces one message may have (about 100 KB of text). Bigger messages are refused.</summary>
    public const int MaxCount = 256;

    /// <summary>The pieces of <paramref name="text"/>, never splitting a surrogate pair. An empty text is one empty piece.</summary>
    public static List<string> Split(string text, int size = Size)
    {
        List<string> pieces = new();
        text ??= string.Empty;
        int at = 0;
        while (at < text.Length)
        {
            int length = Math.Min(size, text.Length - at);
            if (length < text.Length - at && char.IsHighSurrogate(text[at + length - 1]))
            {
                length--;
            }
            pieces.Add(text.Substring(at, length));
            at += length;
        }
        if (pieces.Count == 0)
        {
            pieces.Add(string.Empty);
        }
        return pieces;
    }
}

/// <summary>
/// Puts pieces back together, per sender and request id. Messages arrive in order (reliable ordered delivery), but a
/// sender may have several requests in flight; anything half-finished for too long or too big is dropped.
/// </summary>
public sealed class ChunkAssembler<TSender>(int maxPending = 8) where TSender : notnull
{
    private sealed class Pending(int count)
    {
        public readonly string?[] Pieces = new string?[count];
        public int Received;
        public long Order;
    }

    private readonly Dictionary<(TSender Sender, long Id), Pending> _pending = new();
    private long _order;

    /// <summary>Adds a piece. Returns the whole text once the last piece is in; null until then or when the piece is bad.</summary>
    public string? Add(TSender sender, long requestId, int index, int count, string? chunk)
    {
        if (count < 1 || count > Chunks.MaxCount || index < 0 || index >= count || chunk is null || chunk.Length > Chunks.Size)
        {
            return null;
        }
        if (count == 1)
        {
            return chunk;
        }
        (TSender, long) key = (sender, requestId);
        if (!_pending.TryGetValue(key, out Pending? pending) || pending.Pieces.Length != count)
        {
            DropOldest(sender);
            pending = new Pending(count);
            _pending[key] = pending;
        }
        pending.Order = ++_order;
        if (pending.Pieces[index] is null)
        {
            pending.Received++;
        }
        pending.Pieces[index] = chunk;
        if (pending.Received < count)
        {
            return null;
        }
        _pending.Remove(key);
        StringBuilder whole = new();
        foreach (string? piece in pending.Pieces)
        {
            whole.Append(piece);
        }
        return whole.ToString();
    }

    /// <summary>Forgets everything from a sender (for example when they leave).</summary>
    public void Forget(TSender sender)
    {
        foreach ((TSender, long) key in _pending.Keys.Where(k => k.Sender.Equals(sender)).ToList())
        {
            _pending.Remove(key);
        }
    }

    internal int PendingCount => _pending.Count;

    private void DropOldest(TSender sender)
    {
        List<KeyValuePair<(TSender Sender, long Id), Pending>> mine = _pending.Where(p => p.Key.Sender.Equals(sender)).ToList();
        if (mine.Count >= maxPending)
        {
            _pending.Remove(mine.OrderBy(p => p.Value.Order).First().Key);
        }
    }
}
