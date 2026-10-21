using ReadyM.Api.Multiplayer;
using ReadyM.SDK.Attributes;

namespace MESOL.Common;

/// <summary>
/// The card page talks to the server through these. A request or answer is one JSON text, cut into pieces of at most
/// <see cref="Chunks.Size"/> characters so each message stays inside one network packet (about 1300 bytes); the
/// other side puts the pieces back together (<see cref="ChunkAssembler"/>). Request id 0 in an answer is the server's
/// own message for the card page: /mylore opening the editor, or /lore showing another player's card. Request id -1 is
/// for the offer panel: an introduction or friendship offer, its end, or a short note.
/// No [ClientToClients]: ServerRpcManifest.ClientOffset is never assigned in OB:MP 1.0 (API.md, Delta rc.2).
/// </summary>
[RpcContracts]
public static partial class MesolRpc
{
    /// <summary>One piece of a request from the card page (JSON with an "op").</summary>
    [ClientToServer]
    public static partial void Ask(long requestId, int index, int count, string chunk);

    /// <summary>One piece of the server's answer to a request, or (request id 0) of a message of its own.</summary>
    [ServerToClient]
    public static partial void Answer(long requestId, int index, int count, string chunk);

    /// <summary>The card page has loaded, so /mylore and /lore can open it instead of printing to chat.</summary>
    [ClientToServer]
    public static partial void PageReady();

    /// <summary>A key in the game: "introduce" (=), "decline" (-) or "look" (Shift+End).</summary>
    [ClientToServer]
    public static partial void Gesture(string what);
}
