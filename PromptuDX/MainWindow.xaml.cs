using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PromptuDX
{
    public partial class MainWindow : Window
    {
        readonly Settings _settings;
        bool _busy;

        public bool ReallyClosing { get; set; }
        public event Action? SettingsChanged;
        public event Action? HiddenToTray;

        public MainWindow(Settings settings)
        {
            _settings = settings;
            InitializeComponent();
            HistoryStore.Load();

            RefreshIcons();
            Width = settings.Width;
            Height = settings.Height;
            ApplyTopmost();

            Loaded += (_, _) =>
            {
                PlaceWindow();
                AddAssistant("Paste a rough prompt and press Enter. You'll get a polished, well-structured version, copied to your clipboard.", isResult: false);
                Input.Focus();
            };
            SourceInitialized += (_, _) => RegisterHotkey();
            LocationChanged += (_, _) => Remember();
            SizeChanged += (_, _) => Remember();
            StateChanged += (_, _) =>
            {
                // no taskbar button, so minimising means "go to tray"
                if (WindowState == WindowState.Minimized) { WindowState = WindowState.Normal; HideToTray(); }
            };
        }

        // ---------- window placement / visibility ----------

        void PlaceWindow()
        {
            var wa = SystemParameters.WorkArea;
            if (double.IsNaN(_settings.Left) || double.IsNaN(_settings.Top) ||
                _settings.Left < wa.Left - 50 || _settings.Left > wa.Right - 100 ||
                _settings.Top < wa.Top - 20 || _settings.Top > wa.Bottom - 100)
            {
                Left = wa.Right - Width - 12;
                Top = wa.Bottom - Height - 12;
            }
            else
            {
                Left = _settings.Left;
                Top = _settings.Top;
            }
        }

        void Remember()
        {
            if (!IsLoaded || WindowState != WindowState.Normal) return;
            _settings.Left = Left; _settings.Top = Top;
            _settings.Width = ActualWidth; _settings.Height = ActualHeight;
        }

        public void ShowAndFocus()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            Input.Focus();
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
            _settings.Save();
            base.OnClosing(e);
        }

        public void ApplyTopmost()
        {
            Topmost = _settings.Topmost;
            PinBtn.Content = _settings.Topmost ? "" : "";
            PinBtn.ToolTip = _settings.Topmost ? "Always on top (on)" : "Always on top (off)";
            PinBtn.SetResourceReference(ForegroundProperty, _settings.Topmost ? "AccentTextFillColorPrimaryBrush" : "TextFillColorPrimaryBrush");
        }

        public void RefreshFromSettings() => ApplyTopmost();

        public void RefreshIcons()
        {
            Icon = IconFactory.ToWpf(64);
            Mascot.Source = IconFactory.ToWpf(64);
        }

        // ---------- global hotkey Ctrl+Alt+P ----------

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        const int HotkeyId = 0x5D58;

        void RegisterHotkey()
        {
            var helper = new WindowInteropHelper(this);
            RegisterHotKey(helper.Handle, HotkeyId, 0x0002 | 0x0001 | 0x4000 /*CTRL|ALT|NOREPEAT*/, 0x50 /*P*/);
            HwndSource.FromHwnd(helper.Handle)?.AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (msg == 0x0312 && w.ToInt32() == HotkeyId) { Toggle(); handled = true; }
                return IntPtr.Zero;
            });
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

        void Settings_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsPanel.Visibility == Visibility.Visible) { SettingsPanel.Visibility = Visibility.Collapsed; return; }
            HistoryPanel.Visibility = Visibility.Collapsed;
            ApiKeyBox.Password = _settings.ApiKey;
            ModelBox.Text = _settings.Model;
            StartupCheck.IsChecked = _settings.StartWithWindows;
            AutoCopyCheck.IsChecked = _settings.AutoCopy;
            SettingsPanel.Visibility = Visibility.Visible;
        }

        void CancelSettings_Click(object sender, RoutedEventArgs e) => SettingsPanel.Visibility = Visibility.Collapsed;

        void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            _settings.ApiKey = ApiKeyBox.Password.Trim();
            _settings.Model = string.IsNullOrWhiteSpace(ModelBox.Text) ? "claude-opus-5-5" : ModelBox.Text.Trim();
            _settings.StartWithWindows = StartupCheck.IsChecked == true;
            _settings.AutoCopy = AutoCopyCheck.IsChecked == true;
            Autostart.Set(_settings.StartWithWindows);
            _settings.Save();
            SettingsChanged?.Invoke();
            SettingsPanel.Visibility = Visibility.Collapsed;
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

        void Send_Click(object sender, RoutedEventArgs e) => Send();

        async void Send()
        {
            var raw = Input.Text.Trim();
            if (raw.Length == 0 || _busy) return;
            _busy = true;
            SendBtn.IsEnabled = false;
            Input.Clear();
            SettingsPanel.Visibility = Visibility.Collapsed;
            HistoryPanel.Visibility = Visibility.Collapsed;

            AddUser(raw);
            AddPreCheck(PreCheck.Analyze(raw));
            var thinking = AddThinking();
            try
            {
                var result = await Enhancer.EnhanceAsync(raw, _settings, CancellationToken.None);
                Messages.Children.Remove(thinking);
                AddAssistant(result, isResult: true);
                if (_settings.AutoCopy) CopyToClipboard(result);
                HistoryStore.Add(raw, result);
            }
            catch (Exception ex)
            {
                Messages.Children.Remove(thinking);
                AddError(ex.Message);
            }
            finally
            {
                _busy = false;
                SendBtn.IsEnabled = true;
                Input.Focus();
            }
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

        void AddAssistant(string text, bool isResult, bool showCopiedNote = true)
        {
            var stack = new StackPanel();
            if (isResult)
            {
                var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var label = Text("Enhanced prompt", "AccentTextFillColorPrimaryBrush", 12);
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
            if (isResult && _settings.AutoCopy && showCopiedNote)
            {
                var note = Text("Copied to clipboard", "TextFillColorTertiaryBrush", 11);
                note.Margin = new Thickness(0, 8, 0, 0);
                stack.Children.Add(note);
            }
            AddWithFade(Card(stack));
        }

        void AddError(string message)
        {
            var row = new DockPanel();
            var g = Glyph("", "SystemFillColorCriticalBrush", 14);
            DockPanel.SetDock(g, Dock.Left);
            row.Children.Add(g);
            var body = new StackPanel();
            var title = Text("Couldn't enhance this prompt");
            title.FontWeight = FontWeights.SemiBold;
            body.Children.Add(title);
            body.Children.Add(Text(message, "TextFillColorSecondaryBrush", 12));
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
            foreach (var g in r.Good) Row("", "SystemFillColorSuccessBrush", g);
            foreach (var m in r.Missing) Row("", "SystemFillColorCautionBrush", m);
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
            var stack = new StackPanel();
            stack.Children.Add(Text("Enhancing prompt…", "TextFillColorSecondaryBrush", 12));
            stack.Children.Add(new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 8, 0, 2), Height = 4 });
            var card = Card(stack);
            AddWithFade(card);
            return card;
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
            if (MessageBox.Show(this, "Delete all saved prompts?", "promptuDX", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
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
            actions.Children.Add(Act("", "Copy enhanced prompt", () => CopyToClipboard(item.Enhanced)));
            actions.Children.Add(Act("", "Edit and resend original", () =>
            {
                HistoryPanel.Visibility = Visibility.Collapsed;
                Input.Text = item.Raw;
                Input.CaretIndex = Input.Text.Length;
                Input.Focus();
            }));
            actions.Children.Add(Act("", "Open in conversation", () =>
            {
                HistoryPanel.Visibility = Visibility.Collapsed;
                AddUser(item.Raw);
                AddAssistant(item.Enhanced, isResult: true, showCopiedNote: false);
            }));
            actions.Children.Add(Act("", "Delete", () => { HistoryStore.Remove(item); RenderHistory(); }));

            var head = new DockPanel();
            DockPanel.SetDock(actions, Dock.Right);
            head.Children.Add(actions);
            var time = Text(item.Time.ToString("MMM d, HH:mm"), "TextFillColorTertiaryBrush", 11);
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
