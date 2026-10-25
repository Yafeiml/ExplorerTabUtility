using Shell32;
using SHDocVw;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ExplorerTabUtility.Helpers;
using ExplorerTabUtility.Interop;
using ExplorerTabUtility.Managers;
using ExplorerTabUtility.Models;
using ExplorerTabUtility.WinAPI;
using ExplorerTabUtility.UI.Views;
using ExplorerTabUtility.UI.Localization;

namespace ExplorerTabUtility.Hooks;

using WindowEntry = DualKeyEntry<InternetExplorer, nint?, WindowInfo>;

[Fody.ConfigureAwait(true)]
public class ExplorerWatcher : IHook
{
    private static bool _instanceRunning;
    private static Guid _shellBrowserGuid = typeof(IShellBrowser).GUID;

    private ShellWindows _shellWindows = null!;
    private ShellPathComparer _shellPathComparer = null!;
    private readonly Dispatcher _dispatcher = Application.Current.Dispatcher;
    private nint _mainWindowHandle;
    private readonly DualKeyDictionary<InternetExplorer, nint?, WindowInfo> _windowEntryDict = [];
    private readonly List<WindowRecord> _closedWindows = new();
    private readonly object _windowEntryDictLock = new(), _closedWindowsLock = new(), _processLock = new();
    private readonly object _shellObjectsLock = new();
    private readonly SemaphoreSlim _toOpenWindowsLock = new(1);
    private readonly ProcessWatcher _processWatcher;
    private int _mainExplorerProcessId;
    private Timer? _explorerCheckTimer;
    private bool _shellObjectsInitialized;
    private int _shellGeneration;
    private int _disposed;

    private DShellWindowsEvents_WindowRegisteredEventHandler? _windowRegisteredHandler;

    private string _defaultLocation = null!;
    private bool _reuseTabs = true;
    private bool _isForcingTabs;
    public bool IsHookActive => _isForcingTabs;
    public event Action? OnShellInitialized;

    public ExplorerWatcher()
    {
        if (_instanceRunning)
            throw new InvalidOperationException("Only one instance of ExplorerWatcher is allowed at a time.");
        _instanceRunning = true;

        _processWatcher = new ProcessWatcher("explorer");
        _processWatcher.ProcessTerminated += OnExplorerProcessTerminated;
        StartExplorerProcessCheck();
    }

    public void StartHook()
    {
        if (_isForcingTabs) return;
        _isForcingTabs = true;
    }

    public void StopHook()
    {
        if (!_isForcingTabs) return;
        _isForcingTabs = false;
    }
    public void SetReuseTabs(bool reuseTabs) => _reuseTabs = reuseTabs;

    public void ClearClosedWindows()
    {
        lock (_closedWindowsLock)
            _closedWindows.Clear();
    }

    private void AddClosedWindow(WindowRecord window)
    {
        lock (_closedWindowsLock)
        {
            _closedWindows.Add(window);
            if (_closedWindows.Count > 100)
                _closedWindows.RemoveRange(0, _closedWindows.Count - 100);
        }
    }

    public IReadOnlyCollection<WindowRecord> GetWindows()
    {
        var result = new List<WindowRecord>();
        
        // Add open windows
        InternetExplorer[] windows;
        lock (_windowEntryDictLock)
            windows = _windowEntryDict.Keys.ToArray();
        foreach (var window in windows)
        {
            try { result.Add(new WindowRecord(GetLocation(window), new IntPtr(window.HWND), GetSelectedItems(window), window.LocationName)); }
            catch (COMException) { /* A tab can close while its COM properties are read. */ }
        }
        
        // Add closed windows in reverse order (last closed on top)
        lock (_closedWindowsLock)
            result.AddRange(_closedWindows.AsEnumerable().Reverse());
        
        return result.GroupBy(w => w.Location).Select(g => g.First()).ToList();
    }

    public async Task SwitchTo(string location, nint windowHandle = 0, string[]? selectedItems = null, bool asTab = true, bool duplicate = false)
    {
        var windowToOpen = new WindowRecord(location, windowHandle, selectedItems);
        if (!asTab)
        {
            await OpenNewWindowWithSelection(windowToOpen);
            return;
        }

        await OpenTabNavigateWithSelection(windowToOpen, windowHandle, duplicate, true);
    }
    
