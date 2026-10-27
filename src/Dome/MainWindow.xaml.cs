using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Dome.Core;
using Dome.Services;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Dome;

public partial class MainWindow : Window
{
    private readonly UserSettings _settings = SettingsStore.Load();
    private readonly MediaService _media = new();
    private readonly AudioService _audio = new();
    private readonly BrightnessService _brightness = new();
    private readonly LyricsService _lyricsService = new();
    private readonly FocusClock _focus = new();
    private readonly DispatcherTimer _clock = new();
    private readonly DispatcherTimer _collapse = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly DispatcherTimer _hover = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _brightnessDebounce = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly DispatcherTimer _volumeOverlay = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Forms.NotifyIcon _tray = new();
    private CancellationTokenSource? _lyricCancellation;
    private IReadOnlyList<LyricLine> _lyricLines = Array.Empty<LyricLine>();
    private TrackInfo? _track;
    private bool _expanded, _ready, _syncControls, _seeking, _closing, _volumeNotice;
    private bool _trackBusy, _trackPending;
    private int _lyricIndex = -1, _ticks;
    private string _panel = "Music";
    private DateTimeOffset _lastWaterReminder = DateTimeOffset.Now;
    private HwndSource? _source;
    private const int HotkeyId = 0xD0;
    private bool _hotkeyRegistered;
    private bool _positioning;
    private int _brightnessReadVersion;
    private bool _brightnessWriting;
    private int? _pendingBrightness;

