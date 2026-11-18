using System;
using System.Linq;
using System.Threading;
using System.Windows;
using WF = System.Windows.Forms;

namespace PromptuDX
{
    public partial class App : Application
    {
        Mutex? _mutex;
        EventWaitHandle? _showEvent;
        WF.NotifyIcon? _tray;
        MainWindow? _win;
        Settings _settings = new();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // build helper: promptuDX.exe --make-icon <path>
            if (e.Args.Length >= 2 && e.Args[0] == "--make-icon")
            {
                IconFactory.SaveIco(e.Args[1]);
                Shutdown();
                return;
            }

            // diagnostics: promptuDX.exe --check-theme <out file>  (lists theme keys the UI relies on that are missing)
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

            _mutex = new Mutex(true, "promptuDX_single_instance", out bool isFirst);
            if (!isFirst)
            {
                try { EventWaitHandle.OpenExisting("promptuDX_show").Set(); } catch { }
                Shutdown();
                return;
            }

            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "promptuDX_show");
            new Thread(() =>
            {
                while (_showEvent.WaitOne())
                    Dispatcher.Invoke(() => _win?.ShowAndFocus());
            }) { IsBackground = true }.Start();

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
                _tray.ShowBalloonTip(3000, "promptuDX is still running", "Click the tray icon or press Ctrl+Alt+P to open it.", WF.ToolTipIcon.Info);
            };

            BuildTray();

            if (!e.Args.Contains("--tray")) _win.Show();
        }

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

        WF.ToolStripMenuItem? _topItem, _startupItem;

        void BuildTray()
        {
            var menu = new WF.ContextMenuStrip();
            menu.Items.Add("Show / Hide   (Ctrl+Alt+P)", null, (_, _) => _win?.Toggle());
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
            menu.Items.Add(_topItem);
            menu.Items.Add(_startupItem);
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("Exit promptuDX", null, (_, _) => ExitApp());

            _tray = new WF.NotifyIcon
            {
                Icon = IconFactory.TrayIcon(),
                Text = "promptuDX",
                ContextMenuStrip = menu,
                Visible = true
            };
            _tray.MouseClick += (_, a) => { if (a.Button == WF.MouseButtons.Left) _win?.Toggle(); };
            RefreshMenu();
        }

        void RefreshMenu()
        {
            if (_topItem != null) _topItem.Checked = _settings.Topmost;
            if (_startupItem != null) _startupItem.Checked = _settings.StartWithWindows;
        }

        void ExitApp()
        {
            _settings.Save();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            if (_win != null) { _win.ReallyClosing = true; _win.Close(); }
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            base.OnExit(e);
        }
    }
}
