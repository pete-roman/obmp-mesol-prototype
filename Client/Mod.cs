using ReadyM.Api.DI;
using ReadyM.Cef.Rpc;
using ReadyM.SDK.Attributes;

namespace MESOL.Client;

// The client half's entry point. The client mod loader only loads a mod DLL with a [ModEntry] class,
// and the [Service] classes next to it are found and created on their own.
[ModEntry]
public sealed partial class Mod(IDependencyContainer services)
{
    private void Init()
    {
        // Required in 1.0 for [SendToCef]/[ReceiveFromCef]: without it the page and the game never hear each other,
        // and nothing fails at build time (docs, Web UI).
        services.RegisterSingleton<ICefRpcHandler>(services.Resolve<LorePage>());
    }
}
