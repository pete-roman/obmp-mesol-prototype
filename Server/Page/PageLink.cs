using System;
using System.Collections.Generic;
using MESOL.Common;
using Microsoft.Extensions.Logging;
using ReadyM.Api.Idents;
using ReadyM.Relay.Server.Sdk.Rpc;
using ReadyM.SDK.Attributes;

namespace MESOL.Server.Page;

/// <summary>
/// What the server knows about each player's card page: whether it has loaded (the client says so), so /mylore and
/// /lore can open it instead of printing to chat, and how to send it something. The sending itself is set up by
/// <see cref="PageRpcHandlers"/>, so the tests can use this class alone.
/// </summary>
[Service]
public sealed partial class PageLink
{
    /// <summary>Requests per player in <see cref="RateWindow"/>. Opening the page and saving a few fields takes a handful.</summary>
    public const int RateLimit = 40;

    public static readonly TimeSpan RateWindow = TimeSpan.FromSeconds(10);

    // PlayerId changes on reconnect; a stale entry only means /mylore opens a page that is not there (and the
    // player can still use /mylore card).
    private readonly HashSet<PlayerId> _ready = new();
    private readonly Dictionary<PlayerId, Queue<DateTimeOffset>> _recent = new();

    /// <summary>Sends a whole answer (request id, JSON) to a player, in pieces. Set by <see cref="PageRpcHandlers"/>.</summary>
    public Action<PlayerId, long, string>? Send { get; set; }

    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    public bool HasPage(PlayerId player) => _ready.Contains(player);

    public void SetReady(PlayerId player) => _ready.Add(player);

    /// <summary>The server's own message to a player's page (request id 0).</summary>
    public void Push(PlayerId player, string json) => Send?.Invoke(player, 0, json);

    public void Reply(PlayerId player, long requestId, string json) => Send?.Invoke(player, requestId, json);

    /// <summary>A message for the player's offer panel (request id -1): an offer, its end, or a note.</summary>
    public void Panel(PlayerId player, string json) => Send?.Invoke(player, -1, json);

    /// <summary>False when the player sent more than <see cref="RateLimit"/> requests in the last <see cref="RateWindow"/>.</summary>
    public bool Allow(PlayerId player)
    {
        DateTimeOffset now = Clock();
        if (!_recent.TryGetValue(player, out Queue<DateTimeOffset>? times))
        {
            times = new Queue<DateTimeOffset>();
            _recent[player] = times;
        }
        while (times.Count > 0 && now - times.Peek() > RateWindow)
        {
            times.Dequeue();
        }
        if (times.Count >= RateLimit)
        {
            return false;
        }
        times.Enqueue(now);
        return true;
    }
}

/// <summary>
/// Server half of <see cref="MesolRpc"/>: puts each request back together, answers it with <see cref="PageDesk"/>
/// and sends the answer in pieces. Runs on the game thread; a request is a dictionary lookup and, for an edit, a
/// queued file write.
/// </summary>
[RpcHandlersFor(typeof(MesolRpc))]
public sealed partial class PageRpcHandlers(PageDesk desk, PageLink link, Relations.Social social, ILogger logger)
{
    private readonly ChunkAssembler<PlayerId> _assembler = new();

    partial void OnPageReady(RpcContext context)
    {
        if (!link.HasPage(context.Sender))
        {
            logger.LogInformation("MESOL: {Sender}'s card page is ready.", context.Sender);
        }
        link.SetReady(context.Sender);
    }

    partial void OnGesture(RpcContext context, string what)
    {
        if (!link.Allow(context.Sender))
        {
            return;
        }
        social.Gesture(context.Sender, what ?? string.Empty);
    }

    partial void OnAsk(RpcContext context, long requestId, int index, int count, string chunk)
    {
        try
        {
            string? json = _assembler.Add(context.Sender, requestId, index, count, chunk);
            if (json is null)
            {
                return;
            }
            if (!link.Allow(context.Sender))
            {
                logger.LogWarning("MESOL: {Sender} sent too many page requests; #{RequestId} refused.", context.Sender, requestId);
                link.Reply(context.Sender, requestId, PageJson.Serialize(new { ok = false, message = "Slow down a little and try again." }));
                return;
            }
            link.Reply(context.Sender, requestId, desk.Handle(context.Sender, json));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "MESOL: page request #{RequestId} from {Sender} failed.", requestId, context.Sender);
        }
    }

    private void SendWhole(PlayerId player, long requestId, string json)
    {
        try
        {
            List<string> pieces = Chunks.Split(json);
            if (pieces.Count > Chunks.MaxCount)
            {
                logger.LogError("MESOL: an answer to {Player} is too big ({Length} characters); not sent.", player, json.Length);
                return;
            }
            for (int i = 0; i < pieces.Count; i++)
            {
                SendAnswer(player, requestId, i, pieces.Count, pieces[i]);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "MESOL: sending to {Player}'s card page failed.", player);
        }
    }

    internal Action<PlayerId, long, string> Sender => SendWhole;
}

/// <summary>Connects <see cref="PageLink"/> to the generated sends.</summary>
[Service]
public sealed partial class PageLinkWiring(PageLink link, PageRpcHandlers rpc)
{
    private void Start()
    {
        link.Send = rpc.Sender;
    }
}
