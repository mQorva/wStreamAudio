using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using wStreamAudio.Core.Abstractions;
using wStreamAudio.Services;

namespace wStreamAudio.Tray;

/// <summary>
/// Natives Win32-Shell_NotifyIcon-Tray — gleiche Mechanik wie Magic-Voice, weil der WinUI-
/// Hosted-Flyout über H.NotifyIcon zu viele Erstrender-Probleme hatte. Links-Doppelklick
/// öffnet die Einstellungen, Rechtsklick zeigt das klassische Win32-Kontextmenü mit
/// Stream-Toggle, Audio-Quelle/Brücken-Submenus, Mini-Fenster, Einstellungen, Über und
/// Beenden.
/// </summary>
public sealed class TrayIconController : IDisposable
{
    private const int TrayId = 1;
    private const int TrayCallbackMessage = 0x8000 + 42;
    private const int WmCommand = 0x0111;
    private const int WmDestroy = 0x0002;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmRButtonUp = 0x0205;

    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NimSetVersion = 0x00000004;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NotifyIconVersion4 = 4;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const uint LrDefaultSize = 0x00000040;
    private const int IdiApplication = 32512;

    private const int MfString = 0x00000000;
    private const int MfSeparator = 0x00000800;
    private const int MfGrayed = 0x00000001;
    private const int MfPopup = 0x00000010;
    private const int MfChecked = 0x00000008;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;

    private const int CmdStreamToggle = 1001;
    private const int CmdMiniFenster = 1002;
    private const int CmdSettings = 1003;
    private const int CmdAbout = 1004;
    private const int CmdExit = 1005;
    private const int CmdProfileBase = 2000;

    private readonly IAppProfile _profile;
    private readonly ISettingsService _settings;
    private readonly StreamPipelineCoordinator _pipeline;
    private readonly ILogger<TrayIconController> _log;
    private readonly WndProc _wndProc;
    private readonly IntPtr _hwnd;
    private readonly int _taskbarCreatedMessage;
    private IntPtr _icon;
    private bool _ownsIcon;
    private bool _disposed;

    private readonly List<int> _profileCommandIds = new();

    public TrayIconController(
        IAppProfile profile,
        ISettingsService settings,
        StreamPipelineCoordinator pipeline,
        ILogger<TrayIconController> log)
    {
        _profile = profile;
        _settings = settings;
        _pipeline = pipeline;
        _log = log;
        _taskbarCreatedMessage = (int)RegisterWindowMessage("TaskbarCreated");
        _wndProc = WindowProc;
        _hwnd = CreateMessageWindow(_wndProc);
        if (_hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("Tray-Fenster konnte nicht erstellt werden.");
        }

        AddOrUpdateIcon(NimAdd);
        _pipeline.StreamingChanged += (_, _) => AddOrUpdateIcon(NimModify);
    }

    private void AddOrUpdateIcon(uint message)
    {
        var newIcon = LoadIconForState(_pipeline.IsStreaming, out var ownsNewIcon);
        var data = new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _hwnd,
            uID = TrayId,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = newIcon,
            szTip = TrimTooltip(_pipeline.IsStreaming
                ? $"{_profile.AppName} — Stream aktiv"
                : $"{_profile.AppName} — bereit"),
            uVersionOrTimeout = NotifyIconVersion4,
        };

        if (!Shell_NotifyIcon(message, ref data))
        {
            _log.LogWarning("Shell_NotifyIcon({Message}) fehlgeschlagen: {Error}", message, Marshal.GetLastWin32Error());
            DestroyIconIfOwned(newIcon, ownsNewIcon);
            return;
        }

        if (message == NimAdd && !Shell_NotifyIcon(NimSetVersion, ref data))
        {
            _log.LogWarning("Shell_NotifyIcon(NIM_SETVERSION) fehlgeschlagen: {Error}", Marshal.GetLastWin32Error());
        }

