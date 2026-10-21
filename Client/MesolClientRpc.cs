using System;
using MESOL.Common;
using Microsoft.Extensions.Logging;
using ReadyM.SDK.Attributes;

namespace MESOL.Client;

/// <summary>
/// Client half of <see cref="MesolRpc"/>: puts the server's pieces back together and hands each whole answer to
/// <see cref="LorePage"/>. <c>SendAsk</c> and <c>SendPageReady</c> (generated) go to the server.
/// </summary>
[RpcHandlersFor(typeof(MesolRpc))]
public sealed partial class MesolClientRpc(ILogger logger)
{
    private readonly ChunkAssembler<int> _assembler = new();

    /// <summary>A whole answer: request id (0 = the server's own message) and its JSON.</summary>
    public event Action<long, string>? Answered;

    partial void OnAnswer(long requestId, int index, int count, string chunk)
    {
        try
        {
            if (_assembler.Add(0, requestId, index, count, chunk) is string json)
            {
                Answered?.Invoke(requestId, json);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "[MESOL] Handling an answer from the server failed.");
        }
    }
}