    public nint SearchForTab(string targetPath)
    {
        nint targetPidl = 0;
        try
        {
            targetPidl = _shellPathComparer.GetPidlFromPath(targetPath);
            if (targetPidl == 0) return 0;

            foreach (var (window, windowInfo, tabHandle) in ((IEnumerable<WindowEntry>)_windowEntryDict).ToArray())
            {
                // Make sure it is not the newly created window
                if (!Helper.IsTimeUp(windowInfo.CreatedAt, 2_000) || !tabHandle.HasValue || tabHandle.Value == 0)
                    continue;

                var comparePath = windowInfo.Location ?? GetLocation(window);

                if (_shellPathComparer.IsEquivalent(targetPath, comparePath, targetPidl))
                    return tabHandle.Value;
            }

            return 0;
        }
        catch
        {
            return 0;
        }
        finally
        {
            if (targetPidl != 0)
                Marshal.FreeCoTaskMem(targetPidl);
        }
    }
    public async Task SelectTabByHandle(nint windowHandle, nint tabHandle)
    {
        var tabs = Helper.GetAllExplorerTabs(windowHandle).ToArray();
        if (tabs.Length == 0) return;

        var activeTab = tabs[0];
        for (var i = 0; i < tabs.Length; i++)
        {
            if (activeTab == tabHandle) break;

            SelectTabByIndex(windowHandle, i);

            // ReSharper disable once AccessToModifiedClosure
            activeTab = await Helper.DoUntilConditionAsync(
                () => WinApi.FindWindowEx(windowHandle, 0, "ShellTabWindowClass", null),
                h => h != activeTab);
        }
    }
    public void SelectLastTab(nint windowHandle)
    {
        var count = Helper.GetAllExplorerTabs(windowHandle).Count();
        SelectTabByIndex(windowHandle, count - 1);
    }
    public void SelectTabByIndex(nint windowHandle, int index)
    {
        // Send 0xA221 magic command (CTRL + 1...n)
        WinApi.SendMessage(windowHandle, WinApi.WM_COMMAND, 0xA221, index + 1);
    }
    public async Task RequestToOpenNewTab(nint windowHandle, bool bringToFront = false, bool lockToOpenWindows = true)
    {
        if (bringToFront && windowHandle == 0)
            windowHandle = GetMainWindowHWnd(0);

        if (windowHandle == 0)
        {
            await OpenNewWindowWithSelection(new WindowRecord(string.Empty), lockToOpenWindows: lockToOpenWindows);
            return;
        }

        var tabHandle = WinApi.FindWindowEx(windowHandle, 0, "ShellTabWindowClass", null);
        if (tabHandle == 0) return;

        // Send 0xA21B magic command (CTRL + T)
        WinApi.PostMessage(tabHandle, WinApi.WM_COMMAND, 0xA21B, 0);

        if (bringToFront)
            WinApi.RestoreWindowToForeground(windowHandle);
    }
    public async Task Open(string? location, bool asTab, nint windowHandle, int delay = 0)
    {
        if (delay > 0)
            await Task.Delay(delay);

        var normalizedPath = Helper.NormalizeLocation(location ?? string.Empty);
        
        if (normalizedPath.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ||
            System.IO.File.Exists(normalizedPath))
        {
            try
            {
                Helper.BypassWinForegroundRestrictions();
                Process.Start(new ProcessStartInfo(normalizedPath) { UseShellExecute = true });
                return;
            }
            catch
            {
                //
            }
        }
        
        if (!asTab)
        {
            await OpenNewWindowWithSelection(new WindowRecord(normalizedPath));
            return;
        }

        if (string.IsNullOrWhiteSpace(normalizedPath) && !_reuseTabs)
        {
            await RequestToOpenNewTab(windowHandle, bringToFront: true);
            return;
        }

        if (_windowEntryDict.Count > 0)
        {
            OpenNewTab(windowHandle, normalizedPath);
            return;
        }

        await OpenNewWindowWithSelection(new WindowRecord(normalizedPath));
    }
    public void OpenNewTab(nint windowHandle, string location)
    {
        _ = OpenTabNavigateWithSelection(new WindowRecord(location, windowHandle), windowHandle);
    }
    public async Task DuplicateActiveTab(nint windowHandle, bool asTab)
    {
        var activeTabHandle = GetActiveTabHandle(windowHandle);
        if (activeTabHandle == 0) return;

        var window = GetWindowByTabHandle(activeTabHandle);
        if (window == null) return;

        var location = _windowEntryDict[window].Value.Location ?? GetLocation(window);
        var selectedItems = GetSelectedItems(window);
        var windowRecord = new WindowRecord(location, windowHandle, selectedItems);

        if (!asTab)
        {
            await OpenNewWindowWithSelection(windowRecord);
            return;
        }

        await OpenTabNavigateWithSelection(windowRecord, windowHandle, isDuplicate: true);
    }
    public async Task ReopenClosedTab(bool asTab, nint windowHandle = 0)
    {
        WindowRecord? closedWindow;
        lock (_closedWindowsLock)
        {
            closedWindow = _closedWindows.LastOrDefault(w => w.Location != _defaultLocation);
            if (closedWindow == null) return;
            _closedWindows.Remove(closedWindow);
        }

        if (!asTab)
        {
            closedWindow.CreatedAt = Environment.TickCount;
            await OpenNewWindowWithSelection(closedWindow);
            return;
        }

        await OpenTabNavigateWithSelection(closedWindow, windowHandle);
    }
    public async Task DetachCurrentTab(nint windowHandle)
    {
        if (Helper.GetAllExplorerTabs(windowHandle).Take(2).Count() < 2)
            return;

        var activeTabHandle = GetActiveTabHandle(windowHandle);
        if (activeTabHandle == 0) return;

        var window = GetWindowByTabHandle(activeTabHandle);
        if (window == null) return;

        var location = _windowEntryDict[window].Value.Location ?? GetLocation(window);
        var selectedItems = GetSelectedItems(window);
        var windowRecord = new WindowRecord(location, windowHandle, selectedItems);

        // Send 0xA021 magic command (CTRL + W)
        WinApi.SendMessage(activeTabHandle, WinApi.WM_COMMAND, 0xA021, 1);

        await OpenNewWindowWithSelection(windowRecord);
    }
    public void SetTargetWindow(nint windowHandle)
    {
        if (Helper.IsFileExplorerWindow(windowHandle))
            _mainWindowHandle = windowHandle;
    }
    public void NavigateBackForward(nint windowHandle, bool isForward)
    {
        var activeTabHandle = GetActiveTabHandle(windowHandle);
        if (activeTabHandle == 0) return;

        var window = GetWindowByTabHandle(activeTabHandle);
        try
        {
            if (isForward) window?.GoForward();
            else window?.GoBack();
        }
        catch
        {
            // Will throw if there is no further history
        }
    }

