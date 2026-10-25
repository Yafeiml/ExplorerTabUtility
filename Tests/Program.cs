using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using ExplorerTabUtility.Helpers;
using ExplorerTabUtility.Hooks;
using ExplorerTabUtility.Models;
using ExplorerTabUtility.WinAPI;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        Exception? failure = null;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        timeout.Tick += (_, _) =>
        {
            failure = new TimeoutException("Regression checks did not finish.");
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        };
        timeout.Start();
        dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await PollingKeepsStaContext();
                await TrayRegressionChecks.RunAsync();
                WindowHidingPreservesComposition();
                OptionalKeysAreRemoved();
                ClosedHistoryIsBoundedAndDisposedCallbacksAreIgnored();
                ProcessDisposalSuppressesQueuedEvents();
                Console.WriteLine("PASS: STA polling, window restoration (1000 cycles), optional keys, bounded history and disposed callbacks.");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                timeout.Stop();
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
            }
        }));
        Dispatcher.Run();
        if (failure == null) return 0;
        Console.Error.WriteLine(failure);
        return 1;
    }

    private static async Task PollingKeepsStaContext()
    {
        var originalThread = Environment.CurrentManagedThreadId;
        void CheckContext()
        {
            Assert(Environment.CurrentManagedThreadId == originalThread, "Polling moved off the Dispatcher thread.");
            Assert(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA, "Polling moved off STA.");
            Assert(SynchronizationContext.Current is DispatcherSynchronizationContext, "Polling lost its Dispatcher context.");
        }

        var attempts = 0;
        var result = await Helper.DoUntilConditionAsync(() =>
        {
            CheckContext();
            return ++attempts;
        }, value => value >= 3, timeMs: 2_000, sleepMs: 10);
        Assert(result == 3 && attempts == 3, "Synchronous callback polling did not retry correctly.");

        attempts = 0;
        await Helper.DoUntilConditionAsync(async () =>
        {
            CheckContext();
            await Task.Delay(5);
            CheckContext();
            attempts++;
        }, () => attempts >= 3, timeMs: 2_000, sleepMs: 10);
        Assert(attempts == 3, "Async callback polling did not retry correctly.");

        attempts = 0;
        result = await Helper.DoUntilConditionAsync(async () =>
        {
            CheckContext();
            await Task.Delay(5);
            CheckContext();
            return ++attempts;
        }, value => value >= 3, timeMs: 2_000, sleepMs: 10);
        Assert(result == 3 && attempts == 3, "Async result polling did not retry correctly.");
    }

    private static void WindowHidingPreservesComposition()
    {
        var module = GetModuleHandle(null);
        var windowClass = new WindowClass { Instance = module, ClassName = "CabinetWClass", WindowProc = TestWindowProc };
        if (RegisterClass(ref windowClass) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        nint window = 0;
        nint otherWindow = 0;
        nint unrelated = 0;
        var baselineCount = Helper.HiddenWindows.Count;
        try
        {
            // Only test-owned tool windows are shown, far outside the desktop and without activation.
            const uint extendedStyle = 0x00000080 | 0x08000000 | 0x00000200;
            window = CreateWindowEx(extendedStyle, "CabinetWClass", "ETU regression", 0x80000000,
                -20_000, -20_000, 80, 50, 0, 0, module, 0);
            if (window == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            Assert(WinApi.GetWindowRect(window, out var original), "Cannot inspect the test window rectangle.");
            var originalStyle = WinApi.GetWindowLong(window, WinApi.GWL_EXSTYLE);

            for (var iteration = 0; iteration < 1_000; iteration++)
            {
                Helper.HideWindow(window);
                Helper.HideWindow(window);
                Assert(Helper.HiddenWindows.Count == baselineCount + 1, "Hidden window state accumulated.");
                Assert(Helper.HiddenWindows[window] is { } saved && saved.Equals(original), "Hiding twice replaced the original position.");
                Assert(WinApi.GetWindowLong(window, WinApi.GWL_EXSTYLE) == originalStyle, "Hiding changed Explorer composition styles.");
                Assert(Helper.ShowWindow(window, removeCache: false), "Failed to restore the test window.");
                Assert(Helper.HiddenWindows.ContainsKey(window), "Restore discarded a retained snapshot.");
                Assert(Helper.ShowWindow(window, removeCache: true), "Failed to restore and clear the test window.");
                Assert(WinApi.GetWindowRect(window, out var restored) && restored.Equals(original), "Window position was not restored.");
                Assert(WinApi.GetWindowLong(window, WinApi.GWL_EXSTYLE) == originalStyle, "Restoring changed Explorer composition styles.");
                Assert(Helper.HiddenWindows.Count == baselineCount, "Restored window state leaked.");
            }

            otherWindow = CreateWindowEx(extendedStyle, "CabinetWClass", "ETU alternate target", 0x80000000,
                -20_000, -20_000, 80, 50, 0, 0, module, 0);
            if (otherWindow == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            var watcher = (ExplorerWatcher)RuntimeHelpers.GetUninitializedObject(typeof(ExplorerWatcher));
            var getTarget = typeof(ExplorerWatcher).GetMethod("GetMainWindowHWnd", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (var cached in new[] { (nint)0, (nint)(-1), window })
            {
                SetField(watcher, "_mainWindowHandle", cached);
                var target = (nint)getTarget.Invoke(watcher, new object[] { window })!;
                Assert(target != window && Helper.IsFileExplorerWindow(target), "Missing or stale target selected the source window.");
            }

            unrelated = CreateWindowEx(extendedStyle, "STATIC", null, 0x80000000, -20_000, -20_000, 80, 50, 0, 0, module, 0);
            if (unrelated == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            foreach (var invalid in new[] { (nint)0, (nint)(-1), unrelated })
            {
                Helper.HideWindow(invalid);
                Assert(!Helper.ShowWindow(invalid, removeCache: true), "An unrelated or invalid window was restored.");
                Assert(!Helper.HiddenWindows.ContainsKey(invalid), "An unrelated or invalid window was cached.");
            }

            Helper.HideWindow(window);
            var destroyed = window;
            Assert(DestroyWindow(window), "Cannot destroy the test window.");
            window = 0;
            Assert(!Helper.ShowWindow(destroyed, removeCache: true), "A destroyed window was reported as restored.");
            Assert(Helper.HiddenWindows.Count == baselineCount, "Destroyed window state leaked.");
        }
        finally
        {
            if (window != 0)
            {
                Helper.HiddenWindows.TryRemove(window, out _);
                DestroyWindow(window);
            }
            if (unrelated != 0) DestroyWindow(unrelated);
            if (otherWindow != 0) DestroyWindow(otherWindow);
            UnregisterClass("CabinetWClass", module);
            GC.KeepAlive(TestWindowProc);
        }
    }

    private static void OptionalKeysAreRemoved()
    {
        var windows = new DualKeyDictionary<string, nint?, string>();
        windows.Add("first", "A", (nint)1);
        windows.Add("second", "B", (nint)2);
        windows.Add("pending", "C");
        Assert(windows.RemoveByPrimary("first") && !windows.ContainsOptional((nint)1), "Primary removal retained its optional key.");
        Assert(windows.RemoveByOptional((nint)2) && !windows.ContainsPrimary("second"), "Optional removal retained its primary key.");
        Assert(windows.RemoveByPrimary("pending") && windows.Count == 0 && windows.OptionalKeys.Count == 0, "Removing an unassigned window leaked state.");
    }

    private static void ClosedHistoryIsBoundedAndDisposedCallbacksAreIgnored()
    {
        // Bypass the constructor so these checks cannot install hooks or touch the running Explorer.
        var watcher = (ExplorerWatcher)RuntimeHelpers.GetUninitializedObject(typeof(ExplorerWatcher));
        var history = new List<WindowRecord>();
        SetField(watcher, "_closedWindows", history);
        SetField(watcher, "_closedWindowsLock", new object());
        var addHistory = typeof(ExplorerWatcher).GetMethod("AddClosedWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var index = 0; index < 1_000; index++)
            addHistory.Invoke(watcher, new object[] { new WindowRecord(index.ToString()) });
        Assert(history.Count == 100 && history[0].Location == "900" && history[^1].Location == "999", "Closed history is not bounded to the latest 100 records.");

        SetField(watcher, "_disposed", 1);
        SetField(watcher, "_shellObjectsInitialized", true);
        SetField(watcher, "_isForcingTabs", true);
        Invoke(watcher, "OnShellWindowRegistered", 0);
        Invoke(watcher, "CheckForMainExplorer", (object?)null);
        Invoke(watcher, "OnExplorerProcessTerminated", null, new ProcessEventArgs(0, "explorer", 0));
        Assert(history.Count == 100, "A callback changed disposed watcher state.");
    }

    private static void ProcessDisposalSuppressesQueuedEvents()
    {
        var context = new RecordingContext();
        var watcher = (ProcessWatcher)RuntimeHelpers.GetUninitializedObject(typeof(ProcessWatcher));
        var process = Process.GetCurrentProcess();
        var tracked = new ConcurrentDictionary<int, (Process Process, int SessionId)>();
        tracked[process.Id] = (process, process.SessionId);
        SetField(watcher, "_trackedProcesses", tracked);
        SetField(watcher, "_scanLock", new object());
        SetField(watcher, "_syncContext", context);
        SetField(watcher, "_scanTimer", new Timer(_ => { }, null, Timeout.Infinite, Timeout.Infinite));
        SetField(watcher, "_isMonitoring", true);
        var invoked = false;
        EventHandler<ProcessEventArgs> handler = (_, _) => invoked = true;
        typeof(ProcessWatcher).GetMethod("RaiseEvent", BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(ProcessEventArgs))
            .Invoke(watcher, new object[] { handler, new ProcessEventArgs(process.Id, process.ProcessName, process.SessionId) });
        Assert(context.Callbacks.Count == 1, "The process event was not queued.");
        watcher.Dispose();
        foreach (var (callback, state) in context.Callbacks) callback(state);
        Invoke(watcher, "ScanForProcesses", (object?)null);
        Assert(!invoked && tracked.Count == 0, "A disposed process watcher delivered an event or retained a process.");
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        public List<(SendOrPostCallback Callback, object? State)> Callbacks { get; } = new();
        public override void Post(SendOrPostCallback callback, object? state) => Callbacks.Add((callback, state));
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static void Invoke(object target, string name, params object?[] arguments) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments);

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static readonly WindowProcedure TestWindowProc = DefWindowProc;
    private delegate nint WindowProcedure(nint window, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Style;
        public WindowProcedure WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? moduleName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string className, nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint extendedStyle, string className, string? title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);
}
