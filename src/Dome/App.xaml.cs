using System;
using System.Threading;
using System.Windows;

namespace Dome;

public partial class App : Application
{
    private Mutex? _instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        _instance = new Mutex(true, @"Local\Dome.Island", out var first);
        if (!first) { Shutdown(); return; }
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }
}