    private InternetExplorer? GetRecentlyCreatedWindow(out WindowInfo? windowInfo)
    {
        if (!_shellObjectsInitialized || Volatile.Read(ref _disposed) != 0)
        {
            windowInfo = null;
            return null;
        }
        // When a new window is registered, it's typically the last in the collection
        var count = _shellWindows.Count;
        for (var i = count - 1; i >= 0; i--)
        {
            if (_shellWindows.Item(i) is not InternetExplorer window) continue;

            lock (_windowEntryDictLock)
            {
                if (_windowEntryDict.Keys.Contains(window)) continue;
                try
                {
                    if (GetLocation(window).StartsWith("shell:::{26EE0668-A00A-44D7-9371-BEB064C98683}")) continue;
                }
                catch (COMException) { continue; }

                windowInfo = new WindowInfo { Handle = new IntPtr(window.HWND) };
                _windowEntryDict.Add(window, windowInfo);

                if (_windowEntryDict.Count == 1)
                {
                    _mainWindowHandle = windowInfo.Handle;

                    if (SettingsManager.RestorePreviousWindows && _closedWindows.Any(w => w.Restore))
                        _ = RestorePreviousWindows();
                }
                
                return window;
            }
        }

        windowInfo = null;
        return null;
    }
    private async void OnShellWindowRegistered(int __)
    {
        if (!_shellObjectsInitialized || Volatile.Read(ref _disposed) != 0) return;
        if (!_dispatcher.CheckAccess())
        {
            if (!_dispatcher.HasShutdownStarted)
                _ = _dispatcher.BeginInvoke(new Action(() => OnShellWindowRegistered(__)));
            return;
        }
        var generation = _shellGeneration;

        var showAgain = true;
        nint hWnd = 0;
        InternetExplorer? registeredWindow = null;
        WindowInfo? registeredInfo = null;
        try
        {
            var shouldOpenAsWindow = Helper.IsCtrlShiftDown();

            WindowInfo windowInfo = null!;
            var window = await Helper.DoUntilNotDefaultAsync(
                () => generation == _shellGeneration ? GetRecentlyCreatedWindow(out windowInfo!) : null, 2_500, 70);
            if (window == null || generation != _shellGeneration || !_windowEntryDict.ContainsKey(window)) return;
            registeredWindow = window;
            registeredInfo = windowInfo;

            _ = GetTabHandle(window);

            hWnd = new IntPtr(window.HWND);
            windowInfo.Handle = hWnd;
            
            if (shouldOpenAsWindow)
            {
                HookWindowEvents(window, windowInfo);
                return;
            }
            
            var location = GetLocation(window);

            //Control Panel
            if (location.StartsWith("shell:::{26EE0668-A00A-44D7-9371-BEB064C98683}"))
            {
                RemoveWindowAndUnhookEvents(window, windowInfo);
                return;
            }

            // Check if this is a single tab window and there are other windows
            var shouldReopenAsTab = (_isForcingTabs || _reuseTabs) &&
                                    _windowEntryDict.Count > 1 &&
                                    hWnd != _mainWindowHandle &&
                                    Helper.GetAllExplorerTabs(hWnd).Take(2).Count() == 1;

            if (shouldReopenAsTab)
                Helper.HideWindow(hWnd);

            // Check if it is a detached tab
            var isRecentlyClosed = TryGetRecentlyClosedWindow(location, out var closedWindow);
            if (isRecentlyClosed)
                SelectItems(window, closedWindow!.SelectedItems);

            shouldReopenAsTab = shouldReopenAsTab && !isRecentlyClosed;

            if (shouldReopenAsTab)
            {
                var targetWindow = GetMainWindowHWnd(hWnd);
                if (!await OpenTabNavigateWithSelection(new WindowRecord(location, hWnd, GetSelectedItems(window)), targetWindow, excludedWindow: hWnd))
                {
                    if (generation == _shellGeneration && _windowEntryDict.ContainsKey(window))
                        HookWindowEvents(window, windowInfo);
                    return;
                }

                if (generation == _shellGeneration && _windowEntryDict.ContainsKey(window))
                {
                    window.Quit();
                    showAgain = false;
                    RemoveWindowAndUnhookEvents(window, windowInfo);
                }
                return;
            }

            // OnQuit might fire after ShellWindowRegistered in case of reattached tab (and there were selected files)
            if (!isRecentlyClosed)
            {
                isRecentlyClosed = await Helper.DoUntilNotDefaultAsync(
                    () => generation == _shellGeneration && TryGetRecentlyClosedWindow(location, out closedWindow), 700, 50);
                if (isRecentlyClosed && generation == _shellGeneration && _windowEntryDict.ContainsKey(window))
                    SelectItems(window, closedWindow!.SelectedItems);
            }

            if (generation == _shellGeneration && _windowEntryDict.ContainsKey(window))
                HookWindowEvents(window, windowInfo);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Shell window registration failed: {ex}");
            if (registeredWindow != null && registeredInfo != null && generation == _shellGeneration &&
                _windowEntryDict.ContainsKey(registeredWindow))
                HookWindowEvents(registeredWindow, registeredInfo);
        }
        finally
        {
            if (showAgain && hWnd != 0 && generation == _shellGeneration)
            {
                Helper.ShowWindow(hWnd, removeCache: true);
            }
        }
    }
    private void HookWindowEvents(InternetExplorer window, WindowInfo windowInfo)
    {
        // Create strongly-typed handlers so we can remove them later
        windowInfo.OnQuitHandler = () =>
        {
            if (!_shellObjectsInitialized || Volatile.Read(ref _disposed) != 0 || !_windowEntryDict.ContainsKey(window)) return;
            try
            {
                var location = windowInfo.Location ?? GetLocation(window);
                var locationName = windowInfo.Name ?? window.LocationName;
                var windowRecord = new WindowRecord(location, new IntPtr(window.HWND), name: locationName);
                AddClosedWindow(windowRecord);

                // Home, This PC, etc
                if (location != _defaultLocation)
                    windowRecord.SelectedItems = GetSelectedItems(window);
            }
            catch (Exception ex) { Debug.WriteLine($"Closed shell window could not be read: {ex}"); }
            finally { RemoveWindowAndUnhookEvents(window, windowInfo); }
        };

        if (SettingsManager.RestorePreviousWindows)
            windowInfo.OnNavigateHandler = (object _, ref object _) =>
            {
                if (!_shellObjectsInitialized || Volatile.Read(ref _disposed) != 0 || !_windowEntryDict.ContainsKey(window)) return;
                try
                {
                    windowInfo.Location = GetLocation(window);
                    windowInfo.Name = window.LocationName;
                }
                catch (Exception ex) { Debug.WriteLine($"Shell navigation could not be read: {ex}"); }
            };

        try
        {
            // Subscribe
            window.OnQuit += windowInfo.OnQuitHandler;
            if (SettingsManager.RestorePreviousWindows)
            {
                windowInfo.Location = GetLocation(window);
                windowInfo.Name = window.LocationName;
                window.NavigateComplete2 += windowInfo.OnNavigateHandler;
            }

            // Make sure the window is still alive (User might have closed it immediately after opening it)
            _ = window.HWND;
        }
        catch
        {
            RemoveWindowAndUnhookEvents(window, windowInfo);
        }
    }
    private void RemoveWindowAndUnhookEvents(InternetExplorer window, WindowInfo windowInfo)
    {
        lock (_windowEntryDictLock)
        {
            if (!_windowEntryDict.Remove(window)) return;
            if (windowInfo.Handle != 0 && !_windowEntryDict.Values.Any(info => info.Handle == windowInfo.Handle))
            {
                Helper.ShowWindow(windowInfo.Handle, removeCache: true);
            }
        }

        if (windowInfo.OnQuitHandler != null)
            SafeRun(() => window.OnQuit -= windowInfo.OnQuitHandler);
        if (windowInfo.OnNavigateHandler != null)
            SafeRun(() => window.NavigateComplete2 -= windowInfo.OnNavigateHandler);

        // Async callbacks may still hold this shared RCW; let the runtime release it.
    }

