using Microsoft.Extensions.Logging;
using ReadyM.SDK.Attributes;

namespace MESOL.Server;

// The server half's entry point. Services find themselves; this only logs where the mod was loaded from.
// Services ask for this class to read ModDirectory (set before Init runs).
[ModEntry]
public sealed partial class Mod(ILogger logger)
{
    private void Init()
    {
        logger.LogInformation("MESOL: server half loaded from {Folder}.", ModDirectory);
    }
}