        DestroyIconIfOwned(_icon, _ownsIcon);
        _icon = newIcon;
        _ownsIcon = ownsNewIcon;
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == TrayCallbackMessage)
        {
            // Klassisches Magic-Voice-Pattern: lParam ist die Maus-Message direkt.
            var mouseMessage = lParam.ToInt32();
            if (mouseMessage == WmLButtonDblClk)
            {
                // Doppelklick ins Tray: Mini-Fenster ein-/ausblenden (Toggle).
                var app = App.Instance;
                if (app is not null)
                {
                    if (app.IsQuickPopupVisible) app.HideQuickPopup();
                    else _ = app.ShowQuickPopupAsync();
                }
                return IntPtr.Zero;
            }
            if (mouseMessage == WmRButtonUp)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }

        if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
        {
            AddOrUpdateIcon(NimAdd);
            _log.LogInformation("Tray-Icon nach Explorer-Neustart erneut registriert");
            return IntPtr.Zero;
        }

        if (msg == WmCommand)
        {
            HandleCommand(wParam.ToInt32() & 0xFFFF);
            return IntPtr.Zero;
        }

        if (msg == WmDestroy) return IntPtr.Zero;

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        _profileCommandIds.Clear();

        var menu = CreatePopupMenu();
        AppendMenu(menu, MfString, CmdStreamToggle,
            _pipeline.IsStreaming ? "anhalten" : "abspielen");

        AppendMenu(menu, MfSeparator, 0, string.Empty);

        // Audio-Quelle als Submenu — aktive Quelle mit Häkchen.
        var profileMenu = CreatePopupMenu();
        var settings = _settings.Current;
        for (int i = 0; i < settings.CaptureProfiles.Count; i++)
        {
            var cmd = CmdProfileBase + i;
            _profileCommandIds.Add(cmd);
            var p = settings.CaptureProfiles[i];
            var flags = MfString | (p.Id == settings.ActiveCaptureProfileId ? MfChecked : 0);
            AppendMenu(profileMenu, flags, cmd, p.Name);
        }
        if (settings.CaptureProfiles.Count == 0)
        {
            AppendMenu(profileMenu, MfString | MfGrayed, 0, "(keine Quellen)");
        }
        AppendMenu(menu, MfPopup, profileMenu.ToInt32(), "Audio-Quelle");

        AppendMenu(menu, MfSeparator, 0, string.Empty);

        var miniChecked = App.Instance?.IsQuickPopupVisible == true ? MfChecked : 0;
        AppendMenu(menu, MfString | miniChecked, CmdMiniFenster, "Mini-Fenster");
        AppendMenu(menu, MfString, CmdSettings, "Einstellungen…");
        AppendMenu(menu, MfString, CmdAbout, "Über…");

        AppendMenu(menu, MfSeparator, 0, string.Empty);
        AppendMenu(menu, MfString, CmdExit, $"{_profile.AppName} beenden");

        GetCursorPos(out var point);
        SetForegroundWindow(_hwnd);
        var command = TrackPopupMenu(menu, TpmRightButton | TpmReturnCmd, point.X, point.Y, 0, _hwnd, IntPtr.Zero);
        DestroyMenu(menu);
        if (command != 0) HandleCommand(command);
    }

    private void HandleCommand(int command)
    {
        try
        {
            switch (command)
            {
                case CmdStreamToggle:
                    _ = ToggleStreamAsync();
                    break;
                case CmdMiniFenster:
                    var app = App.Instance;
                    if (app is not null)
                    {
                        if (app.IsQuickPopupVisible) app.HideQuickPopup();
                        else _ = app.ShowQuickPopupAsync();
                    }
                    break;
                case CmdSettings:
                    App.Instance?.ShowSettingsWindow();
                    break;
                case CmdAbout:
                    App.Instance?.ShowAboutWindow();
                    break;
                case CmdExit:
                    _ = ExitAsync();
                    break;
                default:
                    if (_profileCommandIds.Contains(command))
                    {
                        var idx = command - CmdProfileBase;
                        var settings = _settings.Current;
                        if (idx >= 0 && idx < settings.CaptureProfiles.Count)
                        {
                            settings.ActiveCaptureProfileId = settings.CaptureProfiles[idx].Id;
                            _settings.NotifyChanged();
                        }
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Tray-Command {Cmd} fehlgeschlagen", command);
        }
    }

    private async Task ToggleStreamAsync()
    {
        if (_pipeline.IsStreaming) await _pipeline.StopAsync().ConfigureAwait(false);
        else await _pipeline.StartAsync().ConfigureAwait(false);
    }

    private static IntPtr LoadIconForState(bool streaming, out bool ownsIcon)
    {
        var fileName = streaming ? "TrayActive.ico" : "TrayIdle.ico";
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        if (File.Exists(path))
        {
            // Magic-Voice-Pattern: 0,0,LR_LOADFROMFILE|LR_DEFAULTSIZE — Shell pickt selbst die passende Größe.
            var icon = LoadImage(IntPtr.Zero, path, ImageIcon, 0, 0, LrLoadFromFile | LrDefaultSize);
            if (icon != IntPtr.Zero) { ownsIcon = true; return icon; }
        }
        ownsIcon = false;
        return LoadIcon(IntPtr.Zero, new IntPtr(IdiApplication));
    }

    private static string TrimTooltip(string tooltip) => tooltip.Length > 127 ? tooltip[..127] : tooltip;

    private static void DestroyIconIfOwned(IntPtr icon, bool ownsIcon)
    {
        if (icon != IntPtr.Zero && ownsIcon) DestroyIcon(icon);
    }

    private static async Task ExitAsync()
    {
        if (Microsoft.UI.Xaml.Application.Current is App app)
        {
            await app.ShutdownAsync().ConfigureAwait(false);
        }
        Microsoft.UI.Xaml.Application.Current.Exit();
    }

    private static IntPtr CreateMessageWindow(WndProc wndProc)
    {
        var className = "wStreamAudioTray_" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        var moduleHandle = GetModuleHandle(null);
        var windowClass = new WindowClass
        {
            cbSize = (uint)Marshal.SizeOf<WindowClass>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc),
            hInstance = moduleHandle,
            lpszClassName = className
        };
        if (RegisterClassEx(ref windowClass) == 0) return IntPtr.Zero;
        return CreateWindowEx(0, className, className, 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, moduleHandle, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var data = new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _hwnd,
            uID = TrayId
        };
        Shell_NotifyIcon(NimDelete, ref data);
        DestroyIconIfOwned(_icon, _ownsIcon);
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
    }

    private delegate IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NotifyIconData lpData);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string lpString);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);
    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass lpWndClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, int uFlags, int uIDNewItem, string lpNewItem);
    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point lpPoint);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);
}
