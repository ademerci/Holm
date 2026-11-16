using System;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>The WPF side of the Handy bridge: its Dispatcher and a DispatcherTimer for the check. The rest is in HandyBridge.cs.</summary>
internal sealed partial class HandyBridge
{
    public HandyBridge(Dispatcher dispatcher)
        : this(action => dispatcher.InvokeAsync(action), dispatcher.Invoke, dispatcher.CheckAccess, update => Ticker(dispatcher, update)) { }

    static Action<bool> Ticker(Dispatcher dispatcher, Action update)
    {
        dispatcher.VerifyAccess();
        var timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => update(), dispatcher);
        timer.Stop();
        return on => { if (on) timer.Start(); else timer.Stop(); };
    }
}
