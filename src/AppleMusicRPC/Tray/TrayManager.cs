using System.Runtime.InteropServices;
using AppleMusicRPC.Models;
using AppleMusicRPC.Services;

namespace AppleMusicRPC.Tray;

/// <summary>
/// Icône dans la zone de notification via Win32 pur — aucune dépendance WinForms.
/// </summary>
public sealed class TrayManager : IDisposable
{
    // ── Win32 constantes ────────────────────────────────────────────────────────
    private const int WM_USER         = 0x0400;
    private const int TRAY_MSG        = WM_USER + 1;
    private const int WM_DESTROY      = 0x0002;
    private const int WM_COMMAND      = 0x0111;
    private const int NIM_ADD         = 0x00;
    private const int NIM_MODIFY      = 0x01;
    private const int NIM_DELETE      = 0x02;
    private const int NIF_MESSAGE     = 0x01;
    private const int NIF_ICON        = 0x02;
    private const int NIF_TIP         = 0x04;
    private const uint NIF_INFO       = 0x10;
    private const uint NIIF_NOSOUND   = 0x00000010;
    private const int WM_LBUTTONUP    = 0x0202;
    private const int WM_RBUTTONUP    = 0x0205;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_BOTTOMALIGN = 0x0020;
    private const uint MF_STRING      = 0x00;
    private const uint MF_SEPARATOR   = 0x800;
    private const uint MF_DISABLED    = 0x02;
    private const uint MF_GRAYED      = 0x01;
    private const int CS_HREDRAW      = 0x0002;
    private const int CS_VREDRAW      = 0x0001;

    // Menu IDs
    private const int ID_TOGGLE    = 1001;
    private const int ID_CONFIG    = 1002;
    private const int ID_UPDATE    = 1003;
    private const int ID_UNINSTALL = 1004;
    private const int ID_QUIT      = 1005;

