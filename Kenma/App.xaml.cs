using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WF = System.Windows.Forms;

namespace Kenma
{
    public partial class App : Application
    {
        Mutex? _mutex;
        EventWaitHandle? _showEvent;
        WF.NotifyIcon? _tray;
        MainWindow? _win;
        Settings _settings = new();
        bool _errorBalloonShown;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            InstallCrashHandlers();

            // build helper: Kenma.exe --make-icon <path>
            if (e.Args.Length >= 2 && e.Args[0] == "--make-icon")
            {
                IconFactory.SaveIco(e.Args[1]);
                Shutdown();
                return;
            }

            // design helper: Kenma.exe --export-logo <dir>  (logo.svg, logo-mono.svg, logo-256.png in default Windows blue)
            if (e.Args.Length >= 2 && e.Args[0] == "--export-logo")
            {
                System.IO.Directory.CreateDirectory(e.Args[1]);
                IconFactory.SaveSvg(System.IO.Path.Combine(e.Args[1], "logo.svg"), mono: false);
                IconFactory.SaveSvg(System.IO.Path.Combine(e.Args[1], "logo-mono.svg"), mono: true);
                using (var png = IconFactory.Draw(256)) png.Save(System.IO.Path.Combine(e.Args[1], "logo-256.png"), System.Drawing.Imaging.ImageFormat.Png);
                Shutdown();
                return;
            }

            // design helper: Kenma.exe --icon-preview <png>  (all sizes, light + dark, current accent)
            if (e.Args.Length >= 2 && e.Args[0] == "--icon-preview")
            {
                IconFactory.UseSystemAccent();
                IconFactory.SavePreview(e.Args[1]);
                Shutdown();
                return;
            }

            // diagnostics: Kenma.exe --check-theme <out file>  (lists theme keys the UI relies on that are missing)
            if (e.Args.Length >= 2 && e.Args[0] == "--check-theme")
            {
                string[] keys = { "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush", "TextFillColorTertiaryBrush",
                    "AccentFillColorDefaultBrush", "TextOnAccentFillColorPrimaryBrush", "AccentTextFillColorPrimaryBrush",
                    "CardBackgroundFillColorDefaultBrush", "CardStrokeColorDefaultBrush", "DividerStrokeColorDefaultBrush",
                    "SubtleFillColorSecondaryBrush", "SubtleFillColorTertiaryBrush", "SolidBackgroundFillColorBaseBrush",
                    "SystemFillColorCriticalBrush", "SystemFillColorCautionBrush", "SystemFillColorSuccessBrush",
                    "SystemFillColorCriticalBackgroundBrush", "AccentButtonStyle" };
                System.IO.File.WriteAllText(e.Args[1], "missing: " + string.Join(", ", keys.Where(k => TryFindResource(k) == null)));
                Shutdown();
                return;
            }

            _mutex = new Mutex(true, "Kenma_single_instance", out bool isFirst);
            if (!isFirst)
            {
                try { EventWaitHandle.OpenExisting("Kenma_show").Set(); } catch { }
                Shutdown();
                return;
            }

            Log.Info($"Kenma starting (v{typeof(App).Assembly.GetName().Version})");
            AppPaths.MigrateLegacyData();
            Engines.EngineCatalog.MigrateLegacyKeys();

            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Kenma_show");
            new Thread(() =>
            {
                while (_showEvent.WaitOne())
                    Dispatcher.BeginInvoke(() => _win?.ShowAndFocus());
            }) { IsBackground = true, Name = "SingleInstanceListener" }.Start();

            _settings = Settings.Load();
            Autostart.Set(_settings.StartWithWindows);
            IconFactory.UseSystemAccent();
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, a) =>
            {
                if (a.Category is Microsoft.Win32.UserPreferenceCategory.General or Microsoft.Win32.UserPreferenceCategory.Color or Microsoft.Win32.UserPreferenceCategory.VisualStyle)
                    Dispatcher.BeginInvoke(RefreshIcons);
            };

