using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace TVBrowser;

sealed class MainForm : Form
{
    private readonly AppConfig _cfg = AppConfig.Load();
    private readonly WebView2 _web = new()
    {
        Dock = DockStyle.Fill,
        DefaultBackgroundColor = Color.FromArgb(7, 8, 12)
    };
    private readonly Uri _home;
    private bool _closing;
    private bool _internalNav;          // true while we show our own "no connection" page
    private int _crashes;
    private DateTime _crashWindowStart = DateTime.MinValue;

    public MainForm()
    {
        // a typo in homeUrl shouldn't stop the app from starting
        _home = Uri.TryCreate(_cfg.HomeUrl, UriKind.Absolute, out var h) && (h.Scheme == Uri.UriSchemeHttps || h.Scheme == Uri.UriSchemeHttp)
            ? h
            : new Uri(new AppConfig().HomeUrl);

        Text = "TV Browser";
        BackColor = Color.FromArgb(7, 8, 12);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = Screen.PrimaryScreen!.Bounds;   // covers the whole screen, taskbar included
        TopMost = true;
        Controls.Add(_web);
        FormClosing += (_, _) => _closing = true;
        Shown += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            // logins and cookies are kept here so you only sign in once
            var dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TVBrowser");
            var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
            var env = await CoreWebView2Environment.CreateAsync(null, dataDir, options);
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            TopMost = false;
            MessageBox.Show(this,
                "TV Browser couldn't start its web engine.\n\n" + ex.Message +
                "\n\nInstall the Microsoft Edge WebView2 Runtime, then try again.",
                "TV Browser");
            Close();
            return;
        }

        var core = _web.CoreWebView2;

        // lock the browser down
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsPinchZoomEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;

