using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MESOL.Server.Cards;
using Microsoft.Extensions.Logging;
using ReadyM.SDK.Attributes;

namespace MESOL.Server.Storage;

/// <summary>
/// Cards by ReadyM account id: <c>&lt;data folder&gt;/cards/&lt;id&gt;.json</c>. Kept in memory once read and
/// written on every change (not at shutdown, which is not proven to run). Used from the game thread only;
/// the disk work happens on <see cref="FileWriter"/>'s thread.
/// </summary>
[Service]
public sealed partial class CardStore(MesolData data, ILogger logger)
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly Dictionary<Guid, CardFile> _cards = new();
    private readonly HashSet<Guid> _unreadable = new();
    private FileWriter? _writer;

    /// <summary>For the tests: a store on a folder of their own.</summary>
    internal string? FolderOverride { get; init; }

    private string Folder => FolderOverride ?? data.Cards;

    private void Start()
    {
        _writer = new FileWriter((path, exception) => logger.LogError(exception, "MESOL: could not save {Path}.", path));
        logger.LogInformation("MESOL: cards are saved in {Folder}.", Folder);
    }

    private void Stop()
    {
        _writer?.Dispose();
        _writer = null;
    }

    /// <summary>The owner's card, or a new empty one (saved only once something is put on it).</summary>
    public CardFile GetOrCreate(Guid owner) => Find(owner) ?? Remember(CardFile.New(owner, DateTimeOffset.UtcNow));

    /// <summary>The owner's card, or null when they have none yet.</summary>
    public CardFile? Find(Guid owner)
    {
        if (_cards.TryGetValue(owner, out CardFile? card))
        {
            return card;
        }
        string path = PathOf(owner);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            CardFile? loaded = JsonSerializer.Deserialize<CardFile>(File.ReadAllText(path), Json);
            if (loaded is null || loaded.SchemaVersion > CardFile.CurrentSchemaVersion || loaded.Owner != owner)
            {
                throw new InvalidDataException($"schema {loaded?.SchemaVersion}, owner {loaded?.Owner}");
            }
            return Remember(loaded);
        }
        catch (Exception exception)
        {
            // Never write over a file we could not read: the owner keeps an empty card until staff look at it.
            _unreadable.Add(owner);
            logger.LogError(exception, "MESOL: could not read {Path}; that card is read-only until the file is fixed.", path);
            return Remember(CardFile.New(owner, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>False when the card's file exists but could not be read (see the log).</summary>
    public bool CanSave(Guid owner) => !_unreadable.Contains(owner);

    public void Save(CardFile card)
    {
        if (!CanSave(card.Owner))
        {
            return;
        }
        string json = JsonSerializer.Serialize(card, Json);
        if (_writer is null)
        {
            FileWriter.WriteAtomic(PathOf(card.Owner), json);
            return;
        }
        _writer.Write(PathOf(card.Owner), json);
    }

    private CardFile Remember(CardFile card)
    {
        _cards[card.Owner] = card;
        return card;
    }

    private string PathOf(Guid owner) => Path.Combine(Folder, owner.ToString("D") + ".json");
}