            _win = new MainWindow(_settings);
            _win.SettingsChanged += RefreshMenu;
            _win.HiddenToTray += () =>
            {
                if (_settings.TrayHintShown || _tray == null) return;
                _settings.TrayHintShown = true;
                _settings.Save();
                _tray.ShowBalloonTip(3000, "Kenma is still running", $"Click the tray icon or press {_settings.Hotkey} to open it.", WF.ToolTipIcon.Info);
            };

            BuildTray();

            if (!e.Args.Contains("--tray")) _win.Show();
        }

        // ---------- crash handling ----------

        void InstallCrashHandlers()
        {
            DispatcherUnhandledException += (_, a) =>
            {
                Log.Error("Unhandled UI exception", a.Exception);
                a.Handled = true; // keep the tray app alive; the error is in the log
                NotifyError();
            };
            AppDomain.CurrentDomain.UnhandledException += (_, a) =>
                Log.Error($"Fatal unhandled exception (terminating={a.IsTerminating})", a.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, a) =>
            {
                Log.Error("Unobserved task exception", a.Exception);
                a.SetObserved();
            };
        }

        void NotifyError()
        {
            if (_errorBalloonShown || _tray == null) return;
            _errorBalloonShown = true;
            _tray.ShowBalloonTip(4000, "Kenma hit an unexpected error",
                "It's still running. Details were written to the log (tray menu → Open log folder).", WF.ToolTipIcon.Warning);
        }

        // ---------- tray ----------

        void RefreshIcons()
        {
            IconFactory.UseSystemAccent();
            _win?.RefreshIcons();
            if (_tray != null)
            {
                var old = _tray.Icon;
                _tray.Icon = IconFactory.TrayIcon();
                old?.Dispose();
            }
        }

        WF.ToolStripMenuItem? _toggleItem, _topItem, _startupItem;

        void BuildTray()
        {
            var menu = new WF.ContextMenuStrip();
            _toggleItem = new WF.ToolStripMenuItem("Show / Hide", null, (_, _) => _win?.Toggle());
            _topItem = new WF.ToolStripMenuItem("Always on top", null, (_, _) =>
            {
                _settings.Topmost = !_settings.Topmost;
                _settings.Save();
                _win?.RefreshFromSettings();
                RefreshMenu();
            });
            _startupItem = new WF.ToolStripMenuItem("Start with Windows", null, (_, _) =>
            {
                _settings.StartWithWindows = !_settings.StartWithWindows;
                Autostart.Set(_settings.StartWithWindows);
                _settings.Save();
                RefreshMenu();
            });
            menu.Items.Add(_toggleItem);
            menu.Items.Add(_topItem);
            menu.Items.Add(_startupItem);
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("Open log folder", null, (_, _) => OpenLogFolder());
            menu.Items.Add("Exit Kenma", null, (_, _) => ExitApp());

            _tray = new WF.NotifyIcon
            {
                Icon = IconFactory.TrayIcon(),
                Text = "Kenma",
                ContextMenuStrip = menu,
                Visible = true
            };
            _tray.MouseClick += (_, a) => { if (a.Button == WF.MouseButtons.Left) _win?.Toggle(); };
            RefreshMenu();
        }

        void RefreshMenu()
        {
            if (_toggleItem != null) _toggleItem.ShortcutKeyDisplayString = _settings.Hotkey;
            if (_topItem != null) _topItem.Checked = _settings.Topmost;
            if (_startupItem != null) _startupItem.Checked = _settings.StartWithWindows;
        }

        static void OpenLogFolder()
        {
            try
            {
                System.IO.Directory.CreateDirectory(AppPaths.LogDir);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.LogDir}\"") { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Error("Opening log folder failed", ex); }
        }

        void ExitApp()
        {
            Log.Info("Exit requested from tray");
            _settings.Save();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
            if (_win != null) { _win.ReallyClosing = true; _win.Close(); }
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