    public MainWindow()
    {
        InitializeComponent();
        _syncControls = true;
        HoverExpand.IsChecked = _settings.HoverExpand;
        WaterReminders.IsChecked = _settings.WaterReminders;
        LyricsSetting.IsChecked = _settings.LyricsEnabled;
        AutoStart.IsChecked = ReadAutoStart();
        _syncControls = false;
        ApplyTheme();
        SelectPanel("Music");
        RenderTasks(); RenderFiles(); UpdateWater();
        _collapse.Tick += (_, _) => { _collapse.Stop(); if (!IsMouseOver && !IsKeyboardFocusWithin) SetExpanded(false); };
        _hover.Tick += (_, _) => { _hover.Stop(); if (IsMouseOver) SetExpanded(true); };
        _brightnessDebounce.Tick += async (_, _) =>
        {
            _brightnessDebounce.Stop();
            _pendingBrightness = (int)BrightnessSlider.Value;
            if (_brightnessWriting) return;
            _brightnessWriting = true;
            try
            {
                // Slow DDC drivers should receive only the newest value, not a growing queue.
                while (_pendingBrightness is { } value && !_closing)
                {
                    _pendingBrightness = null;
                    await _brightness.SetAsync(value);
                }
            }
            catch (Exception e) { _pendingBrightness = null; Status("Brightness unavailable: " + e.Message); }
            finally { _brightnessWriting = false; }
        };
        _volumeOverlay.Tick += (_, _) => { _volumeOverlay.Stop(); _volumeNotice = false; UpdateCompact(); };
        _clock.Interval = TimeSpan.FromSeconds(1);
        _clock.Tick += (_, _) => Tick();
        _media.TrackChanged += () => Ui(() => _ = RefreshTrackAsync());
        _media.PlaybackChanged += () => Ui(UpdatePlayback);
        _audio.Changed += () => Ui(() =>
        {
            RefreshAudio();
            if (!_expanded && _ready)
            {
                MiniTitle.Text = "Volume  " + VolumeLabel.Text;
                MiniIcon.Text = MuteButton.Content?.ToString();
                _volumeNotice = true;
                _volumeOverlay.Stop(); _volumeOverlay.Start();
            }
        });
        Loaded += async (_, _) => await InitializeAsync();
        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source?.AddHook(WindowProc);
            _hotkeyRegistered = RegisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId, 0x4003, 0x20);
            if (!_hotkeyRegistered) Status("Shortcut already in use. Open Dome from the tray.");
        };
        SystemEvents.UserPreferenceChanged += ThemeChanged;
        SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        MouseRightButtonUp += (_, e) => { _tray.ContextMenuStrip?.Show(Forms.Cursor.Position); e.Handled = true; };
        SizeChanged += (_, _) => PositionIsland();
        Deactivated += (_, _) => { Keyboard.ClearFocus(); if (!IsMouseOver && _expanded) _collapse.Start(); };
        Closing += (_, _) => Cleanup();
    }

    private async Task InitializeAsync()
    {
        PositionIsland();
        CreateTray();
        _ready = true;
        _clock.Start();
        RefreshAudio();
        Tick();
        // Media access is independent of display-driver discovery.
        var media = StartMediaAsync();
        await ReadBrightnessAsync();
        await media;
    }
    private async Task StartMediaAsync()
    {
        try { await _media.StartAsync(); await RefreshTrackAsync(); }
        catch (Exception e) { TrackArtist.Text = "Windows media access unavailable."; Status(e.Message); }
    }
    private void Ui(Action action)
    {
        if (!_closing && !Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(new Action(() => { if (!_closing) action(); }));
    }
    private void CreateTray()
    {
        _tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application;
        _tray.Text = "Dome · your desktop, a little lighter";
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open / collapse   Ctrl+Alt+Space", null, (_, _) => Ui(() => SetExpanded(!_expanded)));
        menu.Items.Add("Music", null, (_, _) => Ui(() => { SelectPanel("Music"); SetExpanded(true); }));
        menu.Items.Add("System controls", null, (_, _) => Ui(() => { SelectPanel("Controls"); SetExpanded(true); }));
        menu.Items.Add("Settings", null, (_, _) => Ui(() => { SelectPanel("Settings"); SetExpanded(true); }));
        menu.Items.Add("Move to next display", null, (_, _) => Ui(NextDisplay));
        menu.Items.Add("Hide / show island", null, (_, _) => Ui(() => { if (IsVisible) Hide(); else Show(); }));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit Dome", null, (_, _) => Ui(Close));
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => Ui(() => SetExpanded(!_expanded));
        _tray.Visible = true;
    }
    private void Notify(string title, string text)
    {
        _tray.ShowBalloonTip(7000, title, text, Forms.ToolTipIcon.Info);
        Status(text);
    }

    private void Tick()
    {
        if (_closing) return;
        var now = DateTimeOffset.Now;
        MiniClock.Text = now.ToString("HH:mm");
        if (_focus.Tick(now)) Notify("A little space to breathe", "Focus complete. Stretch, look away, take a break.");
        FocusTime.Text = FormatTime(_focus.Remaining);
        FocusProgress.Value = 1 - _focus.Remaining.TotalSeconds / _focus.Duration.TotalSeconds;
        FocusStart.Content = _focus.Running ? "Pause" : _focus.Remaining < _focus.Duration && _focus.Remaining > TimeSpan.Zero ? "Resume" : "Start focus";
        UpdateWater();
        if (_settings.WaterReminders && now - _lastWaterReminder >= TimeSpan.FromHours(1) && now.Hour >= 8 && now.Hour < 22)
        {
            _lastWaterReminder = now;
            Notify("Time for a sip", "Take a water break. Add a drink from the Water tab.");
        }
        UpdatePlayback();
        if (_expanded)
        {
            var power = Forms.SystemInformation.PowerStatus;
            BatteryLabel.Text = power.BatteryChargeStatus.HasFlag(Forms.BatteryChargeStatus.NoSystemBattery)
                || power.BatteryLifePercent < 0 ? "DESKTOP" : $"{(int)(power.BatteryLifePercent * 100)}%" + (power.PowerLineStatus == Forms.PowerLineStatus.Online ? " · AC" : "");
            if (_panel == "Settings") MemoryLabel.Text = $"Working set: {Environment.WorkingSet / 1048576d:0.0} MB · native WPF, no web engine";
            if (_panel == "Controls" && ++_ticks % 8 == 0)
            {
                try { _audio.Reconnect(); RefreshAudio(); } catch { RefreshAudio(); }
            }
        }
    }
    private void UpdateCompact()
    {
        if (_volumeNotice) return;
        MiniTitle.Text = _focus.Running ? $"Focus · {FormatTime(_focus.Remaining)}"
            : !string.IsNullOrWhiteSpace(_track?.Title) ? _track.Title : "A little space for everything";
        MiniIcon.Text = _focus.Running ? "\uE916" : _track == null ? "\uE768" : "\uE8D6";
    }
    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1
        ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
        : $"{Math.Max(0, (int)value.TotalMinutes)}:{Math.Max(0, value.Seconds):00}";

    private async Task RefreshTrackAsync()
    {
        if (_closing) return;
        if (_trackBusy) { _trackPending = true; return; }
        _trackBusy = true;
        try
        {
            do
            {
                _trackPending = false;
                var track = await _media.ReadTrackAsync();
                if (_closing) return;
                var changed = _track == null || track?.Title != _track?.Title || track?.Artist != _track?.Artist || track?.Album != _track?.Album;
                _track = track;
                TrackTitle.Text = string.IsNullOrWhiteSpace(track?.Title) ? "Nothing playing. Yet." : track.Title;
                TrackArtist.Text = string.IsNullOrWhiteSpace(track?.Artist) ? "Play music in a Windows media-enabled player." : track.Artist;
                PlayerLabel.Text = track == null ? "WINDOWS MEDIA" : track.Player;
                Artwork.Source = null;
                if (track?.Thumbnail != null)
                {
                    try
                    {
                        using var stream = await track.Thumbnail.OpenReadAsync();
                        using var managed = stream.AsStreamForRead();
                        var image = new BitmapImage();
                        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 108;
                        image.StreamSource = managed; image.EndInit(); image.Freeze();
                        Artwork.Source = image;
                    }
                    catch { /* A missing/protected thumbnail does not prevent playback controls. */ }
                }
                UpdatePlayback();
                if (changed) _ = RefreshLyricsAsync();
            } while (_trackPending && !_closing);
        }
        catch (Exception e) { Status("Player did not share media information: " + e.Message); }
        finally { _trackBusy = false; }
    }
    private void UpdatePlayback()
    {
        try
        {
            var state = _media.ReadPlayback();
            var duration = state.End - state.Start;
            _syncControls = true;
            SeekSlider.Maximum = Math.Max(1, duration.TotalSeconds);
            SeekSlider.IsEnabled = state.CanSeek && duration > TimeSpan.Zero;
            if (!_seeking) SeekSlider.Value = Math.Max(0, (state.Position - state.Start).TotalSeconds);
            _syncControls = false;
            ElapsedLabel.Text = FormatTime(state.Position - state.Start);
            DurationLabel.Text = FormatTime(duration);
            PlayButton.Content = state.Playing ? "\uE769" : "\uE768";
            PlayButton.IsEnabled = state.CanPlay;
            NextButton.IsEnabled = state.CanNext; PreviousButton.IsEnabled = state.CanPrevious;
            UpdateLyricPosition(state.Position);
            _clock.Interval = TimeSpan.FromSeconds(state.Playing || _focus.Running || _expanded ? 1 : 30);
        }
        catch { _syncControls = false; PlayButton.IsEnabled = NextButton.IsEnabled = PreviousButton.IsEnabled = SeekSlider.IsEnabled = false; }
        UpdateCompact();
    }
    private async void Transport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!await _media.TransportAsync(((Button)sender).Tag.ToString()!)) Status("This player did not accept that control.");
            UpdatePlayback();
        }
        catch (Exception error) { Status(error.Message); }
    }
    private async Task SeekToAsync(TimeSpan position)
    {
        try { if (!await _media.SeekAsync(position)) Status("Seeking is not supported by this player."); }
        catch (Exception e) { Status(e.Message); }
        finally { _seeking = false; UpdatePlayback(); }
    }
    private async void Seek_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _syncControls) return;
        _seeking = true;
        if (!SeekSlider.IsMouseCaptureWithin)
        {
            try { await SeekToAsync(_media.ReadPlayback().Start + TimeSpan.FromSeconds(SeekSlider.Value)); }
            catch (Exception error) { _seeking = false; Status(error.Message); }
        }
    }
    private async void Seek_Released(object sender, MouseButtonEventArgs e)
    {
        if (!SeekSlider.IsEnabled) return;
        try { await SeekToAsync(_media.ReadPlayback().Start + TimeSpan.FromSeconds(SeekSlider.Value)); }
        catch (Exception error) { _seeking = false; Status(error.Message); }
    }

    private async Task RefreshLyricsAsync()
    {
        _lyricCancellation?.Cancel();
        _lyricCancellation?.Dispose();
        _lyricCancellation = new CancellationTokenSource();
        var cancellation = _lyricCancellation.Token;
        _lyricLines = Array.Empty<LyricLine>(); _lyricIndex = -1;
        PreviousLyric.Text = NextLyric.Text = "";
        LyricsButton.Content = _settings.LyricsEnabled ? "Refresh" : "Enable lyrics";
        CurrentLyric.Text = !_settings.LyricsEnabled ? "Lyrics stay off until you enable them."
            : _track == null ? "Your next song starts here." : "Finding the words…";
        LyricsCredit.Text = _settings.LyricsEnabled ? "Track lookup via LRCLIB" : "Optional lookup via LRCLIB. Track title and artist are sent.";
        if (!_settings.LyricsEnabled || _track == null) return;
        try
        {
            var state = _media.ReadPlayback();
            var result = await _lyricsService.LookupAsync(_track, state.End - state.Start, cancellation);
            if (cancellation.IsCancellationRequested || _closing) return;
            _lyricLines = result.Lines;
            LyricsCredit.Text = result.Message;
            CurrentLyric.Text = result.Lines.Count > 0 ? "♪" : string.IsNullOrWhiteSpace(result.Plain) ? result.Message : result.Plain;
            UpdateLyricPosition(state.Position);
        }
        catch (OperationCanceledException) { if (!cancellation.IsCancellationRequested) CurrentLyric.Text = "Lyrics request timed out. Tap Refresh to retry."; }
        catch (Exception) { if (!cancellation.IsCancellationRequested) CurrentLyric.Text = "Lyrics are offline right now. Tap Refresh to retry."; }
    }
    private void UpdateLyricPosition(TimeSpan position)
    {
        if (_lyricLines.Count == 0) return;
        var index = Lyrics.CurrentIndex(_lyricLines, position);
        if (index == _lyricIndex) return;
        _lyricIndex = index;
        PreviousLyric.Text = index > 0 ? _lyricLines[index - 1].Text : "";
        CurrentLyric.Text = index >= 0 ? string.IsNullOrWhiteSpace(_lyricLines[index].Text) ? "♪" : _lyricLines[index].Text : "♪";
        NextLyric.Text = index + 1 < _lyricLines.Count ? _lyricLines[index + 1].Text : "";
    }
    private async void LyricSeek_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lyricIndex >= 0 && _lyricIndex < _lyricLines.Count && _media.ReadPlayback().CanSeek)
                await SeekToAsync(_lyricLines[_lyricIndex].Time);
        }
        catch (Exception error) { Status(error.Message); }
    }
    private async void Lyrics_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.LyricsEnabled) { LyricsSetting.IsChecked = true; return; }
        await RefreshLyricsAsync();
    }
    private async void LyricsSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _syncControls) return;
        _settings.LyricsEnabled = LyricsSetting.IsChecked == true;
        Save(); await RefreshLyricsAsync();
    }

    private void RefreshAudio()
    {
        try
        {
            var (volume, muted) = _audio.Read();
            _syncControls = true; VolumeSlider.Value = volume * 100; _syncControls = false;
            VolumeLabel.Text = muted ? "Muted" : $"{volume * 100:0}%";
            MuteButton.Content = muted ? "\uE74F" : "\uE767";
            VolumeSlider.IsEnabled = MuteButton.IsEnabled = true;
            AudioLabel.Text = "Windows default multimedia output";
        }
        catch
        {
            _syncControls = false;
            VolumeSlider.IsEnabled = MuteButton.IsEnabled = false;
            VolumeLabel.Text = "Unavailable";
            AudioLabel.Text = "Connect an audio device, then reopen Controls.";
        }
    }
    private void Volume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _syncControls) return;
        try { _audio.SetVolume((float)(VolumeSlider.Value / 100)); VolumeLabel.Text = $"{VolumeSlider.Value:0}%"; }
        catch (Exception error) { Status(error.Message); }
    }
    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        try { _audio.ToggleMute(); RefreshAudio(); } catch (Exception error) { Status(error.Message); }
    }
    private async Task ReadBrightnessAsync()
    {
        var version = ++_brightnessReadVersion;
        _brightnessDebounce.Stop();
        _pendingBrightness = null;
        BrightnessSlider.IsEnabled = false;
        try
        {
            var screens = Forms.Screen.AllScreens;
            var bounds = screens[Math.Clamp(_settings.DisplayIndex, 0, screens.Length - 1)].Bounds;
            var result = await _brightness.ReadAsync(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            if (_closing || version != _brightnessReadVersion) return;
            var value = result.Value;
            _syncControls = true; BrightnessSlider.Value = value ?? 0; _syncControls = false;
            BrightnessSlider.IsEnabled = value.HasValue;
            BrightnessLabel.Text = value.HasValue ? $"{value}%" : "Unavailable";
            BrightnessHint.Text = result.Source;
        }
        catch
        {
            if (_closing || version != _brightnessReadVersion) return;
            BrightnessSlider.IsEnabled = false; BrightnessLabel.Text = "Unavailable";
            BrightnessHint.Text = "The display driver doesn't expose brightness control. Windows settings may offer an alternative.";
        }
    }
    private void Brightness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready || _syncControls) return;
        BrightnessLabel.Text = $"{BrightnessSlider.Value:0}%";
        _brightnessDebounce.Stop(); _brightnessDebounce.Start();
    }
    private void DisplaySettings_Click(object sender, RoutedEventArgs e) => Open("ms-settings:display");

    private void FocusPreset_Click(object sender, RoutedEventArgs e)
    {
        _focus.SetDuration(TimeSpan.FromMinutes(int.Parse(((Button)sender).Tag.ToString()!))); Tick();
    }
    private void FocusStart_Click(object sender, RoutedEventArgs e)
    {
        if (_focus.Running) _focus.Pause(DateTimeOffset.Now); else _focus.Start(DateTimeOffset.Now);
        Tick();
    }
    private void FocusReset_Click(object sender, RoutedEventArgs e) { _focus.Reset(); Tick(); }
    private void AddTask_Click(object sender, RoutedEventArgs e) => AddTask();
    private void TaskInput_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { AddTask(); e.Handled = true; } }
    private void AddTask()
    {
        var text = TaskInput.Text.Trim();
        if (text.Length == 0) return;
        if (_settings.Tasks.Count >= 30) { Status("Finish a task first. Keep this list small."); return; }
        _settings.Tasks.Add(text); TaskInput.Clear(); Save(); RenderTasks();
    }
    private void RenderTasks()
    {
        TaskList.Children.Clear();
        foreach (var text in _settings.Tasks.ToArray())
        {
            var row = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, Margin = new Thickness(4, 7, 4, 7), ToolTip = "Mark complete and remove" };
            row.Checked += (_, _) => { _settings.Tasks.Remove(text); Save(); RenderTasks(); };
            TaskList.Children.Add(row);
        }
    }
    private void UpdateWater()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (_settings.WaterDate != today) { _settings.WaterDate = today; _settings.Drinks.Clear(); Save(); }
        var amount = _settings.Drinks.Sum();
        WaterAmount.Text = $"{amount} / 2000 ml";
        WaterProgress.Value = amount;
    }
    private void WaterAdd_Click(object sender, RoutedEventArgs e)
    {
        UpdateWater();
        if (_settings.Drinks.Count >= 100) { Status("Daily drink log is full."); return; }
        _settings.Drinks.Add(int.Parse(((Button)sender).Tag.ToString()!)); Save(); UpdateWater();
    }
    private void WaterUndo_Click(object sender, RoutedEventArgs e)
    {
        UpdateWater();
        if (_settings.Drinks.Count == 0) return;
        _settings.Drinks.RemoveAt(_settings.Drinks.Count - 1); Save(); UpdateWater();
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) AddFiles(files);
    }
    private void ChooseFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Add shortcuts to your shelf" };
        if (dialog.ShowDialog(this) == true) AddFiles(dialog.FileNames);
    }
    private void AddFiles(IEnumerable<string> files)
    {
        foreach (var file in files)
            if (_settings.Files.Count < 30 && (File.Exists(file) || Directory.Exists(file)) && !_settings.Files.Contains(file, StringComparer.OrdinalIgnoreCase))
                _settings.Files.Add(file);
        Save(); RenderFiles(); SelectPanel("Shelf"); SetExpanded(true);
        Status("Shortcuts saved locally. Up to 30 files.");
    }
    private void RenderFiles()
    {
        FileList.Children.Clear();
        foreach (var file in _settings.Files.ToArray())
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
            var remove = new Button { Content = "×", Width = 35, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Remove shortcut" };
            remove.Click += (_, _) => { _settings.Files.Remove(file); Save(); RenderFiles(); };
            DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
            var open = new Button { Content = new TextBlock { Text = Path.GetFileName(file.TrimEnd(Path.DirectorySeparatorChar)), TextTrimming = TextTrimming.CharacterEllipsis }, ToolTip = file, HorizontalContentAlignment = HorizontalAlignment.Left };
            open.Click += (_, _) =>
            {
                if (File.Exists(file) || Directory.Exists(file)) Open(file); else Status("This file has moved or is no longer available.");
            };
            row.Children.Add(open); FileList.Children.Add(row);
        }
    }
    private void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception error) { Status("Couldn't open: " + error.Message); }
    }

    private void Header_Click(object sender, RoutedEventArgs e) => SetExpanded(!_expanded);
    private void SetExpanded(bool expanded)
    {
        if (expanded && !IsVisible) Show();
        _expanded = expanded;
        _hover.Stop(); _collapse.Stop();
        Width = expanded ? Math.Min(462, SystemParameters.WorkArea.Width - 16) : 310;
        ExpandedContent.Visibility = CollapseButton.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        if (expanded)
        {
            ExpandedContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            // Focus only follows an explicit click/hotkey; hover does not steal focus from other apps.
            Tick();
        }
        else Keyboard.ClearFocus();
        UpdatePlayback(); PositionIsland();
    }
    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        _collapse.Stop();
        if (!_expanded && _settings.HoverExpand) _hover.Start();
    }
    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        _hover.Stop();
        if (_expanded) _collapse.Start();
    }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { SetExpanded(false); e.Handled = true; }
    }
    private void Nav_Click(object sender, RoutedEventArgs e) => SelectPanel(((Button)sender).Tag.ToString()!);
    private void SelectPanel(string panel)
    {
        _panel = panel;
        foreach (var (name, view, button) in new[] {
            ("Music", MusicPanel, MusicNav), ("Controls", ControlsPanel, ControlsNav),
            ("Focus", FocusPanel, FocusNav), ("Water", WaterPanel, WaterNav),
            ("Shelf", ShelfPanel, ShelfNav), ("Settings", SettingsPanel, SettingsNav) })
        {
            view.Visibility = name == panel ? Visibility.Visible : Visibility.Collapsed;
            button.SetResourceReference(Control.BackgroundProperty, name == panel ? "Accent" : "Card");
            button.SetResourceReference(Control.ForegroundProperty, name == panel ? "AccentInk" : "Ink");
        }
        BodyScroll.ScrollToTop();
        if (_ready && panel == "Controls")
        {
            try { _audio.Reconnect(); } catch { }
            RefreshAudio(); _ = ReadBrightnessAsync();
        }
    }

    private void Settings_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _syncControls) return;
        _settings.HoverExpand = HoverExpand.IsChecked == true;
        _settings.WaterReminders = WaterReminders.IsChecked == true;
        _lastWaterReminder = DateTimeOffset.Now;
        Save();
    }
    private void Theme_Click(object sender, RoutedEventArgs e) { _settings.Theme = ((Button)sender).Tag.ToString()!; ApplyTheme(); Save(); }
    private void ThemeChanged(object sender, UserPreferenceChangedEventArgs e) => Ui(ApplyTheme);
    private void ApplyTheme()
    {
        var light = _settings.Theme == "Light";
        if (_settings.Theme == "System")
        {
            try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); light = key?.GetValue("AppsUseLightTheme") is int value && value != 0; }
            catch { light = false; }
        }
        var palette = light
            ? new[] { "#FAFAF7", "#EEEEEA", "#202720", "#626B62", "#D8DDD4", "#426B25", "#FFFFFF" }
            : new[] { "#151719", "#222528", "#F5F5F0", "#A6ADAA", "#353A36", "#C2EE91", "#1B2912" };
        var keys = new[] { "Surface", "Card", "Ink", "Muted", "Line", "Accent", "AccentInk" };
        for (var i = 0; i < keys.Length; i++)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette[i])); brush.Freeze();
            Application.Current.Resources[keys[i]] = brush;
        }
        ThemeLabel.Text = _settings.Theme == "System" ? "Following Windows appearance" : _settings.Theme + " appearance";
    }
    private static bool ReadAutoStart()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return key?.GetValue("Dome") != null; }
        catch { return false; }
    }
    private void AutoStart_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || _syncControls) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (AutoStart.IsChecked == true)
            {
                var path = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable.");
                if (!Path.GetFileName(path).Equals("Dome.exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Run the published Dome.exe before enabling autostart.");
                key.SetValue("Dome", "\"" + path + "\"");
            }
            else key.DeleteValue("Dome", false);
        }
        catch (Exception error)
        {
            _syncControls = true; AutoStart.IsChecked = ReadAutoStart(); _syncControls = false;
            Status("Autostart: " + error.Message);
        }
    }
    private void Save()
    {
        try { SettingsStore.Save(_settings); }
        catch (Exception e) { Status("Preferences couldn't be saved: " + e.Message); }
    }
    private void Status(string message) { StatusLabel.Text = message; StatusLabel.ToolTip = message; }
    private void DisplaysChanged(object? sender, EventArgs e) => Ui(PositionIsland);
    private void NextDisplay_Click(object sender, RoutedEventArgs e) => NextDisplay();
    private void NextDisplay()
    {
        _settings.DisplayIndex = (_settings.DisplayIndex + 1) % Forms.Screen.AllScreens.Length;
        Save(); PositionIsland();
        if (_panel == "Controls") _ = ReadBrightnessAsync();
    }
    private void PositionIsland()
    {
        if (_positioning || _source?.CompositionTarget == null) return;
        _positioning = true;
        try
        {
            var screens = Forms.Screen.AllScreens;
            var screen = screens[Math.Clamp(_settings.DisplayIndex, 0, screens.Length - 1)];
            var hwnd = new WindowInteropHelper(this).Handle;
            if (!GetWindowRect(hwnd, out var rect)) return;
            // Position the HWND in physical pixels; WPF virtual coordinates differ on mixed-DPI monitors.
            SetWindowPos(hwnd, IntPtr.Zero, screen.WorkingArea.Left + (screen.WorkingArea.Width - (rect.Right - rect.Left)) / 2,
                screen.WorkingArea.Top, 0, 0, 0x0015);
            var scale = Math.Max(1, GetDpiForWindow(hwnd)) / 96d;
            Width = Math.Max(240, Math.Min(_expanded ? 462 : 310, screen.WorkingArea.Width / scale - 16));
            BodyScroll.MaxHeight = Math.Max(100, Math.Min(490, screen.WorkingArea.Height / scale - 190));
        }
        finally { _positioning = false; }
    }
    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x02E0) Dispatcher.BeginInvoke(new Action(PositionIsland), DispatcherPriority.Background);
        if (message == 0x0312 && wParam.ToInt32() == HotkeyId)
        {
            SetExpanded(!_expanded);
            if (_expanded) Activate();
            handled = true;
        }
        return IntPtr.Zero;
    }
    private void Quit_Click(object sender, RoutedEventArgs e) => Close();
    private void Cleanup()
    {
        _closing = true;
        _clock.Stop(); _collapse.Stop(); _hover.Stop(); _brightnessDebounce.Stop(); _volumeOverlay.Stop();
        _lyricCancellation?.Cancel(); _lyricCancellation?.Dispose();
        SystemEvents.UserPreferenceChanged -= ThemeChanged;
        SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
        if (_hotkeyRegistered) UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId);
        _source?.RemoveHook(WindowProc);
        _media.Dispose(); _audio.Dispose(); _lyricsService.Dispose();
        _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Icon?.Dispose(); _tray.Dispose();
        Save();
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out WindowRect rectangle);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
}