        // only the launcher and the allowed streaming sites can be opened
        core.NavigationStarting += (_, e) =>
        {
            if (_internalNav && e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;   // our own "no connection" page
            if (IsAllowed(e.Uri)) return;

            e.Cancel = true;
            // if the link was clicked on the launcher, tell it so it can explain instead of hanging
            if (IsOnLauncher() && Uri.TryCreate(e.Uri, UriKind.Absolute, out var blocked) && blocked.Host.Length > 0)
                core.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "blocked", host = blocked.Host }));
        };

        core.NavigationCompleted += (_, e) =>
        {
            _internalNav = false;
            if (e.IsSuccess)
            {
                if (IsOnLauncher()) SendConfigToLauncher();
                return;
            }
            if (e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return;   // we blocked it on purpose
            ShowOfflinePage();
        };

        // links that want a new window open in this same window instead
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (IsAllowed(e.Uri)) core.Navigate(e.Uri);
        };

        // never offer to open other apps (spotify:, mailto:, ms-windows-store: ...)
        core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;

        core.DownloadStarting += (_, e) => e.Cancel = true;

        core.PermissionRequested += (_, e) =>
        {
            e.State = e.PermissionKind switch
            {
                CoreWebView2PermissionKind.Camera or
                CoreWebView2PermissionKind.Microphone or
                CoreWebView2PermissionKind.Geolocation or
                CoreWebView2PermissionKind.Notifications => CoreWebView2PermissionState.Deny,
                _ => CoreWebView2PermissionState.Default
            };
        };

        core.ProcessFailed += (_, e) =>
        {
            if (_closing) return;
            switch (e.ProcessFailedKind)
            {
                case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                    RestartApp();            // the whole web engine is gone, so start over
                    break;
                case CoreWebView2ProcessFailedKind.RenderProcessExited:
                    OnRenderCrash();
                    break;
            }
        };

        // keys that work on every site, even during fullscreen video
        Log("Started");
        InstallKeyHook();
        RegisterHomeHotkey();

        // the Home button shown in the top left of every page
        await core.AddScriptToExecuteOnDocumentCreatedAsync(
            HomeButtonScript
                .Replace("__HOME__", JsonSerializer.Serialize(_home.ToString()))
                .Replace("__BLOCKFS__", _cfg.BlockFullscreen ? "true" : "false"));

        if (AppConfig.LoadError is { } configError)
        {
            TopMost = false;
            MessageBox.Show(this, configError, "TV Browser");
            TopMost = true;
        }

        core.Navigate(_home.ToString());
    }

    // ---- crash recovery ----
    private void OnRenderCrash()
    {
        // reload the page, but if it keeps crashing fall back to the launcher
        if (DateTime.UtcNow - _crashWindowStart > TimeSpan.FromMinutes(1))
        {
            _crashWindowStart = DateTime.UtcNow;
            _crashes = 0;
        }
        if (++_crashes > 3) GoHome();
        else _web.CoreWebView2.Reload();
    }

    private void RestartApp()
    {
        try
        {
            if (Environment.ProcessPath is { } exe)
                Process.Start(new ProcessStartInfo(exe, "--restart") { UseShellExecute = false });
        }
        catch { /* if it can't restart, closing is still better than a dead black window */ }
        Close();
    }

    // ---- no connection ----
    private void ShowOfflinePage()
    {
        _internalNav = true;
        var home = JsonSerializer.Serialize(_home.ToString());
        _web.CoreWebView2.NavigateToString($$"""
        <!doctype html><meta charset="utf-8"><title>No connection</title>
        <body style="margin:0;height:100vh;display:grid;place-items:center;background:#07080c;color:#f4f6fb;font:600 2rem system-ui,sans-serif;text-align:center">
        <div>No connection<div style="margin-top:.8rem;font-size:1.1rem;font-weight:400;color:#9aa3b5">Trying again in a few seconds…</div></div>
        <script>setTimeout(() => { location.href = {{home}}; }, 5000);</script>
        """);
    }

    // ---- talking to the launcher page ----
    private bool IsOnLauncher() =>
        _web.CoreWebView2.Source.StartsWith(_home.ToString(), StringComparison.OrdinalIgnoreCase);

    // the launcher uses this list to refuse sites this TV would block anyway
    private void SendConfigToLauncher()
    {
        var allowed = _cfg.AllowedDomains
            .Select(d => d.Trim().TrimStart('.').ToLowerInvariant())
            .Where(d => d.Length > 0)
            .Append(_home.Host.ToLowerInvariant())
            .ToArray();
        _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "config", allowed }));
    }

    // ---- small log so problems can be diagnosed: %LOCALAPPDATA%\TVBrowser\log.txt ----
    private static void Log(string message)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TVBrowser");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "log.txt");
            if (File.Exists(file) && new FileInfo(file).Length > 100_000) File.Delete(file);
            File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
        }
        catch { /* logging must never break the app */ }
    }

    // ---- Ctrl+H, second path: a Windows hotkey that is only active while this window is in front ----
    private const int HOTKEY_HOME = 1, WM_HOTKEY = 0x0312;
    private const uint MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
    private bool _hotkeyOn;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private void RegisterHomeHotkey()
    {
        if (_hotkeyOn || !IsHandleCreated) return;
        _hotkeyOn = RegisterHotKey(Handle, HOTKEY_HOME, MOD_CONTROL | MOD_NOREPEAT, (uint)Keys.H);
        if (!_hotkeyOn) Log("Ctrl+H hotkey could not be registered, error " + Marshal.GetLastWin32Error());
    }

    private void UnregisterHomeHotkey()
    {
        if (!_hotkeyOn) return;
        UnregisterHotKey(Handle, HOTKEY_HOME);
        _hotkeyOn = false;
    }

    protected override void OnActivated(EventArgs e) { base.OnActivated(e); RegisterHomeHotkey(); }
    protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); UnregisterHomeHotkey(); }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam == (IntPtr)HOTKEY_HOME)
        {
            Log("Ctrl+H received (hotkey)");
            GoHome();
            return;
        }
        base.WndProc(ref m);
    }

    // ---- Ctrl+H, first path: a keyboard hook that also handles Alt+Home and the quit keys ----
    private const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private LowLevelKeyboardProc? _hookProc;   // kept in a field so it isn't garbage collected
    private IntPtr _hook = IntPtr.Zero;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private void InstallKeyHook()
    {
        _hookProc = HookCallback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
        Log(_hook == IntPtr.Zero
            ? "Keyboard hook FAILED, error " + Marshal.GetLastWin32Error() + " (Ctrl+H still works through the hotkey path)"
            : "Keyboard hook installed");
    }

    // true when any window of this app is the one in front (the web view's own windows count)
    private static bool OurWindowIsInFront()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        GetWindowThreadProcessId(fg, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN) &&
            OurWindowIsInFront())
        {
            var key = (Keys)Marshal.ReadInt32(lParam);          // first field of KBDLLHOOKSTRUCT is the key code
            bool alt = (GetAsyncKeyState((int)Keys.Menu) & 0x8000) != 0;
            bool ctrl = (GetAsyncKeyState((int)Keys.ControlKey) & 0x8000) != 0;

            if ((key == Keys.Home && alt) || (key == Keys.H && ctrl && !alt))
            {
                Log("Home shortcut received (keyboard hook)");
                BeginInvoke(new Action(GoHome));
                return (IntPtr)1;                               // swallow the key
            }
            if ((key == Keys.F4 && alt) || (key == Keys.Q && ctrl && alt))
            {
                BeginInvoke(new Action(Close));
                return (IntPtr)1;
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        UnregisterHomeHotkey();
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        base.OnFormClosed(e);
    }

    private void GoHome()
    {
        var core = _web.CoreWebView2;
        if (core is null) return;
        // already on the launcher: let it scroll back to the first app instead of reloading
        if (!_internalNav && IsOnLauncher()) core.PostWebMessageAsJson("{\"type\":\"home\"}");
        else core.Navigate(_home.ToString());
    }

    private bool IsAllowed(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return false;
        if (u.Scheme == "about") return true;
        if (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp) return false;

        var host = u.Host.ToLowerInvariant();
        if (host == _home.Host.ToLowerInvariant()) return true;

        foreach (var d in _cfg.AllowedDomains)
        {
            var domain = d.Trim().TrimStart('.').ToLowerInvariant();
            if (domain.Length == 0) continue;
            if (host == domain || host.EndsWith("." + domain)) return true;
        }
        return false;
    }

    // Built with createElement only (no innerHTML) because sites like YouTube forbid string-to-HTML
    // assignments (Trusted Types), and styled with a constructed stylesheet so a site's CSP can't block it.
    private const string HomeButtonScript = """
    (() => {
      const HOME = __HOME__;
      // optional: refuse fullscreen so pages stay windowed (the window already fills the screen)
      if (__BLOCKFS__) {
        const deny = () => Promise.reject(new TypeError('Fullscreen is turned off'));
        Element.prototype.requestFullscreen = deny;
        Element.prototype.webkitRequestFullscreen = deny;
      }
      if (window.top !== window) return;
      const CSS = `
        button {
          all: initial; box-sizing: border-box; width: 52px; height: 52px; border-radius: 50%;
          display: grid; place-items: center; cursor: pointer; color: #fff;
          background: rgba(18, 20, 28, .72); border: 2px solid rgba(255, 255, 255, .28);
          backdrop-filter: blur(6px); opacity: .4; transition: opacity .15s, transform .15s;
        }
        button:hover, button:focus-visible { opacity: 1; transform: scale(1.08); outline: 3px solid #fff; }
        svg { width: 26px; height: 26px; fill: none; stroke: currentColor; stroke-width: 2; stroke-linecap: round; stroke-linejoin: round; }
      `;
      const add = () => {
        const href = location.href;
        if (href.startsWith('about:') || href.startsWith('data:') || href.startsWith(HOME)) return;
        if (document.getElementById('tv-home-button-host')) return;
        const host = document.createElement('div');
        host.id = 'tv-home-button-host';
        host.style.cssText = 'all:initial;position:fixed;top:14px;left:14px;z-index:2147483647;transition:opacity .3s;';
        const root = host.attachShadow({ mode: 'closed' });
        try {
          const sheet = new CSSStyleSheet();
          sheet.replaceSync(CSS);
          root.adoptedStyleSheets = [sheet];
        } catch {
          const style = document.createElement('style');
          style.textContent = CSS;
          root.append(style);
        }
        const NS = 'http://www.w3.org/2000/svg';
        const svg = document.createElementNS(NS, 'svg');
        svg.setAttribute('viewBox', '0 0 24 24');
        for (const d of ['M3 11.5 12 4l9 7.5', 'M5.5 10v10h13V10', 'M10 20v-6h4v6']) {
          const p = document.createElementNS(NS, 'path');
          p.setAttribute('d', d);
          svg.append(p);
        }
        const button = document.createElement('button');
        button.type = 'button';
        button.setAttribute('aria-label', 'Home');
        button.append(svg);
        button.addEventListener('click', () => { location.href = HOME; });
        root.append(button);
        document.documentElement.append(host);

        // A fullscreen player hides everything outside of it, so move the button inside the
        // fullscreen element while a video is fullscreen, and fade it out when idle.
        let idle = 0;
        const place = () => {
          const fs = document.fullscreenElement;
          const target = fs && !(fs instanceof HTMLVideoElement) ? fs : document.documentElement;
          if (host.parentNode !== target) target.append(host);
          host.style.opacity = '1';
          clearTimeout(idle);
          if (fs) idle = setTimeout(() => { host.style.opacity = '0'; }, 3000);
        };
        document.addEventListener('fullscreenchange', place);
        for (const ev of ['mousemove', 'pointerdown', 'keydown', 'touchstart'])
          document.addEventListener(ev, () => { if (document.fullscreenElement) place(); }, true);
      };
      window.addEventListener('keydown', e => {
        if (e.ctrlKey && !e.altKey && !e.shiftKey && (e.key === 'h' || e.key === 'H')) {
          e.preventDefault(); location.href = HOME;
        }
      }, true);
      if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', add);
      else add();
    })();
    """;
}
