using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Kenma.Engines;

namespace Kenma
{
    public partial class MainWindow : Window
    {
        const string GlyphSend = "\uE724", GlyphStop = "\uE71A", GlyphPinned = "\uE840", GlyphPin = "\uE718";

        readonly Settings _settings;
        readonly DispatcherTimer _saveDebounce = new() { Interval = TimeSpan.FromMilliseconds(800) };
        CancellationTokenSource? _userCancel;
        bool _busy;
        IntPtr _hwnd;

        public bool ReallyClosing { get; set; }
        public event Action? SettingsChanged;
        public event Action? HiddenToTray;

        public MainWindow(Settings settings)
        {
            _settings = settings;
            InitializeComponent();
            HistoryStore.Load(settings.HistoryDays);

            RefreshIcons();
            Width = settings.Width;
            Height = settings.Height;
            SetInitialPosition();
            ApplyTopmost();

            _saveDebounce.Tick += (_, _) => { _saveDebounce.Stop(); _settings.Save(); };

            Loaded += (_, _) =>
            {
                AddAssistant("Paste a rough prompt and press Enter. You'll get a polished, well-structured version, copied to your clipboard.", isResult: false);
                Input.Focus();
                RefreshStatus();
            };
            Connectivity.Changed += () => Dispatcher.BeginInvoke(() =>
            {
                LocalProbe.Invalidate();
                RefreshStatus();
            });
            SourceInitialized += (_, _) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
                EnsureOnScreen();
                ApplyHotkey();
            };
            LocationChanged += (_, _) => Remember();
            SizeChanged += (_, _) => Remember();
            StateChanged += (_, _) =>
            {
                // no taskbar button, so minimising means "go to tray"
                if (WindowState == WindowState.Minimized) { WindowState = WindowState.Normal; HideToTray(); }
            };
        }

        // ---------- window placement / visibility ----------

        void SetInitialPosition()
        {
            if (double.IsFinite(_settings.Left) && double.IsFinite(_settings.Top))
            {
                Left = _settings.Left;
                Top = _settings.Top;
            }
            else MoveToDefaultCorner();
        }

        void MoveToDefaultCorner()
        {
            var wa = SystemParameters.WorkArea; // primary monitor, in DIPs
            Left = wa.Right - Width - 12;
            Top = wa.Bottom - Height - 12;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);

        /// <summary>If the saved position is on a monitor that no longer exists, bring the window back to the primary screen.</summary>
        void EnsureOnScreen()
        {
            if (!GetWindowRect(_hwnd, out var r)) return;
            // the title bar strip must be on some monitor, otherwise the window can't be grabbed
            var titleBar = new RECT { Left = r.Left + 40, Top = r.Top, Right = r.Right - 40, Bottom = r.Top + 30 };
            if (MonitorFromRect(ref titleBar, 0 /*MONITOR_DEFAULTTONULL*/) != IntPtr.Zero) return;
            Log.Info("Saved window position is off-screen; moving to the primary monitor");
            MoveToDefaultCorner();
        }

        void Remember()
        {
            if (!IsLoaded || WindowState != WindowState.Normal) return;
            _settings.Left = Left; _settings.Top = Top;
            _settings.Width = ActualWidth; _settings.Height = ActualHeight;
            _saveDebounce.Stop();
            _saveDebounce.Start();
        }

        public void ShowAndFocus()
        {
            Show();
            WindowState = WindowState.Normal;
            EnsureOnScreen();
            Activate();
            Input.Focus();
            RefreshStatus();
        }

        public void Toggle()
        {
            if (IsVisible) HideToTray(); else ShowAndFocus();
        }

