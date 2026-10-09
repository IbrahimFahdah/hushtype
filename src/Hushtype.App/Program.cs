using Velopack;
using Hushtype.Platform;

namespace Hushtype.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build()
            .OnAfterUpdateFastCallback(_ => StartupRegistration.RefreshIfEnabled(Environment.ProcessPath!))
            .OnBeforeUninstallFastCallback(_ => StartupRegistration.Set(false, Environment.ProcessPath!, null))
            .Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
