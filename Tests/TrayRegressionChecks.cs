using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ExplorerTabUtility.UI.Views.Controls;

internal static class TrayRegressionChecks
{
    public static async Task RunAsync()
    {
        Assert(SynchronizationContext.Current is DispatcherSynchronizationContext,
            "Tray checks require the UI Dispatcher context.");
        await KeepsDispatcherResponsiveAndStopsWhenShownAsync();
        await StopsAfterDisposalAsync();
        Console.WriteLine("PASS: tray recovery keeps the Dispatcher responsive and stops when shown or disposed.");
    }

    private static async Task KeepsDispatcherResponsiveAndStopsWhenShownAsync()
    {
        // No constructor, native message sink, or Shell_NotifyIcon call is needed.
        var owner = CreateOwner();
        var icon = new Border { Visibility = Visibility.Collapsed };
        var repeated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var changes = 0;
        var descriptor = DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(Border))!;
        EventHandler changed = (_, _) =>
        {
            Assert(icon.Dispatcher.CheckAccess(), "Tray recovery moved off the UI Dispatcher.");
            if (++changes == 4) repeated.TrySetResult(true);
        };
        descriptor.AddValueChanged(icon, changed);
        var elapsed = Stopwatch.StartNew();
        var recovery = StartRecovery(owner, icon);
        try
        {
            var callbackRan = false;
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => callbackRan = true);
            Assert(callbackRan && elapsed.Elapsed < TimeSpan.FromSeconds(1),
                "Tray recovery blocked the UI Dispatcher.");
            Assert(!recovery.IsCompleted, "Hidden tray recovery did not wait asynchronously.");

            await repeated.Task.WaitAsync(TimeSpan.FromSeconds(2));
            icon.Visibility = Visibility.Visible;
            await recovery.WaitAsync(TimeSpan.FromSeconds(2));
            Assert(icon.Visibility == Visibility.Visible, "Tray recovery hid an icon that was shown again.");
        }
        finally
        {
            MarkDisposed(owner);
            descriptor.RemoveValueChanged(icon, changed);
            await recovery.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private static async Task StopsAfterDisposalAsync()
    {
        var owner = CreateOwner();
        var icon = new Border { Visibility = Visibility.Collapsed };
        var changes = 0;
        var descriptor = DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(Border))!;
        EventHandler changed = (_, _) =>
        {
            Assert(icon.Dispatcher.CheckAccess(), "Tray recovery moved off the UI Dispatcher.");
            changes++;
        };
        descriptor.AddValueChanged(icon, changed);
        var recovery = StartRecovery(owner, icon);
        try
        {
            Assert(changes == 2, "Tray recovery did not perform its initial visibility reset.");
            MarkDisposed(owner);
            var changesAtDisposal = changes;
            await recovery.WaitAsync(TimeSpan.FromSeconds(2));
            Assert(changes == changesAtDisposal, "Tray recovery changed visibility after disposal.");
        }
        finally
        {
            MarkDisposed(owner);
            descriptor.RemoveValueChanged(icon, changed);
            await recovery.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private static SystemTrayIcon CreateOwner() =>
        (SystemTrayIcon)RuntimeHelpers.GetUninitializedObject(typeof(SystemTrayIcon));

    private static Task StartRecovery(SystemTrayIcon owner, UIElement icon) =>
        (Task)typeof(SystemTrayIcon).GetMethod("KeepTrayIconHiddenAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(owner, new object[] { icon })!;

    private static void MarkDisposed(SystemTrayIcon owner) =>
        typeof(SystemTrayIcon).GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, true);

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