        void HideToTray()
        {
            _settings.Save();
            Hide();
            HiddenToTray?.Invoke();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!ReallyClosing) { e.Cancel = true; HideToTray(); return; }
            _userCancel?.Cancel();
            if (_hwnd != IntPtr.Zero) Hotkey.Unregister(_hwnd, HotkeyId);
            _settings.Save();
            base.OnClosing(e);
        }

        public void ApplyTopmost()
        {
            Topmost = _settings.Topmost;
            PinBtn.Content = _settings.Topmost ? GlyphPinned : GlyphPin;
            PinBtn.ToolTip = _settings.Topmost ? "Always on top (on)" : "Always on top (off)";
            PinBtn.SetResourceReference(ForegroundProperty, _settings.Topmost ? "AccentTextFillColorPrimaryBrush" : "TextFillColorPrimaryBrush");
        }

        public void RefreshFromSettings() => ApplyTopmost();

        public void RefreshIcons()
        {
            Icon = IconFactory.ToWpf(64);
            AppLogo.Source = IconFactory.ToWpf(64);
        }

        // ---------- global hotkey ----------

        const int HotkeyId = 0x5D58;
        string? _hotkeyError;

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x0312 /*WM_HOTKEY*/ && w.ToInt32() == HotkeyId) { Toggle(); handled = true; }
            return IntPtr.Zero;
        }

        /// <summary>(Re)registers the shortcut from settings and tells the user if Windows refused it.</summary>
        void ApplyHotkey()
        {
            if (_hwnd == IntPtr.Zero) return;
            _hotkeyError = Hotkey.Register(_hwnd, HotkeyId, _settings.Hotkey);
            if (_hotkeyError != null)
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => AddError(_hotkeyError, "Shortcut not available")));
        }

        void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.Tab) { e.Handled = false; return; }
            if (Hotkey.IsModifierKey(key)) return;
            var mods = Keyboard.Modifiers;
            if (mods == ModifierKeys.None || mods == ModifierKeys.Shift)
            {
                HotkeyCaption.Text = "Include Ctrl, Alt or Win so the shortcut doesn't clash with normal typing.";
                return;
            }
            HotkeyBox.Text = Hotkey.Format(mods, key);
            HotkeyCaption.Text = "Saved when you click Save.";
        }

        // ---------- toolbar ----------

        void Pin_Click(object sender, RoutedEventArgs e)
        {
            _settings.Topmost = !_settings.Topmost;
            ApplyTopmost();
            _settings.Save();
            SettingsChanged?.Invoke();
        }

        void Clear_Click(object sender, RoutedEventArgs e)
        {
            Messages.Children.Clear();
            AddAssistant("Conversation cleared. Enter your next prompt.", isResult: false);
        }

        // ---------- settings ----------

        /// <summary>Unsaved edits for one engine while the Settings panel is open.</summary>
        sealed class EngineDraft { public string Key = "", Model = "", BaseUrl = ""; }

        readonly Dictionary<string, EngineDraft> _drafts = new();
        string _shownEngine = EngineCatalog.Auto;
        bool _loadingSettings;

        string SelectedEngineId => (EngineBox.SelectedItem as ComboBoxItem)?.Tag as string ?? EngineCatalog.Auto;

        void Settings_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsPanel.Visibility == Visibility.Visible) { CloseSettings(); return; }
            HistoryPanel.Visibility = Visibility.Collapsed;

            _drafts.Clear();
            _shownEngine = EngineCatalog.Auto;
            _loadingSettings = true;
            EngineBox.Items.Clear();
            EngineBox.Items.Add(new ComboBoxItem { Content = "Automatic (recommended)", Tag = EngineCatalog.Auto });
            foreach (var info in EngineCatalog.All)
                EngineBox.Items.Add(new ComboBoxItem { Content = info.Name, Tag = info.Id });
            EngineBox.SelectedItem = EngineBox.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == _settings.PrimaryEngine);
            _loadingSettings = false;
            ShowEngineFields(_settings.PrimaryEngine);

            FallbackCliCheck.IsChecked = _settings.FallbackClaudeCli;
            FallbackLocalCheck.IsChecked = _settings.FallbackLocal;
            FallbackTemplateCheck.IsChecked = _settings.FallbackTemplate;
            HotkeyBox.Text = _settings.Hotkey;
            HotkeyCaption.Text = _hotkeyError ?? "Click the box and press a key combination with Ctrl, Alt or Win.";
            StartupCheck.IsChecked = _settings.StartWithWindows;
            AutoCopyCheck.IsChecked = _settings.AutoCopy;
            SaveHistoryCheck.IsChecked = _settings.SaveHistory;
            RetentionBox.SelectedIndex = _settings.HistoryDays switch { 7 => 0, 30 => 1, 0 => 3, _ => 2 };
            SettingsPanel.Visibility = Visibility.Visible;
        }

        void CloseSettings()
        {
            ApiKeyBox.Clear();
            _drafts.Clear(); // drop any unsaved keys from memory
            SettingsPanel.Visibility = Visibility.Collapsed;
        }

        void CancelSettings_Click(object sender, RoutedEventArgs e) => CloseSettings();

        EngineDraft Draft(string id)
        {
            if (_drafts.TryGetValue(id, out var d)) return d;
            var info = EngineCatalog.Info(id);
            var cfg = _settings.EngineConfig(id);
            d = new EngineDraft
            {
                Key = info.NeedsKey ? Secrets.Get(info.KeyTarget) : "",
                Model = cfg.Model.Length > 0 ? cfg.Model : info.DefaultModel,
                BaseUrl = cfg.BaseUrl.Length > 0 ? cfg.BaseUrl : info.DefaultBaseUrl
            };
            return _drafts[id] = d;
        }

        /// <summary>Copies what's in the fields back into the draft of the engine currently shown.</summary>
        void StashShownEngine()
        {
            if (_shownEngine == EngineCatalog.Auto) return;
            var d = Draft(_shownEngine);
            d.Key = ApiKeyBox.Password.Trim();
            d.Model = ModelBox.Text.Trim();
            d.BaseUrl = BaseUrlBox.Text.Trim();
        }

        void EngineBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loadingSettings) ShowEngineFields(SelectedEngineId);
        }

        void ShowEngineFields(string id)
        {
            StashShownEngine();
            _shownEngine = id;
            TestResult.Text = "";
            ModelBox.Items.Clear();

            if (id == EngineCatalog.Auto)
            {
                EngineCaption.Text = "Uses the first engine that's ready: cloud APIs that have a key, then Claude Code, then Ollama or LM Studio, then the offline template. Choose an engine above to set its key or model.";
                KeyPanel.Visibility = UrlPanel.Visibility = ModelPanel.Visibility = FallbackPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var info = EngineCatalog.Info(id);
            var d = Draft(id);
            EngineCaption.Text = info.Description + (info.NeedsKey ? $" The {info.EnvVar} environment variable also works." : "");
            KeyPanel.Visibility = info.NeedsKey ? Visibility.Visible : Visibility.Collapsed;
            UrlPanel.Visibility = info.IsLocal && info.HasModels ? Visibility.Visible : Visibility.Collapsed;
            ModelPanel.Visibility = info.HasModels ? Visibility.Visible : Visibility.Collapsed;
            FallbackPanel.Visibility = id == EngineCatalog.Template ? Visibility.Collapsed : Visibility.Visible;
            ApiKeyBox.Password = d.Key;
            BaseUrlBox.Text = d.BaseUrl;
            ModelBox.Text = d.Model;
            ModelCaption.Text = info.IsLocal ? "Leave empty to use the first installed model." : "Click Load models to choose from the models your key can use.";
        }

        /// <summary>Builds an engine from the unsaved values in the panel, for Load models and Test.</summary>
        IEngine DraftEngine(string id)
        {
            StashShownEngine();
            var d = Draft(id);
            var temp = new Settings();
            var cfg = temp.EngineConfig(id);
            cfg.Model = d.Model;
            cfg.BaseUrl = d.BaseUrl;
            return EngineCatalog.Create(id, temp, d.Key);
        }

        async void LoadModels_Click(object sender, RoutedEventArgs e)
        {
            var id = _shownEngine;
            if (id == EngineCatalog.Auto || DraftEngine(id) is not IModelSource source) return;
            LoadModelsBtn.IsEnabled = false;
            ModelCaption.Text = "Loading models…";
            try
            {
                LocalProbe.Invalidate();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var models = await source.ListModelsAsync(cts.Token);
                if (_shownEngine != id) return;
                var current = ModelBox.Text;
                ModelBox.Items.Clear();
                foreach (var m in models) ModelBox.Items.Add(m);
                ModelBox.Text = current;
                ModelCaption.Text = models.Count == 0 ? "No models found." : $"{models.Count} models found. Pick one from the list.";
                if (models.Count > 0) ModelBox.IsDropDownOpen = true;
            }
            catch (EnhanceException ex) { ModelCaption.Text = ex.Message; }
            catch (OperationCanceledException) { ModelCaption.Text = "Timed out while loading models."; }
            catch (Exception ex) { Log.Error("Loading models failed", ex); ModelCaption.Text = "Couldn't load models. Details are in the log."; }
            finally { LoadModelsBtn.IsEnabled = true; }
        }

        async void TestEngine_Click(object sender, RoutedEventArgs e)
        {
            TestBtn.IsEnabled = false;
            TestResult.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            TestResult.Text = "Testing…";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                const string sample = "write a short thank-you note to a colleague";
                string used;
                if (_shownEngine == EngineCatalog.Auto)
                {
                    var result = await EngineChain.EnhanceAsync(sample, _settings, cts.Token);
                    used = EngineLabel(result.Engine);
                }
                else
                {
                    LocalProbe.Invalidate();
                    var engine = DraftEngine(_shownEngine);
                    var notReady = await engine.CheckAsync(cts.Token);
                    if (notReady != null) throw new EnhanceException($"Not ready: {notReady}.");
                    await engine.EnhanceAsync(sample, cts.Token);
                    used = EngineLabel(engine);
                }
                TestResult.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorSuccessBrush");
                TestResult.Text = $"Works: {used}, {sw.Elapsed.TotalSeconds:0.0} s";
            }
            catch (Exception ex)
            {
                TestResult.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
                TestResult.Text = ex switch
                {
                    EnhanceException => ex.Message,
                    OperationCanceledException => "No response within 90 seconds.",
                    _ => "Test failed. Details are in the log."
                };
                if (ex is not EnhanceException and not OperationCanceledException) Log.Error("Engine test failed", ex);
            }
            finally { TestBtn.IsEnabled = true; }
        }

        void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            StashShownEngine();
            foreach (var (id, d) in _drafts)
            {
                var info = EngineCatalog.Info(id);
                if (info.NeedsKey && d.Key != Secrets.Get(info.KeyTarget) && !EngineCatalog.SetKey(id, d.Key))
                    AddError("Windows Credential Manager refused to store the API key. Details are in the log.", $"{info.Name} key not saved");
                var cfg = _settings.EngineConfig(id);
                cfg.Model = d.Model == info.DefaultModel ? "" : d.Model;       // empty = follow the default
                cfg.BaseUrl = d.BaseUrl == info.DefaultBaseUrl ? "" : d.BaseUrl;
            }

            _settings.PrimaryEngine = SelectedEngineId;
            _settings.FallbackClaudeCli = FallbackCliCheck.IsChecked == true;
            _settings.FallbackLocal = FallbackLocalCheck.IsChecked == true;
            _settings.FallbackTemplate = FallbackTemplateCheck.IsChecked == true;
            _settings.StartWithWindows = StartupCheck.IsChecked == true;
            _settings.AutoCopy = AutoCopyCheck.IsChecked == true;
            _settings.SaveHistory = SaveHistoryCheck.IsChecked == true;
            _settings.HistoryDays = RetentionBox.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out var days) ? days : 90;

            var hotkeyChanged = HotkeyBox.Text != _settings.Hotkey && Hotkey.TryParse(HotkeyBox.Text, out _, out _);
            if (hotkeyChanged) _settings.Hotkey = HotkeyBox.Text;

            Autostart.Set(_settings.StartWithWindows);
            if (HistoryStore.Prune(_settings.HistoryDays)) HistoryStore.Save();
            _settings.Save();
            if (hotkeyChanged) ApplyHotkey();
            LocalProbe.Invalidate();
            SettingsChanged?.Invoke();
            CloseSettings();
            RefreshStatus();
        }

        // ---------- engine status ----------

        int _statusVersion;

        static string EngineLabel(IEngine engine) => engine.Detail.Length > 0 ? $"{engine.Name} ({engine.Detail})" : engine.Name;

        /// <summary>Updates the dot and text under the title with the engine a request would use right now.</summary>
        public async void RefreshStatus()
        {
            int version = ++_statusVersion;
            try
            {
                var (engine, skipped) = await EngineChain.FirstReadyAsync(_settings, CancellationToken.None);
                if (version != _statusVersion) return; // a newer refresh started meanwhile

                string brush, text;
                var prefix = Connectivity.IsOnline ? "" : "Offline · ";
                if (engine == null) { brush = "SystemFillColorCriticalBrush"; text = prefix + "No engine available"; }
                else if (!engine.IsAi) { brush = "SystemFillColorCautionBrush"; text = prefix + "Template mode (no AI)"; }
                else
                {
                    brush = skipped.Count == 0 ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush";
                    text = prefix + (engine.Detail.Length > 0 ? $"{engine.Name} · {engine.Detail}" : engine.Name);
                }
                StatusDot.SetResourceReference(Shape.FillProperty, brush);
                StatusText.Text = text;
                StatusPanel.ToolTip = (engine != null ? $"Using {EngineLabel(engine)}" : "No engine is ready")
                    + (skipped.Count > 0 ? "\nSkipped:\n• " + string.Join("\n• ", skipped) : "");
            }
            catch (Exception ex) { Log.Error("Status refresh failed", ex); }
        }

        // ---------- theme helpers (Fluent resources, follow light/dark + accent live) ----------

        static T Themed<T>(T el, DependencyProperty dp, string key) where T : FrameworkElement
        {
            el.SetResourceReference(dp, key);
            return el;
        }

        static TextBlock Text(string text, string brushKey = "TextFillColorPrimaryBrush", double size = 13)
        {
            var tb = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
            return Themed(tb, TextBlock.ForegroundProperty, brushKey);
        }

        static TextBlock Glyph(string glyph, string brushKey, double size = 12)
        {
            var tb = new TextBlock { Text = glyph, FontSize = size, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 8, 0) };
            return Themed(tb, TextBlock.ForegroundProperty, brushKey);
        }

        static string ScoreKey(int score) =>
            score < 40 ? "SystemFillColorCriticalBrush" : score < 70 ? "SystemFillColorCautionBrush" : "SystemFillColorSuccessBrush";

        Border Card(UIElement child, bool alignRight = false)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = alignRight ? new Thickness(48, 4, 0, 4) : new Thickness(0, 4, 24, 4),
                HorizontalAlignment = alignRight ? HorizontalAlignment.Right : HorizontalAlignment.Stretch,
                BorderThickness = new Thickness(1),
                Child = child
            };
            Themed(b, Border.BackgroundProperty, "CardBackgroundFillColorDefaultBrush");
            Themed(b, Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
            return b;
        }

        TextBox SelectableText(string text, string brushKey)
        {
            var tb = new TextBox
            {
                Text = text,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                MinHeight = 0,
                FontSize = 13,
                Template = PlainTextBoxTemplate()
            };
            return Themed(tb, TextBox.ForegroundProperty, brushKey);
        }

        // Fluent TextBox style adds borders/underline; use a bare template for read-only message text.
        static ControlTemplate? _plainTemplate;
        static ControlTemplate PlainTextBoxTemplate()
        {
            if (_plainTemplate != null) return _plainTemplate;
            var t = new ControlTemplate(typeof(TextBox));
            var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
            host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            t.VisualTree = host;
            return _plainTemplate = t;
        }

        // ---------- composer ----------

        void Input_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool empty = Input.Text.Trim().Length == 0;
            Hint.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            LiveScorePanel.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            if (empty) return;
            var r = PreCheck.Analyze(Input.Text);
            LiveScore.Text = $"Clarity {r.Score}/100 · {r.Kind}" + (r.Missing.Count > 0 ? $" · {r.Missing.Count} gap{(r.Missing.Count == 1 ? "" : "s")}" : "");
            LiveScoreDot.SetResourceReference(Shape.FillProperty, ScoreKey(r.Score));
        }

        void Input_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
            {
                e.Handled = true;
                Send();
            }
            else if (e.Key == Key.Escape) HideToTray();
        }

        void Send_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) _userCancel?.Cancel();
            else Send();
        }

        void SetBusy(bool busy)
        {
            _busy = busy;
            SendGlyph.Text = busy ? GlyphStop : GlyphSend;
            SendBtn.ToolTip = busy ? "Stop" : "Enhance (Enter)";
        }

        async void Send()
        {
            var raw = Input.Text.Trim();
            if (raw.Length == 0 || _busy) return;
            Input.Clear();
            SettingsPanel.Visibility = Visibility.Collapsed;
            HistoryPanel.Visibility = Visibility.Collapsed;

            AddUser(raw);
            AddPreCheck(PreCheck.Analyze(raw));
            var thinking = AddThinking();

            // Per-engine timeouts live in EngineChain (a slow engine falls back to the next); this is only the Stop button.
            using var userCancel = new CancellationTokenSource();
            _userCancel = userCancel;
            SetBusy(true);
            try
            {
                var result = await EngineChain.EnhanceAsync(raw, _settings, userCancel.Token);
                RemoveThinking(thinking);
                ShowResult(raw, result.Text, result.Engine, result.Skipped);
            }
            catch (OperationCanceledException) when (userCancel.IsCancellationRequested)
            {
                RemoveThinking(thinking);
                AddNotice("Stopped. Your prompt is back in the input box.");
                if (Input.Text.Length == 0) Input.Text = raw;
            }
            catch (EnhanceException ex)
            {
                RemoveThinking(thinking);
                AddError(ex.Message, retryRaw: raw);
            }
            catch (Exception ex)
            {
                RemoveThinking(thinking);
                Log.Error("Unexpected enhancement failure", ex);
                AddError("Something unexpected went wrong. Details were written to the log.", retryRaw: raw);
            }
            finally
            {
                _userCancel = null;
                SetBusy(false);
                Input.CaretIndex = Input.Text.Length;
                Input.Focus();
                RefreshStatus();
            }
        }

        void ShowResult(string raw, string text, IEngine engine, IReadOnlyList<string> skipped)
        {
            AddAssistant(text, isResult: true, engineLabel: EngineLabel(engine), isTemplate: !engine.IsAi, skipped: skipped);
            if (_settings.AutoCopy) CopyToClipboard(text);
            if (_settings.SaveHistory) HistoryStore.Add(raw, text, EngineLabel(engine), _settings.HistoryDays);
        }

        static void CopyToClipboard(string text)
        {
            for (int i = 0; i < 5; i++)
            {
                try { Clipboard.SetText(text); return; }
                catch { Thread.Sleep(40); }
            }
        }

        // ---------- messages ----------

        void AddUser(string text)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(48, 8, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Right,
                Child = SelectableText(text, "TextOnAccentFillColorPrimaryBrush")
            };
            Themed(b, Border.BackgroundProperty, "AccentFillColorDefaultBrush");
            AddWithFade(b);
        }

        void AddAssistant(string text, bool isResult, bool showCopiedNote = true,
            string? engineLabel = null, bool isTemplate = false, IReadOnlyList<string>? skipped = null)
        {
            var stack = new StackPanel();
            if (isResult)
            {
                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var label = isTemplate
                    ? Text("Offline template · not AI-enhanced", "SystemFillColorCautionBrush", 12)
                    : Text("Enhanced prompt", "AccentTextFillColorPrimaryBrush", 12);
                label.FontWeight = FontWeights.SemiBold;
                label.VerticalAlignment = VerticalAlignment.Center;
                var copy = new Button { Style = (Style)FindResource("SubtleBtn"), Content = "", ToolTip = "Copy", Width = 28, Height = 28 };
                copy.Click += (_, _) =>
                {
                    CopyToClipboard(text);
                    copy.Content = "";
                    var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                    t.Tick += (_, _) => { t.Stop(); copy.Content = ""; };
                    t.Start();
                };
                DockPanel.SetDock(copy, Dock.Right);
                head.Children.Add(copy);
                head.Children.Add(label);
                stack.Children.Add(head);
            }
            stack.Children.Add(SelectableText(text, "TextFillColorPrimaryBrush"));

            if (isResult)
            {
                var footer = new List<string>();
                if (engineLabel != null && !isTemplate) footer.Add("via " + engineLabel);
                if (_settings.AutoCopy && showCopiedNote) footer.Add("copied to clipboard");
                if (footer.Count > 0)
                {
                    var note = Text(char.ToUpper(footer[0][0]) + string.Join(" · ", footer)[1..], "TextFillColorTertiaryBrush", 11);
                    note.Margin = new Thickness(0, 8, 0, 0);
                    stack.Children.Add(note);
                }
                if (skipped is { Count: > 0 })
                {
                    var fell = Text("Fell back after: " + string.Join("; ", skipped), "TextFillColorTertiaryBrush", 11);
                    fell.Margin = new Thickness(0, 2, 0, 0);
                    stack.Children.Add(fell);
                }
            }
            AddWithFade(Card(stack));
        }

        void AddNotice(string message)
        {
            var note = Text(message, "TextFillColorSecondaryBrush", 12);
            note.Margin = new Thickness(4, 6, 4, 6);
            AddWithFade(note);
        }

        /// <param name="retryRaw">When set, the card offers Retry and "Use offline template" for this prompt.</param>
        void AddError(string message, string heading = "Couldn't enhance this prompt", string? retryRaw = null)
        {
            var row = new DockPanel();
            var g = Glyph("", "SystemFillColorCriticalBrush", 14);
            DockPanel.SetDock(g, Dock.Left);
            row.Children.Add(g);
            var body = new StackPanel();
            var title = Text(heading);
            title.FontWeight = FontWeights.SemiBold;
            body.Children.Add(title);
            body.Children.Add(Text(message, "TextFillColorSecondaryBrush", 12));

            if (retryRaw != null)
            {
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
                var retry = new Button { Content = "Retry", MinWidth = 72, Margin = new Thickness(0, 0, 8, 0) };
                retry.Click += (_, _) =>
                {
                    if (_busy) return;
                    Input.Text = retryRaw;
                    Send();
                };
                var template = new Button { Content = "Use offline template" };
                template.Click += (_, _) =>
                {
                    template.IsEnabled = false;
                    ShowResult(retryRaw, TemplateEngine.Build(retryRaw), new TemplateEngine(), Array.Empty<string>());
                    if (Input.Text.Trim() == retryRaw) Input.Clear();
                };
                actions.Children.Add(retry);
                actions.Children.Add(template);
                body.Children.Add(actions);
            }

            row.Children.Add(body);
            var card = Card(row);
            Themed(card, Border.BackgroundProperty, "SystemFillColorCriticalBackgroundBrush");
            AddWithFade(card);
        }

        void AddPreCheck(CheckResult r)
        {
            var stack = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            var dot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(Shape.FillProperty, ScoreKey(r.Score));
            head.Children.Add(dot);
            var title = Text($"Pre-check · {r.Kind} · {r.Score}/100", "TextFillColorPrimaryBrush", 12);
            title.FontWeight = FontWeights.SemiBold;
            head.Children.Add(title);
            stack.Children.Add(head);

            void Row(string glyph, string glyphKey, string text)
            {
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var g = Glyph(glyph, glyphKey, 11);
                DockPanel.SetDock(g, Dock.Left);
                row.Children.Add(g);
                row.Children.Add(Text(text, "TextFillColorSecondaryBrush", 12));
                stack.Children.Add(row);
            }
            foreach (var g in r.Good) Row("\uE73E", "SystemFillColorSuccessBrush", g);
            foreach (var m in r.Missing) Row("\uE7BA", "SystemFillColorCautionBrush", m);
            if (r.Missing.Count > 0)
            {
                var note = Text("Gaps are filled with stated assumptions or [PLACEHOLDERS].", "TextFillColorTertiaryBrush", 11);
                note.Margin = new Thickness(0, 6, 0, 0);
                stack.Children.Add(note);
            }
            AddWithFade(Card(stack));
        }

        Border AddThinking()
        {
            var (mark, stop) = AnimatedMark(28);
            mark.Margin = new Thickness(0, 0, 12, 0);
            var label = Text("Polishing your prompt…", "TextFillColorSecondaryBrush", 12);
            label.VerticalAlignment = VerticalAlignment.Center;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(mark);
            row.Children.Add(label);
            var card = Card(row);
            card.Tag = stop;
            AddWithFade(card);
            return card;
        }

        void RemoveThinking(Border thinking)
        {
            (thinking.Tag as Action)?.Invoke();
            Messages.Children.Remove(thinking);
        }

        /// <summary>
        /// The Kenma mark as live vector shapes, animated while a prompt is being enhanced: the tag brackets
        /// move in and out like polishing strokes and the gem turns and pulses. Holds still when Windows'
        /// "Animation effects" setting is off. Returns a callback that stops the animations.
        /// </summary>
        (FrameworkElement Mark, Action Stop) AnimatedMark(double size)
        {
            var shapes = IconFactory.Shapes();
            Polygon Make(System.Drawing.PointF[] points)
            {
                var p = new Polygon { Points = new PointCollection(points.Select(q => new Point(q.X, q.Y))) };
                p.SetResourceReference(Shape.FillProperty, "AccentFillColorDefaultBrush");
                return p;
            }
            Polygon left = Make(shapes[0]), right = Make(shapes[1]), gem = Make(shapes[2]);
            var canvas = new Canvas { Width = 16, Height = 16 };
            canvas.Children.Add(left);
            canvas.Children.Add(right);
            canvas.Children.Add(gem);
            var view = new Viewbox { Width = size, Height = size, Child = canvas };
            if (!SystemParameters.ClientAreaAnimation) return (view, () => { });

            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            var stroke = TimeSpan.FromMilliseconds(650);
            var leftMove = new TranslateTransform();
            var rightMove = new TranslateTransform();
            left.RenderTransform = leftMove;
            right.RenderTransform = rightMove;
            leftMove.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-1.1, 0.35, stroke) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
            rightMove.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(1.1, -0.35, stroke) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });

            var spin = new RotateTransform(0, 8, 8);
            var pulse = new ScaleTransform(1, 1, 8, 8);
            gem.RenderTransform = new TransformGroup { Children = { pulse, spin } };
            spin.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(1300)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
            var grow = new DoubleAnimation(0.7, 1.15, stroke) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
            pulse.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            pulse.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

            return (view, () =>
            {
                leftMove.BeginAnimation(TranslateTransform.XProperty, null);
                rightMove.BeginAnimation(TranslateTransform.XProperty, null);
                spin.BeginAnimation(RotateTransform.AngleProperty, null);
                pulse.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                pulse.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            });
        }

        void AddWithFade(UIElement el)
        {
            el.Opacity = 0;
            Messages.Children.Add(el);
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Scroller.ScrollToEnd()));
        }

        // ---------- history ----------

        void History_Click(object sender, RoutedEventArgs e)
        {
            if (HistoryPanel.Visibility == Visibility.Visible) { HistoryPanel.Visibility = Visibility.Collapsed; return; }
            SettingsPanel.Visibility = Visibility.Collapsed;
            HistorySearch.Text = "";
            RenderHistory();
            HistoryPanel.Visibility = Visibility.Visible;
            HistorySearch.Focus();
        }

        void HistorySearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchHint.Visibility = HistorySearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            RenderHistory();
        }

        void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            if (HistoryStore.Items.Count == 0) return;
            if (MessageBox.Show(this, "Delete all saved prompts?", "Kenma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            HistoryStore.Clear();
            RenderHistory();
        }

        static string Preview(string s, int max)
        {
            var one = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return one.Length <= max ? one : one.Substring(0, max) + "…";
        }

        void RenderHistory()
        {
            HistoryList.Children.Clear();
            var q = HistorySearch.Text.Trim();
            int shown = 0;
            foreach (var item in HistoryStore.Items)
            {
                if (q.Length > 0 &&
                    item.Raw.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                    item.Enhanced.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                HistoryList.Children.Add(HistoryCard(item));
                shown++;
            }
            if (shown == 0)
            {
                var empty = Text(q.Length > 0 ? "No matching prompts." : "No history yet. Enhanced prompts appear here.", "TextFillColorSecondaryBrush", 12);
                empty.Margin = new Thickness(2, 6, 2, 0);
                HistoryList.Children.Add(empty);
            }
        }

        Border HistoryCard(HistoryEntry item)
        {
            Button Act(string glyph, string tip, Action onClick)
            {
                var b = new Button { Style = (Style)FindResource("SubtleBtn"), Content = glyph, ToolTip = tip, Width = 28, Height = 28, FontSize = 12 };
                b.Click += (_, _) => onClick();
                return b;
            }

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(Act("\uE8C8", "Copy enhanced prompt", () => CopyToClipboard(item.Enhanced)));
            actions.Children.Add(Act("\uE70F", "Edit and resend original", () =>
            {
                HistoryPanel.Visibility = Visibility.Collapsed;
                Input.Text = item.Raw;
                Input.CaretIndex = Input.Text.Length;
                Input.Focus();
            }));
            actions.Children.Add(Act("\uE8A7", "Open in conversation", () =>
            {
                HistoryPanel.Visibility = Visibility.Collapsed;
                AddUser(item.Raw);
                AddAssistant(item.Enhanced, isResult: true, showCopiedNote: false, engineLabel: item.Engine.Length > 0 ? item.Engine : null);
            }));
            actions.Children.Add(Act("\uE74D", "Delete", () => { HistoryStore.Remove(item); RenderHistory(); }));

            var head = new DockPanel();
            DockPanel.SetDock(actions, Dock.Right);
            head.Children.Add(actions);
            var time = Text(item.Time.ToString("MMM d, HH:mm") + (item.Engine.Length > 0 ? " · " + item.Engine : ""), "TextFillColorTertiaryBrush", 11);
            time.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(time);

            var body = new StackPanel();
            body.Children.Add(head);
            var raw = Text(Preview(item.Raw, 90), "TextFillColorPrimaryBrush", 13);
            raw.FontWeight = FontWeights.SemiBold;
            raw.Margin = new Thickness(0, 2, 0, 2);
            body.Children.Add(raw);
            body.Children.Add(Text(Preview(item.Enhanced, 140), "TextFillColorSecondaryBrush", 12));

            var card = Card(body);
            card.Margin = new Thickness(0, 0, 0, 6);
            card.Padding = new Thickness(12, 6, 6, 10);
            card.ToolTip = item.Enhanced.Length > 1200 ? item.Enhanced.Substring(0, 1200) + "…" : item.Enhanced;
            return card;
        }
    }
}