    // ── Win32 structures ─────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int    cbSize;
        public nint   hWnd;
        public int    uID;
        public uint   uFlags;
        public int    uCallbackMessage;
        public nint   hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint   dwState;
        public uint   dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint   uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint   dwInfoFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public int    cbSize;
        public int    style;
        public nint   lpfnWndProc;
        public int    cbClsExtra;
        public int    cbWndExtra;
        public nint   hInstance;
        public nint   hIcon;
        public nint   hCursor;
        public nint   hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string  lpszClassName;
        public nint   hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint   hwnd;
        public uint   message;
        public nuint  wParam;
        public nint   lParam;
        public uint   time;
        public int    ptX, ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x, y; }

    // ── P/Invoke ─────────────────────────────────────────────────────────────
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint exStyle, string cls, string title,
        uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProc(nint hWnd, uint msg, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern void DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetMessage(ref MSG lpMsg, nint hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int code);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(nint hMenu, uint flags, nuint id, string? text);

    [DllImport("user32.dll")]
    private static extern bool TrackPopupMenu(nint hMenu, uint flags, int x, int y,
        int reserved, nint hWnd, nint pRect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(ref POINT pt);

    [DllImport("kernel32.dll")]
    private static extern nint GetModuleHandle(string? name);

    [DllImport("user32.dll")]
    private static extern nint LoadIcon(nint hInst, nint name);

    // ── État ─────────────────────────────────────────────────────────────────
    private nint   _hwnd;
    private nint   _hIcon;
    private bool   _rpcEnabled = true;
    private string _tooltip    = "Apple Music RPC";

    private readonly ConfigService _config;
    private readonly GCHandle      _procHandle;  // empêche le GC de collecter le delegate

    public event Action? ToggleRpcRequested;
    public event Action? CheckUpdateRequested;
    public event Action? UninstallRequested;
    public event Action? QuitRequested;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    public TrayManager(ConfigService config)
    {
        _config = config;
    }

    /// <summary>Démarre la boucle de messages Windows (bloquant — appeler sur le thread principal).</summary>
    public void Run()
    {
        var hInst = GetModuleHandle(null);
        _hIcon    = LoadIconFromResource(hInst);

        // Enregistrement de la classe de fenêtre
        WndProcDelegate proc = WndProc;
        _procHandle.IsAllocated  // juste pour référencer _procHandle après init
            .ToString();          // dummy read — voir affectation ci-dessous

        var cls = new WNDCLASSEX
        {
            cbSize      = Marshal.SizeOf<WNDCLASSEX>(),
            style       = CS_HREDRAW | CS_VREDRAW,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
            hInstance   = hInst,
            lpszClassName = "AppleMusicRPC_Tray"
        };
        RegisterClassEx(ref cls);

        _hwnd = CreateWindowEx(0, "AppleMusicRPC_Tray", "Apple Music RPC",
            0, 0, 0, 0, 0, -3 /* HWND_MESSAGE */, nint.Zero, hInst, nint.Zero);

        // Évite que le GC collecte le delegate pendant la boucle de messages
        var pinned = GCHandle.Alloc(proc);

        AddTrayIcon();

        ShowBalloon("Apple Music RPC", "En cours d'exécution dans la barre des tâches.");

        var msg = new MSG();
        while (GetMessage(ref msg, nint.Zero, 0, 0))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        pinned.Free();
    }

    // ── Procédure de fenêtre ──────────────────────────────────────────────────
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate nint WndProcDelegate(nint hWnd, uint msg, nuint wParam, nint lParam);

    private nint WndProc(nint hWnd, uint msg, nuint wParam, nint lParam)
    {
        if (msg == WM_DESTROY)
        {
            RemoveTrayIcon();
            PostQuitMessage(0);
            return 0;
        }

        if (msg == (uint)TRAY_MSG)
        {
            var loword = (int)(lParam & 0xFFFF);
            if (loword == WM_RBUTTONUP || loword == WM_LBUTTONUP)
                ShowContextMenu(hWnd);
            return 0;
        }

        if (msg == WM_COMMAND)
        {
            switch ((int)(wParam & 0xFFFF))
            {
                case ID_TOGGLE:
                    _rpcEnabled = !_rpcEnabled;
                    ToggleRpcRequested?.Invoke();
                    break;
                case ID_CONFIG:
                    _config.OpenInEditor();
                    break;
                case ID_UPDATE:
                    CheckUpdateRequested?.Invoke();
                    break;
                case ID_UNINSTALL:
                    UninstallRequested?.Invoke();
                    break;
                case ID_QUIT:
                    QuitRequested?.Invoke();
                    DestroyWindow(hWnd);
                    break;
            }
            return 0;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    // ── Tray icon ─────────────────────────────────────────────────────────────
    private void AddTrayIcon()
    {
        var nid = MakeNid();
        Shell_NotifyIcon(NIM_ADD, ref nid);
    }

    private void RemoveTrayIcon()
    {
        var nid = MakeNid();
        Shell_NotifyIcon(NIM_DELETE, ref nid);
    }

    public void SetTrack(MediaInfo? info)
    {
        _tooltip = info is { Status: PlaybackStatus.Playing or PlaybackStatus.Paused }
            ? $"{info.Title} — {info.Artist}"
            : "Apple Music RPC";

        if (_tooltip.Length > 127) _tooltip = _tooltip[..124] + "...";

        if (_hwnd == nint.Zero) return;
        var nid = MakeNid();
        Shell_NotifyIcon(NIM_MODIFY, ref nid);
    }

    public void ShowBalloon(string title, string message)
    {
        if (_hwnd == nint.Zero) return;
        var nid = MakeNid();
        nid.uFlags      |= NIF_INFO;
        nid.szInfoTitle  = title;
        nid.szInfo       = message;
        nid.dwInfoFlags  = NIIF_NOSOUND;
        Shell_NotifyIcon(NIM_MODIFY, ref nid);
    }

    private NOTIFYICONDATA MakeNid() => new()
    {
        cbSize          = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd            = _hwnd,
        uID             = 1,
        uFlags          = NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage = TRAY_MSG,
        hIcon           = _hIcon,
        szTip           = _tooltip,
        szInfo          = "",
        szInfoTitle     = ""
    };

    private void ShowContextMenu(nint hWnd)
    {
        var hMenu = CreatePopupMenu();
        AppendMenu(hMenu, MF_STRING | MF_DISABLED | MF_GRAYED, 0, "Apple Music RPC");
        AppendMenu(hMenu, MF_SEPARATOR, 0, null);
        AppendMenu(hMenu, MF_STRING, ID_TOGGLE,
            _rpcEnabled ? "✓  RPC activé" : "    RPC désactivé");
        AppendMenu(hMenu, MF_SEPARATOR, 0, null);
        AppendMenu(hMenu, MF_STRING, ID_CONFIG,    "Ouvrir la configuration");
        AppendMenu(hMenu, MF_STRING, ID_UPDATE,    "Vérifier les mises à jour");
        AppendMenu(hMenu, MF_SEPARATOR, 0, null);
        AppendMenu(hMenu, MF_STRING, ID_UNINSTALL, "Désinstaller");
        AppendMenu(hMenu, MF_STRING, ID_QUIT,      "Quitter");

        var pt = new POINT();
        GetCursorPos(ref pt);
        SetForegroundWindow(hWnd);
        TrackPopupMenu(hMenu, TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, pt.x, pt.y, 0, hWnd, nint.Zero);
        DestroyMenu(hMenu);
    }

    // ── Icône ─────────────────────────────────────────────────────────────────
    private static nint LoadIconFromResource(nint hInst)
    {
        // Charge l'icône depuis les ressources embarquées
        try
        {
            using var stream = typeof(TrayManager).Assembly.GetManifestResourceStream("appicon.ico");
            if (stream is null) return LoadIcon(nint.Zero, (nint)32512); // IDI_APPLICATION

            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return CreateIconFromBytes(bytes);
        }
        catch
        {
            return LoadIcon(nint.Zero, (nint)32512);
        }
    }

    [DllImport("user32.dll")]
    private static extern nint CreateIconFromResourceEx(byte[] pbIconBits, uint cbIconBits,
        bool fIcon, uint dwVersion, int cxDesired, int cyDesired, uint flags);

    private static nint CreateIconFromBytes(byte[] data)
    {
        // Format ICO : skip le header (6 bytes) + directory entry (16 bytes) pour lire l'offset
        if (data.Length < 22) return nint.Zero;
        int offset = BitConverter.ToInt32(data, 18);
        int size   = BitConverter.ToInt32(data, 14);
        if (offset + size > data.Length) return nint.Zero;

        var iconData = data.AsSpan(offset, size).ToArray();
        return CreateIconFromResourceEx(iconData, (uint)iconData.Length, true, 0x00030000, 0, 0, 0);
    }

    public void Dispose()
    {
        RemoveTrayIcon();
        if (_procHandle.IsAllocated) _procHandle.Free();
    }
}
