using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MESOL.Server.Cards;
using MESOL.Server.Storage;
using Microsoft.Extensions.Logging;
using ReadyM.SDK.Attributes;

namespace MESOL.Server.Relations;

/// <summary>
/// One account's relations, saved as <c>relations/&lt;ReadyM id&gt;.json</c>. Introductions and friendships are
/// mutual, so both accounts' files hold them; a block is one-sided.
/// </summary>
public sealed class RelationFile
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public Guid Owner { get; set; }

    /// <summary>Accounts this one has been introduced to (both agreed), and since when.</summary>
    public Dictionary<Guid, DateTimeOffset> Known { get; set; } = new();

    /// <summary>Accounts this one is friends with (both agreed).</summary>
    public Dictionary<Guid, DateTimeOffset> Friends { get; set; } = new();

    /// <summary>Accounts this one has blocked: they see only Public fields and cannot send offers.</summary>
    public Dictionary<Guid, DateTimeOffset> Blocked { get; set; } = new();
}

/// <summary>How two accounts stand, from the viewer's side.</summary>
public enum Relation
{
    Stranger,
    Known,
    Friend,

    /// <summary>One of the two has blocked the other.</summary>
    Blocked,
}

/// <summary>
/// Relations by ReadyM account id, kept in memory once read and written on every change through
/// <see cref="FileWriter"/>, like <see cref="CardStore"/>. The rules for who sees what are here too.
/// </summary>
[Service]
public sealed partial class RelationStore(MesolData data, ILogger logger)
{
    private readonly Dictionary<Guid, RelationFile> _files = new();
    private FileWriter? _writer;

    internal string? FolderOverride { get; init; }

    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    private string Folder => FolderOverride ?? data.Relations;

    private void Start()
    {
        _writer = new FileWriter((path, exception) => logger.LogError(exception, "MESOL: could not save {Path}.", path));
    }

    private void Stop()
    {
        _writer?.Dispose();
        _writer = null;
    }

    public RelationFile Get(Guid owner)
    {
        if (_files.TryGetValue(owner, out RelationFile? file))
        {
            return file;
        }
        string path = PathOf(owner);
        file = new RelationFile { Owner = owner };
        if (File.Exists(path))
        {
            try
            {
                RelationFile? loaded = JsonSerializer.Deserialize<RelationFile>(File.ReadAllText(path), CardStore.Json);
                if (loaded is not null && loaded.Owner == owner && loaded.SchemaVersion <= RelationFile.CurrentSchemaVersion)
                {
                    file = loaded;
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "MESOL: could not read {Path}; that account starts with no relations.", path);
            }
        }
        _files[owner] = file;
        return file;
    }

    /// <summary>How <paramref name="viewer"/> stands towards <paramref name="owner"/>.</summary>
    public Relation Between(Guid viewer, Guid owner)
    {
        RelationFile mine = Get(viewer);
        RelationFile theirs = Get(owner);
        if (mine.Blocked.ContainsKey(owner) || theirs.Blocked.ContainsKey(viewer))
        {
            return Relation.Blocked;
        }
        if (theirs.Friends.ContainsKey(viewer))
        {
            return Relation.Friend;
        }
        return theirs.Known.ContainsKey(viewer) ? Relation.Known : Relation.Stranger;
    }

    /// <summary>The level of the owner's card the viewer may see.</summary>
    public Visibility LevelOf(Guid viewer, Guid owner)
    {
        if (viewer == owner)
        {
            return Visibility.Private;
        }
        return Between(viewer, owner) switch
        {
            Relation.Friend => Visibility.Friends,
            Relation.Known => Visibility.Known,
            _ => Visibility.Public,
        };
    }

    /// <summary>Both agreed to an introduction: each now sees the other's Known fields.</summary>
    public void Introduce(Guid a, Guid b)
    {
        DateTimeOffset now = Clock();
        Get(a).Known[b] = now;
        Get(b).Known[a] = now;
        Save(a, b);
    }

    /// <summary>Both agreed to be friends: each sees the other's Friends fields (and Known ones).</summary>
    public void Befriend(Guid a, Guid b)
    {
        DateTimeOffset now = Clock();
        foreach ((Guid one, Guid other) in new[] { (a, b), (b, a) })
        {
            RelationFile file = Get(one);
            file.Known.TryAdd(other, now);
            file.Friends[other] = now;
        }
        Save(a, b);
    }

    /// <summary>Ends a friendship on both sides; the introduction stays.</summary>
    public bool Unfriend(Guid a, Guid b)
    {
        bool changed = Get(a).Friends.Remove(b) | Get(b).Friends.Remove(a);
        if (changed)
        {
            Save(a, b);
        }
        return changed;
    }

    /// <summary>Takes an introduction back (and any friendship): both are strangers again.</summary>
    public bool Forget(Guid a, Guid b)
    {
        bool changed = false;
        foreach ((Guid one, Guid other) in new[] { (a, b), (b, a) })
        {
            RelationFile file = Get(one);
            changed |= file.Known.Remove(other);
            changed |= file.Friends.Remove(other);
        }
        if (changed)
        {
            Save(a, b);
        }
        return changed;
    }

    /// <summary><paramref name="a"/> blocks <paramref name="b"/>: everything between them ends, and b can't send offers.</summary>
    public void Block(Guid a, Guid b)
    {
        Forget(a, b);
        Get(a).Blocked[b] = Clock();
        Save(a, b);
    }

    public bool Unblock(Guid a, Guid b)
    {
        bool changed = Get(a).Blocked.Remove(b);
        if (changed)
        {
            Save(a, b);
        }
        return changed;
    }

    private void Save(params Guid[] owners)
    {
        foreach (Guid owner in owners.Distinct())
        {
            string json = JsonSerializer.Serialize(Get(owner), CardStore.Json);
            if (_writer is null)
            {
                FileWriter.WriteAtomic(PathOf(owner), json);
            }
            else
            {
                _writer.Write(PathOf(owner), json);
            }
        }
    }

    private string PathOf(Guid owner) => Path.Combine(Folder, owner.ToString("D") + ".json");
}