    private async Task RestorePreviousWindows()
    {
        var result = await RunInStaThread(() => CustomMessageBox.Show(
            LocalizationManager.GetString("RestorePreviousPrompt"),
            LocalizationManager.GetString("AppTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question));

        WindowRecord[] records;
        lock (_closedWindowsLock)
            records = _closedWindows.Where(record => record.Restore).ToArray();
        foreach (var record in records)
        {
            record.Restore = false;
            
            if (result != MessageBoxResult.Yes) continue;
            
            _ = OpenTabNavigateWithSelection(record);
        }
    }
    private async Task OpenNewWindowWithSelection(WindowRecord windowToOpen, bool duplicate = true, bool lockToOpenWindows = true)
    {
        if (lockToOpenWindows)
            await _toOpenWindowsLock.WaitAsync();

        try
        {
            AddClosedWindow(windowToOpen);

            var hasSelection = windowToOpen.SelectedItems?.Length > 0;

            nint[]? currentWindows = null;
            if (hasSelection)
                currentWindows = Helper.GetAllExplorerWindows().ToArray();

            Helper.BypassWinForegroundRestrictions();

            var location = string.IsNullOrWhiteSpace(windowToOpen.Location) ? _defaultLocation : windowToOpen.Location;
            await RunInStaThread(() =>
            {
                Shell? shell = null;
                try
                {
                    shell = new Shell();
                    shell.ShellExecute(location, "", "", duplicate ? "opennewwindow" : "open");
                }
                finally
                {
                    if (shell != null)
                        Marshal.ReleaseComObject(shell);
                }
            });

            if (!hasSelection) return;

            var newWindowHandle = await Helper.ListenForNewExplorerWindowAsync(currentWindows ?? []);
            if (newWindowHandle == 0) return;

            var window = _windowEntryDict.Keys.FirstOrDefault(w => w.HWND == newWindowHandle);
            if (window == null) return;

            SelectItems(window, windowToOpen.SelectedItems);
        }
        finally
        {
            if (lockToOpenWindows)
                _toOpenWindowsLock.Release();
        }
    }
    private async Task<bool> OpenTabNavigateWithSelection(WindowRecord windowToOpen, nint windowHandle = 0, bool isDuplicate = false, bool forceTabReuse = false, nint excludedWindow = 0)
    {
        await _toOpenWindowsLock.WaitAsync();
        try
        {
            if (!_shellObjectsInitialized || Volatile.Read(ref _disposed) != 0) return false;
            var generation = _shellGeneration;

            if ((_reuseTabs || forceTabReuse) && !isDuplicate && _windowEntryDict.Count > 0)
            {
                var existingTab = SearchForTab(windowToOpen.Location);
                if (existingTab != 0 && WinApi.GetParent(existingTab) != excludedWindow)
                {
                    windowHandle = WinApi.GetParent(existingTab);
                    await SelectTabByHandle(windowHandle, existingTab);
                    if (generation != _shellGeneration || !Helper.IsFileExplorerWindow(windowHandle) ||
                        GetWindowByTabHandle(existingTab) == null) return false;
                    WinApi.RestoreWindowToForeground(windowHandle);
                    return true;
                }
            }

            // Get the main window
            var mainWindowHWnd = Helper.IsFileExplorerWindow(windowHandle)
                ? windowHandle
                : GetMainWindowHWnd(windowToOpen.Handle);
            if (excludedWindow != 0 && mainWindowHWnd == excludedWindow) return false;

            if (mainWindowHWnd == 0)
            {
                await OpenNewWindowWithSelection(windowToOpen, lockToOpenWindows: false);
                return true;
            }

            // Store the current tabs
            var currentTabs = Helper.GetAllExplorerTabs(mainWindowHWnd).ToArray();

            // Request to open a new tab
            await RequestToOpenNewTab(mainWindowHWnd, lockToOpenWindows: false);

            // Wait for the new tab
            var newTabHandle = await Helper.ListenForNewExplorerTabAsync(mainWindowHWnd, currentTabs, 2_000);
            if (newTabHandle == 0 || generation != _shellGeneration || !_shellObjectsInitialized) return false;

            // Get the window object
            var window = await Helper.DoUntilNotDefaultAsync(() => GetWindowByTabHandle(newTabHandle), 2_000, 50);
            if (window == null || generation != _shellGeneration || !_windowEntryDict.ContainsKey(window)) return false;

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            DWebBrowserEvents2_NavigateComplete2EventHandler navigateHandler = null!;
            navigateHandler = (object _, ref object _) =>
            {
                if (generation != _shellGeneration || !_windowEntryDict.ContainsKey(window)) return;
                try
                {
                    if (!string.IsNullOrWhiteSpace(windowToOpen.Location) &&
                        !_shellPathComparer.IsEquivalent(windowToOpen.Location, GetLocation(window))) return;
                    SelectItems(window, windowToOpen.SelectedItems);
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Shell selection failed: {ex}");
                    tcs.TrySetResult(false);
                }
            };

            window.NavigateComplete2 += navigateHandler;
            try
            {
                await Navigate(window, windowToOpen.Location);
                WinApi.RestoreWindowToForeground(mainWindowHWnd);
                await Task.WhenAny(tcs.Task, Task.Delay(5000));
                return tcs.Task.IsCompleted && await tcs.Task && generation == _shellGeneration &&
                    _windowEntryDict.ContainsKey(window) && Helper.IsFileExplorerWindow(mainWindowHWnd);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Shell tab navigation failed: {ex}");
                return false;
            }
            finally { SafeRun(() => window.NavigateComplete2 -= navigateHandler); }
        }
        finally
        {
            _toOpenWindowsLock.Release();
        }
    }
    private bool TryGetRecentlyClosedWindow(string location, out WindowRecord? closedWindow, int maxAge = 2_000)
    {
        nint targetPidl = 0;
        try
        {
            targetPidl = _shellPathComparer.GetPidlFromPath(location);
            lock (_closedWindowsLock)
            {
                for (var i = _closedWindows.Count - 1; i >= 0; i--)
                {
                    var record = _closedWindows[i];
                    if (Environment.TickCount - record.CreatedAt > maxAge) break;
                    if (!_shellPathComparer.IsEquivalent(location, record.Location, targetPidl)) continue;
                    _closedWindows.RemoveAt(i);
                    closedWindow = record;
                    return true;
                }
            }
            closedWindow = null;
            return false;
        }
        finally
        {
            if (targetPidl != 0)
                Marshal.FreeCoTaskMem(targetPidl);
        }
    }
    private nint GetMainWindowHWnd(nint otherThan)
    {
        if (_mainWindowHandle != otherThan && Helper.IsFileExplorerWindow(_mainWindowHandle))
            return _mainWindowHandle;

        var allWindows = WinApi.FindAllWindowsEx("CabinetWClass");

        // Get another handle other than the newly created one. (In case if it is still alive.)
        _mainWindowHandle = allWindows
            .Where(h => h != otherThan)
            .Reverse() // To get the last one in the z-index (the oldest)
            .OrderByDescending(h => WinApi.FindAllWindowsEx("ShellTabWindowClass", h).Count()) // The one with the most tabs first
            .FirstOrDefault();

        if (_mainWindowHandle != 0) return _mainWindowHandle;

        return Helper.IsFileExplorerWindow(otherThan) ? otherThan : 0;
    }
    private Task<nint> GetTabHandle(InternetExplorer window)
    {
        if (_windowEntryDict.TryGetValue(window, out WindowEntry entry) && entry.OptionalKey is { } handle and > 0)
            return Task.FromResult(handle);

        // ReSharper disable once SuspiciousTypeConversion.Global
        if (window is not Interop.IServiceProvider sp) return Task.FromResult<nint>(0);

        var hr = sp.QueryService(ref _shellBrowserGuid, ref _shellBrowserGuid, out var shellBrowser);
        if (hr < 0 || shellBrowser == null) return Task.FromResult<nint>(0);

        try
        {
            hr = shellBrowser.GetWindow(out var hWnd);

            if (hr >= 0 && hWnd != 0 && _windowEntryDict.ContainsKey(window))
                _windowEntryDict.UpdateOptionalKey(window, hWnd);

            return Task.FromResult(hr >= 0 ? hWnd : 0);
        }
        finally
        {
            if (!ReferenceEquals(shellBrowser, window))
                SafeReleaseComObject(shellBrowser);
        }
    }
    private static nint GetActiveTabHandle(nint windowHandle)
    {
        // Active tab always at the top of the z-index
        return WinApi.FindWindowEx(windowHandle, 0, "ShellTabWindowClass", null);
    }
    private InternetExplorer? GetWindowByTabHandle(nint tabHandle)
    {
        if (tabHandle == 0) return null;
        return _windowEntryDict.TryGetValue(tabHandle, out InternetExplorer? foundWindow) ? foundWindow : null;
    }
    private static string[]? GetSelectedItems(InternetExplorer window)
    {
        object? document = null;
        FolderItems? selectedItems = null;
        try
        {
            document = window.Document;
            if (document is not ShellFolderView view) return null;
            selectedItems = view.SelectedItems();
            var count = selectedItems.Count;
            if (count == 0) return null;

            var result = new string[count];
            for (var i = 0; i < count; i++)
            {
                var item = selectedItems.Item(i);
                try { result[i] = item.Name; }
                finally { SafeReleaseComObject(item); }
            }
            return result;
        }
        finally
        {
            SafeReleaseComObject(selectedItems);
            SafeReleaseComObject(document);
        }
    }
    private static void SelectItems(InternetExplorer window, string[]? names)
    {
        if (names == null || names.Length == 0) return;

        object? document = null;
        Folder? folder = null;
        try
        {
            document = window.Document;
            if (document is not ShellFolderView view) return;
            folder = view.Folder;
            foreach (var name in names)
            {
                object item = folder.ParseName(name);
                if (item == null) continue;
                try { view.SelectItem(ref item, 1); }
                finally { SafeReleaseComObject(item); }
            }
        }
        finally
        {
            SafeReleaseComObject(folder);
            SafeReleaseComObject(document);
        }
    }
    private static string GetLocation(InternetExplorer window)
    {
        var path = window.LocationURL;
        if (!string.IsNullOrWhiteSpace(path)) return Helper.NormalizeLocation(path);

        // Recycle Bin, This PC, etc
        object? document = null;
        Folder? folder = null;
        FolderItem? item = null;
        try
        {
            document = window.Document;
            if (document is not ShellFolderView view) return string.Empty;
            folder = view.Folder;
            if (folder is not Folder2 folder2) return string.Empty;
            item = folder2.Self;
            return Helper.NormalizeLocation(item.Path);
        }
        finally
        {
            SafeReleaseComObject(item);
            SafeReleaseComObject(folder);
            SafeReleaseComObject(document);
        }
    }
    private async Task Navigate(InternetExplorer window, string path)
    {
        if (!path.Contains("#") && !path.Contains("%23"))
        {
            window.Navigate2(path);
            return;
        }

        var folder = await RunInStaThread(() =>
        {
            Shell? shell = null;
            Folder? folder;
            try
            {
                shell = new Shell();
                folder = shell.NameSpace(path);
            }
            finally
            {
                if (shell != null)
                    Marshal.ReleaseComObject(shell);
            }
            return folder;
        });

        try
        {
            window.Navigate2(folder);
        }
        finally
        {
            if (folder != null)
                Marshal.ReleaseComObject(folder);
        }
    }
    private Task RunInStaThread(Action action)
    {
        return _dispatcher.InvokeAsync(action).Task;
    }
    private Task<T?> RunInStaThread<T>(Func<T?> action)
    {
        return _dispatcher.InvokeAsync(action).Task;
    }
    
    private void StartExplorerProcessCheck() => _explorerCheckTimer = new Timer(CheckForMainExplorer, null, 0, 1000);
    private void CheckForMainExplorer(object? state)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (!_dispatcher.CheckAccess())
        {
            if (!_dispatcher.HasShutdownStarted)
                _ = _dispatcher.BeginInvoke(new Action(() => CheckForMainExplorer(state)));
            return;
        }

        using var process = Helper.GetMainExplorerProcess();
        if (process == null) return;
        
        lock (_processLock)
        {
            if (_mainExplorerProcessId != 0) return;
            if (Volatile.Read(ref _disposed) != 0) return;
            
            _mainExplorerProcessId = process.Id;
            if (InitializeShellObjects())
            {
                _explorerCheckTimer?.Dispose();
                _explorerCheckTimer = null;
                OnShellInitialized?.Invoke();
            }
            else
                _mainExplorerProcessId = 0;
        }
    }
    private void OnExplorerProcessTerminated(object? s, ProcessEventArgs e)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (!_dispatcher.CheckAccess())
        {
            if (!_dispatcher.HasShutdownStarted)
                _ = _dispatcher.BeginInvoke(new Action(() => OnExplorerProcessTerminated(s, e)));
            return;
        }

        // Main explorer.exe process (_shellWindows must be restarted)
        lock (_processLock)
        {
            if (Volatile.Read(ref _disposed) != 0) return;

            if (e.ProcessId == _mainExplorerProcessId)
            {
                _mainExplorerProcessId = 0;
                DisposeShellObjects();
                StartExplorerProcessCheck();
                return;
            }
        }
        
        // Other explorer.exe processes
        lock (_windowEntryDictLock)
        {
            if (_windowEntryDict.Count == 0) return;
            var crashCount = 0;
            foreach (var (window, info) in ((IEnumerable<WindowEntry>)_windowEntryDict).ToArray().AsEnumerable().Reverse())
            {
                if (!_windowEntryDict.ContainsKey(window)) continue;
                try
                {
                    _ = window.HWND;
                }
                catch
                {
                    if (info.OnNavigateHandler != null)
                    {
                        crashCount++;
                        AddClosedWindow(new WindowRecord(info.Location!, name: info.Name!));
                    }
                    
                    RemoveWindowAndUnhookEvents(window, info);
                }
            }
            if (!SettingsManager.RestorePreviousWindows || _windowEntryDict.Count > 0) return;
            lock (_closedWindowsLock)
            {
                for (var i = 1; i <= Math.Min(crashCount, _closedWindows.Count); i++)
                    _closedWindows[_closedWindows.Count - i].Restore = true;
            }
        }
    }

    private bool InitializeShellObjects()
    {
        lock (_shellObjectsLock)
        {
            if (Volatile.Read(ref _disposed) != 0 || _shellObjectsInitialized)
                return false;

            _shellObjectsInitialized = true;
            _shellGeneration++;
            try
            {
                _shellPathComparer = new ShellPathComparer();
                _shellWindows = new ShellWindows();
                _defaultLocation = Helper.GetDefaultExplorerLocation(_shellPathComparer);

                if (SettingsManager.ClosedWindows != null)
                    foreach (var window in SettingsManager.ClosedWindows)
                        AddClosedWindow(window);

                // Shell callbacks share the WPF STA message loop.
                _windowRegisteredHandler = OnShellWindowRegistered;
                _shellWindows.WindowRegistered += _windowRegisteredHandler;

                var hasOpen = false;
                var count = _shellWindows.Count;
                for (var i = 0; i < count; i++)
                {
                    if (_shellWindows.Item(i) is not InternetExplorer window)
                        continue;
                    hasOpen = true;

                    var windowInfo = new WindowInfo { Handle = new IntPtr(window.HWND) };
                    _windowEntryDict.Add(window, windowInfo);
                    _ = GetTabHandle(window);
                    HookWindowEvents(window, windowInfo);
                }

                if (hasOpen)
                    lock (_closedWindowsLock)
                        foreach (var window in _closedWindows) window.Restore = false;

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Shell initialization failed; retrying: {ex}");
                DisposeShellObjects(persistWindows: false);
                return false;
            }
        }
    }
    private void DisposeShellObjects(bool persistWindows = true)
    {
        ShellWindows shellWindows;
        ShellPathComparer shellPathComparer;
        DShellWindowsEvents_WindowRegisteredEventHandler? windowRegisteredHandler;

        lock (_shellObjectsLock)
        {
            if (!_shellObjectsInitialized) return;

            _shellObjectsInitialized = false;
            _shellGeneration++;
            shellWindows = _shellWindows;
            shellPathComparer = _shellPathComparer;
            windowRegisteredHandler = _windowRegisteredHandler;

            _windowRegisteredHandler = null;
            _shellWindows = null!;
            _shellPathComparer = null!;
        }

        if (persistWindows)
            SafeRun(PersistWindows);

        // Unhook global event
        if (windowRegisteredHandler != null)
            SafeRun(() => shellWindows.WindowRegistered -= windowRegisteredHandler);


        // Unsubscribe from each InternetExplorer instance's events
        WindowEntry[] windowEntries;
        lock (_windowEntryDictLock)
        {
            windowEntries = ((IEnumerable<WindowEntry>)_windowEntryDict).ToArray();
            _windowEntryDict.Clear();
        }

        foreach (var (window, windowInfo) in windowEntries)
        {
            // Unsubscribe
            if (windowInfo.OnQuitHandler != null)
                SafeRun(() => window.OnQuit -= windowInfo.OnQuitHandler);

            if (windowInfo.OnNavigateHandler != null)
                SafeRun(() => window.NavigateComplete2 -= windowInfo.OnNavigateHandler);

        }

        // Release the ShellWindows COM object
        SafeReleaseComObject(shellWindows);

        SafeDispose(shellPathComparer);
        foreach (var hWnd in Helper.HiddenWindows.Keys)
            SafeRun(() => Helper.ShowWindow(hWnd, removeCache: true));
        _mainWindowHandle = 0;
    }

    private void PersistWindows()
    {
        var store = new List<WindowRecord>();
        lock (_closedWindowsLock)
        {
            if (SettingsManager.SaveClosedHistory) store.AddRange(_closedWindows);
            _closedWindows.Clear();
        }

        // Save currently open windows (explorer crash / system restart, logoff / AppExit)
        if (SettingsManager.RestorePreviousWindows)
            lock (_windowEntryDictLock)
            {
                store.AddRange(_windowEntryDict.Values
                    .Where(w => w.OnNavigateHandler != null)
                    .Select(w => new WindowRecord(w.Location!, name: w.Name!, restore: true)));
            }
        
        // DistinctBy location
        var distinctItems = store
            .GroupBy(w => w.Location)
            .Select(g => g.Last())
            .ToArray();
        
        // TakeLast 100
        SettingsManager.ClosedWindows = distinctItems.Skip(Math.Max(0, distinctItems.Length - 100)).ToArray();
    }

    public void Dispose()
    {
        if (!_dispatcher.CheckAccess() && !_dispatcher.HasShutdownStarted)
        {
            _dispatcher.Invoke(Dispose);
            return;
        }
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _explorerCheckTimer?.Dispose();
        _explorerCheckTimer = null;
        DisposeShellObjects();
        _instanceRunning = false;
        _processWatcher.Dispose();
        GC.SuppressFinalize(this);
    }

    private static void SafeRun(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Ignore shutdown-time cleanup failures.
        }
    }

    private static void SafeDispose(IDisposable? disposable)
    {
        if (disposable == null) return;

        SafeRun(disposable.Dispose);
    }

    private static void SafeReleaseComObject(object? comObject)
    {
        if (comObject == null) return;

        SafeRun(() =>
        {
            if (Marshal.IsComObject(comObject))
                Marshal.ReleaseComObject(comObject);
        });
    }
}
