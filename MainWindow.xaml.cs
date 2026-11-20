using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Net.Http;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;

namespace WHIPPINMTA;

public partial class MainWindow : Window
{
    private string _color = "#FFFF4242";
    private string _outlineColor = "#FF000000";
    private string _preset = "classic";
    private OverlayWindow? _overlay;
    private bool _dragging;
    private static readonly HttpClient UpdateHttp = new();
    private UpdateManifest? _availableUpdate;
    private bool _checkingUpdate;
    private readonly System.Windows.Threading.DispatcherTimer _rainbowTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private int _rainbowIndex;
    private static readonly string[] RainbowColors = { "#FFFF334D", "#FFFF9F43", "#FFFFFF4D", "#FF43E879", "#FF43D5FF", "#FF617BFF", "#FFD66BFF" };
    private string ActiveCrosshairColor => RainbowCheck?.IsChecked == true ? RainbowColors[_rainbowIndex] : _color;
    private HwndSource? _hotkeySource;
    private const int HotkeyId = 1978;
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;
    private string _overlayHotkey = "F8";
    private sealed record UpdateManifest(string Version, string DownloadUrl, string Sha256);
    private sealed record Settings(double Size, double Thickness, double Gap, string Color, string Preset, bool Overlay, double OffsetX = 0, double OffsetY = 0, double Opacity = 1, bool OutlineEnabled = true, double OutlineThickness = 2, string OutlineColor = "#FF000000", string Hotkey = "F8", double LeftLength = 18, double RightLength = 18, double TopLength = 18, double BottomLength = 18, bool CenterDot = false, double CenterDotSize = 3, double Rotation = 0, bool ShowLeft = true, bool ShowRight = true, bool ShowTop = true, bool ShowBottom = true, bool GlowEnabled = false, double GlowStrength = 12, bool CenterRing = false, double CenterRingSize = 16, bool RainbowEnabled = false, double RainbowSpeed = 350);

    public MainWindow()
    {
        InitializeComponent();
        _rainbowTimer.Tick += (_, _) => { _rainbowIndex = (_rainbowIndex + 1) % RainbowColors.Length; DrawPreview(); };
        NavigateTo(0);
        LoadSettings();
        DrawPreview();
        RefreshProfiles();
        Loaded += async (_, _) =>
        {
            RegisterOverlayHotkey();
            if (RainbowCheck.IsChecked == true) Rainbow_Changed(this, new RoutedEventArgs());
            await CheckForUpdatesAsync(false);
        };
    }


    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !int.TryParse(button.Tag?.ToString(), out var index))
            return;
        NavigateTo(index);
    }

    private void NavigateTo(int index)
    {
        if (SettingsTabs is null) return;
        SettingsTabs.SelectedIndex = Math.Clamp(index, 0, 6);
        var buttons = new[] { NavHome, NavCrosshair, NavAdvanced, NavPosition, NavHotkeys, NavProfiles, NavAbout };
        for (var i = 0; i < buttons.Length; i++)
        {
            var selected = i == SettingsTabs.SelectedIndex;
            buttons[i].Background = selected
                ? new SolidColorBrush(Color.FromRgb(133, 10, 23))
                : Brushes.Transparent;
            buttons[i].BorderBrush = selected
                ? new SolidColorBrush(Color.FromRgb(255, 38, 56))
                : Brushes.Transparent;
            buttons[i].BorderThickness = selected ? new Thickness(1) : new Thickness(0);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private bool TryParseHotkey(string value, out uint modifiers, out uint virtualKey)
    {
        modifiers = MOD_NOREPEAT;
        virtualKey = 0;
        var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= MOD_CONTROL; break;
                case "alt": modifiers |= MOD_ALT; break;
                case "shift": modifiers |= MOD_SHIFT; break;
                case "win": case "windows": modifiers |= MOD_WIN; break;
                default: return false;
            }
        }

        if (!Enum.TryParse<Key>(parts[^1], true, out var key) || key is Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return false;
        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        return virtualKey != 0;
    }

    private bool RegisterOverlayHotkey()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _hotkeySource ??= HwndSource.FromHwnd(hwnd);
        _hotkeySource?.AddHook(HotkeyWndProc);
        if (!TryParseHotkey(_overlayHotkey, out var modifiers, out var virtualKey))
            return false;
        return RegisterHotKey(hwnd, HotkeyId, modifiers, virtualKey);
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }
        if (key == Key.Escape)
        {
            HotkeyBox.Text = _overlayHotkey;
            e.Handled = true;
            return;
        }

        var parts = new List<string>();
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(key.ToString());
        HotkeyBox.Text = string.Join("+", parts);
        e.Handled = true;
    }

    private void ApplyHotkey_Click(object sender, RoutedEventArgs e)
    {
        var candidate = HotkeyBox.Text.Trim();
        if (!TryParseHotkey(candidate, out _, out _))
        {
            StatusText.Text = "Geçerli bir klavye tuşu seç.";
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(hwnd, HotkeyId);
        var previous = _overlayHotkey;
        _overlayHotkey = candidate;
        if (!RegisterOverlayHotkey())
        {
            _overlayHotkey = previous;
            RegisterOverlayHotkey();
            HotkeyBox.Text = previous;
            StatusText.Text = "Bu kısayol kullanılamıyor; başka bir uygulama tarafından kullanılıyor olabilir.";
            return;
        }
        StatusText.Text = $"Overlay kısayolu {_overlayHotkey} olarak ayarlandı. Kalıcı olması için ayarları kaydet.";
    }

    private void ResetHotkey_Click(object sender, RoutedEventArgs e)
    {
        HotkeyBox.Text = "F8";
        ApplyHotkey_Click(sender, e);
    }

    private IntPtr HotkeyWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            OverlayCheck.IsChecked = OverlayCheck.IsChecked != true;
            handled = true;
        }
        return IntPtr.Zero;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(true);

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_checkingUpdate) return;
        _checkingUpdate = true;
        CheckUpdateButton.IsEnabled = false;
        if (manual) UpdateStatusText.Text = "Güncellemeler kontrol ediliyor...";
        try
        {
            UpdateHttp.DefaultRequestHeaders.UserAgent.ParseAdd("WHIPPINMTA-Updater/1.0");
            var manifestUrl = "https://github.com/memocann1978-bit/WHIPPINMTA/releases/download/latest/latest.json";
            var json = await UpdateHttp.GetStringAsync(manifestUrl);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null || !Version.TryParse(manifest.Version, out var latest))
                throw new InvalidDataException("Güncelleme bilgisi geçersiz.");
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
            if (latest > current)
            {
                _availableUpdate = manifest;
                InstallUpdateButton.Visibility = Visibility.Visible;
                UpdateStatusText.Text = $"Yeni sürüm bulundu: v{manifest.Version}";
            }
            else
            {
                _availableUpdate = null;
                InstallUpdateButton.Visibility = Visibility.Collapsed;
                UpdateStatusText.Text = manual ? "WHIPPINMTA güncel." : "Uygulama güncel.";
            }
        }
        catch
        {
            UpdateStatusText.Text = manual ? "Güncelleme kontrol edilemedi. İnternet bağlantını kontrol et." : "Güncelleme kontrolü yapılamadı.";
        }
        finally
        {
            _checkingUpdate = false;
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null) return;
        InstallUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "Güncelleme indiriliyor...";
        string? downloaded = null;
        try
        {
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(currentExe) || !System.IO.File.Exists(currentExe))
                throw new InvalidOperationException("Çalışan EXE yolu bulunamadı.");
            downloaded = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"WHIPPINMTA-{Guid.NewGuid():N}.exe");
            using var response = await UpdateHttp.GetAsync(_availableUpdate.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using (var fs = new System.IO.FileStream(downloaded, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.None))
                await response.Content.CopyToAsync(fs);

            await using var verifyStream = System.IO.File.OpenRead(downloaded);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(verifyStream)).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(hash),
                System.Text.Encoding.ASCII.GetBytes(_availableUpdate.Sha256.ToLowerInvariant())))
                throw new InvalidDataException("İndirilen dosyanın SHA-256 doğrulaması başarısız.");

            static string PsQuote(string value) => "'" + value.Replace("'", "''") + "'";
            var scriptPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"whippin-update-{Guid.NewGuid():N}.ps1");
            var pid = Environment.ProcessId;
            var script = $@"$ErrorActionPreference = 'Stop'
$target = {PsQuote(currentExe)}
$source = {PsQuote(downloaded)}
$processId = {pid}
$log = Join-Path $env:TEMP 'WHIPPINMTA-update.log'
try {{
  while (Get-Process -Id $processId -ErrorAction SilentlyContinue) {{ Start-Sleep -Milliseconds 500 }}
  Copy-Item -LiteralPath $source -Destination $target -Force
  Start-Process -FilePath $target
  Remove-Item -LiteralPath $source -Force -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
}} catch {{
  $_ | Out-String | Set-Content -LiteralPath $log
}}";
            await System.IO.File.WriteAllTextAsync(scriptPath, script);
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            UpdateStatusText.Text = "Güncelleme uygulanıyor, uygulama yeniden açılacak...";
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            if (downloaded is not null) try { System.IO.File.Delete(downloaded); } catch { }
            UpdateStatusText.Text = "Güncelleme başarısız: " + ex.Message;
            InstallUpdateButton.IsEnabled = true;
        }
    }

    private void LoadSettings()
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "whippinmta-settings.json");
            if (!System.IO.File.Exists(path)) return;
            var s = JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(path));
            if (s is null) return;
            SizeSlider.Value = Math.Clamp(s.Size, SizeSlider.Minimum, SizeSlider.Maximum);
            ThicknessSlider.Value = Math.Clamp(s.Thickness, ThicknessSlider.Minimum, ThicknessSlider.Maximum);
            GapSlider.Value = Math.Clamp(s.Gap, GapSlider.Minimum, GapSlider.Maximum);
            LeftLengthSlider.Value = Math.Clamp(s.LeftLength, LeftLengthSlider.Minimum, LeftLengthSlider.Maximum);
            RightLengthSlider.Value = Math.Clamp(s.RightLength, RightLengthSlider.Minimum, RightLengthSlider.Maximum);
            TopLengthSlider.Value = Math.Clamp(s.TopLength, TopLengthSlider.Minimum, TopLengthSlider.Maximum);
            BottomLengthSlider.Value = Math.Clamp(s.BottomLength, BottomLengthSlider.Minimum, BottomLengthSlider.Maximum);
            CenterDotCheck.IsChecked = s.CenterDot; CenterDotSizeSlider.Value = Math.Clamp(s.CenterDotSize, 1, 12); RotationSlider.Value = Math.Clamp(s.Rotation, -180, 180);
            ShowLeftCheck.IsChecked = s.ShowLeft; ShowRightCheck.IsChecked = s.ShowRight; ShowTopCheck.IsChecked = s.ShowTop; ShowBottomCheck.IsChecked = s.ShowBottom;
            GlowCheck.IsChecked = s.GlowEnabled; GlowStrengthSlider.Value = Math.Clamp(s.GlowStrength, 0, 30); CenterRingCheck.IsChecked = s.CenterRing; CenterRingSizeSlider.Value = Math.Clamp(s.CenterRingSize, 4, 60);
            RainbowCheck.IsChecked = s.RainbowEnabled; RainbowSpeedSlider.Value = Math.Clamp(s.RainbowSpeed, 80, 1200);
            _color = s.Color;
            _outlineColor = s.OutlineColor;
            if (CustomColorBox is not null) CustomColorBox.Text = _color;
            if (CustomOutlineColorBox is not null) CustomOutlineColorBox.Text = _outlineColor;
            if (OpacitySlider is not null) OpacitySlider.Value = Math.Clamp(s.Opacity, 0.1, 1.0);
            OutlineCheck.IsChecked = s.OutlineEnabled;
            OutlineThicknessSlider.Value = Math.Clamp(s.OutlineThickness, 1, 8);
            _preset = s.Preset;
            _overlayHotkey = string.IsNullOrWhiteSpace(s.Hotkey) ? "F8" : s.Hotkey;
            if (HotkeyBox is not null) HotkeyBox.Text = _overlayHotkey;
            OffsetXSlider.Value = Math.Clamp(s.OffsetX, OffsetXSlider.Minimum, OffsetXSlider.Maximum);
            OffsetYSlider.Value = Math.Clamp(s.OffsetY, OffsetYSlider.Minimum, OffsetYSlider.Maximum);
            OverlayCheck.IsChecked = s.Overlay;
        }
        catch { StatusText.Text = "Kayıtlı ayarlar yüklenemedi; varsayılanlar kullanılıyor."; }
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        _preset = (string)((Button)sender).Tag;
        if (_preset == "dot") { SizeSlider.Value = 6; GapSlider.Value = 0; }
        else if (_preset == "precision") { SizeSlider.Value = 24; GapSlider.Value = 7; ThicknessSlider.Value = 1; }
        else if (_preset == "minimal") { SizeSlider.Value = 14; GapSlider.Value = 4; }
        else if (_preset == "circle") { SizeSlider.Value = 24; GapSlider.Value = 4; }
        else { SizeSlider.Value = 18; GapSlider.Value = 5; }
        DrawPreview();
    }

    private static string NormalizeHexColor(string value)
    {
        var parsed = (Color?)ColorConverter.ConvertFromString(value.Trim());
        if (parsed is null) throw new FormatException("Renk kodu geçersiz.");
        return parsed.Value.ToString();
    }

    private void ApplyCustomColor_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _color = NormalizeHexColor(CustomColorBox.Text);
            CustomColorBox.Text = _color;
            DrawPreview();
            StatusText.Text = "Crosshair özel rengi uygulandı.";
        }
        catch { StatusText.Text = "Geçersiz renk. #RRGGBB veya #AARRGGBB kullan."; }
    }

    private void ApplyCustomOutlineColor_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _outlineColor = NormalizeHexColor(CustomOutlineColorBox.Text);
            CustomOutlineColorBox.Text = _outlineColor;
            DrawPreview();
            StatusText.Text = "Dış çizgi özel rengi uygulandı.";
        }
        catch { StatusText.Text = "Geçersiz renk. #RRGGBB veya #AARRGGBB kullan."; }
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        _color = NormalizeHexColor((string)((Button)sender).Tag);
        CustomColorBox.Text = _color;
        DrawPreview();
    }

    private void OutlineColor_Click(object sender, RoutedEventArgs e)
    {
        _outlineColor = NormalizeHexColor((string)((Button)sender).Tag);
        CustomOutlineColorBox.Text = _outlineColor;
        DrawPreview();
    }
    private void Setting_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        if (ReferenceEquals(sender, RainbowSpeedSlider) && _rainbowTimer.IsEnabled)
            _rainbowTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(RainbowSpeedSlider.Value, 80, 1200));
        if (ReferenceEquals(sender, SizeSlider))
        {
            LeftLengthSlider.Value = RightLengthSlider.Value = TopLengthSlider.Value = BottomLengthSlider.Value = SizeSlider.Value;
        }
        DrawPreview();
    }

    private void EqualizeArms_Click(object sender, RoutedEventArgs e)
    {
        var length = (LeftLengthSlider.Value + RightLengthSlider.Value + TopLengthSlider.Value + BottomLengthSlider.Value) / 4;
        LeftLengthSlider.Value = RightLengthSlider.Value = TopLengthSlider.Value = BottomLengthSlider.Value = length;
        SizeSlider.Value = length;
        StatusText.Text = "Dört kol eşit uzunluğa getirildi.";
    }
    private void SettingToggle_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) DrawPreview(); }
    private void Rainbow_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (RainbowCheck.IsChecked == true)
        {
            _rainbowTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(RainbowSpeedSlider.Value, 80, 1200));
            _rainbowTimer.Start();
            StatusText.Text = "Rainbow renk döngüsü etkin.";
        }
        else
        {
            _rainbowTimer.Stop();
            StatusText.Text = "Rainbow renk döngüsü kapatıldı.";
        }
        DrawPreview();
    }
    private void Position_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        OffsetXText.Text = ((int)OffsetXSlider.Value).ToString();
        OffsetYText.Text = ((int)OffsetYSlider.Value).ToString();
        DrawPreview();
    }
    private void Center_Click(object sender, RoutedEventArgs e) { OffsetXSlider.Value = 0; OffsetYSlider.Value = 0; }

    private void Preview_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        PreviewCanvas.CaptureMouse();
        SetPositionFromPreview(e.GetPosition(PreviewCanvas));
    }
    private void Preview_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging && e.LeftButton == MouseButtonState.Pressed) SetPositionFromPreview(e.GetPosition(PreviewCanvas));
    }
    private void Preview_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        PreviewCanvas.ReleaseMouseCapture();
    }
    private void SetPositionFromPreview(Point p)
    {
        double cx = PreviewCanvas.ActualWidth / 2;
        double cy = PreviewCanvas.ActualHeight / 2;
        OffsetXSlider.Value = Math.Clamp(p.X - cx, OffsetXSlider.Minimum, OffsetXSlider.Maximum);
        OffsetYSlider.Value = Math.Clamp(p.Y - cy, OffsetYSlider.Minimum, OffsetYSlider.Maximum);
    }

    private void DrawPreview()
    {
        if (PreviewCanvas is null) return;
        PreviewCanvas.Children.Clear();
        double cx = PreviewCanvas.ActualWidth / 2, cy = PreviewCanvas.ActualHeight / 2;
        if (cx < 1) { PreviewCanvas.Loaded += (s, e) => DrawPreview(); return; }
        AddCross(PreviewCanvas, cx + OffsetXSlider.Value, cy + OffsetYSlider.Value, SizeSlider.Value, ThicknessSlider.Value, GapSlider.Value, ActiveCrosshairColor, _preset, OpacitySlider.Value, OutlineCheck.IsChecked == true, OutlineThicknessSlider.Value, _outlineColor, LeftLengthSlider.Value, RightLengthSlider.Value, TopLengthSlider.Value, BottomLengthSlider.Value, CenterDotCheck.IsChecked == true, CenterDotSizeSlider.Value, RotationSlider.Value, ShowLeftCheck.IsChecked == true, ShowRightCheck.IsChecked == true, ShowTopCheck.IsChecked == true, ShowBottomCheck.IsChecked == true, GlowCheck.IsChecked == true, GlowStrengthSlider.Value, CenterRingCheck.IsChecked == true, CenterRingSizeSlider.Value, RainbowCheck.IsChecked == true, RainbowSpeedSlider.Value);
        UpdateOverlay();
    }

    internal static void AddCross(Canvas c, double x, double y, double size, double thick, double gap, string color, string preset, double opacity = 1, bool outlineEnabled = true, double outlineThickness = 2, string outlineColor = "#FF000000", double leftLength = -1, double rightLength = -1, double topLength = -1, double bottomLength = -1, bool centerDot = false, double centerDotSize = 3, double rotation = 0, bool showLeft = true, bool showRight = true, bool showTop = true, bool showBottom = true, bool glowEnabled = false, double glowStrength = 12, bool centerRing = false, double centerRingSize = 16)
    {
        var brush = (Brush)new BrushConverter().ConvertFromString(color)!;
        var outlineBrush = (Brush)new BrushConverter().ConvertFromString(outlineColor)!;
        double outer = Math.Max(0, outlineThickness) * 2;
        leftLength = leftLength < 0 ? size : leftLength;
        rightLength = rightLength < 0 ? size : rightLength;
        topLength = topLength < 0 ? size : topLength;
        bottomLength = bottomLength < 0 ? size : bottomLength;
        Point RotatePoint(double px, double py)
        {
            double radians = rotation * Math.PI / 180.0;
            double dx = px - x, dy = py - y;
            return new Point(x + dx * Math.Cos(radians) - dy * Math.Sin(radians), y + dx * Math.Sin(radians) + dy * Math.Cos(radians));
        }
        void DrawLine(double x1, double y1, double x2, double y2, Brush stroke, double strokeWidth)
        {
            var p1 = RotatePoint(x1, y1); var p2 = RotatePoint(x2, y2);
            x1 = p1.X; y1 = p1.Y; x2 = p2.X; y2 = p2.Y;
            var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, Opacity = opacity, StrokeThickness = strokeWidth, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Effect = glowEnabled ? new System.Windows.Media.Effects.DropShadowEffect { Color = ((SolidColorBrush)brush).Color, BlurRadius = glowStrength, ShadowDepth = 0, Opacity = 0.9 } : null };
            c.Children.Add(line);
        }
        void Line(double x1, double y1, double x2, double y2)
        {
            if (outlineEnabled && outer > 0) DrawLine(x1, y1, x2, y2, outlineBrush, thick + outer);
            DrawLine(x1, y1, x2, y2, brush, thick);
        }
        void EllipseShape(double left, double top, double width, double height, bool filled)
        {
            if (outlineEnabled && outer > 0)
            {
                var back = new Ellipse { Width = filled ? width + outer : width, Height = filled ? height + outer : height, Opacity = opacity };
                if (filled) back.Fill = outlineBrush;
                else { back.Stroke = outlineBrush; back.StrokeThickness = thick + outer; }
                Canvas.SetLeft(back, filled ? left - outer / 2 : left); Canvas.SetTop(back, filled ? top - outer / 2 : top); c.Children.Add(back);
            }
            var shape = new Ellipse { Width = width, Height = height, Opacity = opacity };
            if (filled) shape.Fill = brush; else { shape.Stroke = brush; shape.StrokeThickness = thick; }
            Canvas.SetLeft(shape, left); Canvas.SetTop(shape, top); c.Children.Add(shape);
        }
        if (preset == "circle") EllipseShape(x - size / 2, y - size / 2, size, size, false);
        else if (preset == "dot") { double d = size / 3; EllipseShape(x - d / 2, y - d / 2, d, d, true); }
        else if (preset == "square")
        {
            double h = size / 2;
            Line(x-h,y-h,x+h,y-h); Line(x+h,y-h,x+h,y+h); Line(x+h,y+h,x-h,y+h); Line(x-h,y+h,x-h,y-h);
        }
        else if (preset == "diamond")
        {
            double h = size / 2;
            Line(x,y-h,x+h,y); Line(x+h,y,x,y+h); Line(x,y+h,x-h,y); Line(x-h,y,x,y-h);
        }
        else if (preset == "chevron")
        {
            if (showLeft) Line(x-leftLength,y-size*0.25,x-gap,y);
            if (showRight) Line(x+gap,y,x+rightLength,y-size*0.25);
            if (showBottom) { Line(x-leftLength*0.55,y-size*0.25,x,y+size*0.35); Line(x,y+size*0.35,x+rightLength*0.55,y-size*0.25); }
        }
        else if (preset == "tshape")
        {
            if (showTop) Line(x-topLength,y-gap,x+rightLength,y-gap);
            if (showBottom) Line(x,y-gap,x,y+bottomLength);
        }
        else if (preset == "brackets")
        {
            if (showLeft) { Line(x-leftLength,y-size/2,x-leftLength,y+size/2); Line(x-leftLength,y-size/2,x-gap,y-size/2); Line(x-leftLength,y+size/2,x-gap,y+size/2); }
            if (showRight) { Line(x+rightLength,y-size/2,x+rightLength,y+size/2); Line(x+gap,y-size/2,x+rightLength,y-size/2); Line(x+gap,y+size/2,x+rightLength,y+size/2); }
            if (showTop) Line(x,y-topLength,x,y-gap);
            if (showBottom) Line(x,y+gap,x,y+bottomLength);
        }
        else if (preset == "star")
        {
            double h = size / 2, d = h * 0.38;
            Line(x-h,y,x+h,y); Line(x,y-h,x,y+h); Line(x-d,y-d,x+d,y+d); Line(x-d,y+d,x+d,y-d);
        }
        else
        {
            if (showLeft) Line(x - leftLength, y, x - gap, y);
            if (showRight) Line(x + gap, y, x + rightLength, y);
            if (showTop) Line(x, y - topLength, x, y - gap);
            if (showBottom) Line(x, y + gap, x, y + bottomLength);
        }
        if (centerRing)
        {
            double ring = Math.Max(4, centerRingSize);
            EllipseShape(x - ring / 2, y - ring / 2, ring, ring, false);
        }
        if (centerDot)
        {
            double d = Math.Max(1, centerDotSize);
            EllipseShape(x - d / 2, y - d / 2, d, d, true);
        }
    }

    private void Overlay_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (OverlayCheck.IsChecked == true) { _overlay ??= new OverlayWindow(); _overlay.Show(); UpdateOverlay(); }
        else { _overlay?.Close(); _overlay = null; }
    }
    private void UpdateOverlay() => _overlay?.UpdateCross(SizeSlider.Value, ThicknessSlider.Value, GapSlider.Value, ActiveCrosshairColor, _preset, OffsetXSlider.Value, OffsetYSlider.Value, OpacitySlider.Value, OutlineCheck.IsChecked == true, OutlineThicknessSlider.Value, _outlineColor, LeftLengthSlider.Value, RightLengthSlider.Value, TopLengthSlider.Value, BottomLengthSlider.Value, CenterDotCheck.IsChecked == true, CenterDotSizeSlider.Value, RotationSlider.Value, ShowLeftCheck.IsChecked == true, ShowRightCheck.IsChecked == true, ShowTopCheck.IsChecked == true, ShowBottomCheck.IsChecked == true, GlowCheck.IsChecked == true, GlowStrengthSlider.Value, CenterRingCheck.IsChecked == true, CenterRingSizeSlider.Value);

    private string ProfilesPath => System.IO.Path.Combine(AppContext.BaseDirectory, "whippinmta-profiles.json");

    private Settings CaptureSettings() => new Settings(SizeSlider.Value, ThicknessSlider.Value, GapSlider.Value, _color, _preset, OverlayCheck.IsChecked == true, OffsetXSlider.Value, OffsetYSlider.Value, OpacitySlider.Value, OutlineCheck.IsChecked == true, OutlineThicknessSlider.Value, _outlineColor, _overlayHotkey, LeftLengthSlider.Value, RightLengthSlider.Value, TopLengthSlider.Value, BottomLengthSlider.Value, CenterDotCheck.IsChecked == true, CenterDotSizeSlider.Value, RotationSlider.Value, ShowLeftCheck.IsChecked == true, ShowRightCheck.IsChecked == true, ShowTopCheck.IsChecked == true, ShowBottomCheck.IsChecked == true, GlowCheck.IsChecked == true, GlowStrengthSlider.Value, CenterRingCheck.IsChecked == true, CenterRingSizeSlider.Value);

    private void RefreshProfiles(string? selectName = null)
    {
        try
        {
            Dictionary<string, Settings> profiles = System.IO.File.Exists(ProfilesPath)
                ? JsonSerializer.Deserialize<Dictionary<string, Settings>>(System.IO.File.ReadAllText(ProfilesPath)) ?? new()
                : new();
            ProfileCombo.ItemsSource = profiles.Keys.OrderBy(k => k).ToList();
            if (!string.IsNullOrWhiteSpace(selectName)) ProfileCombo.SelectedItem = selectName;
        }
        catch { StatusText.Text = "Profil listesi okunamadı."; }
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = ProfileNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { StatusText.Text = "Önce profil adı yaz."; return; }
        try
        {
            Dictionary<string, Settings> profiles = System.IO.File.Exists(ProfilesPath)
                ? JsonSerializer.Deserialize<Dictionary<string, Settings>>(System.IO.File.ReadAllText(ProfilesPath)) ?? new()
                : new();
            profiles[name] = CaptureSettings();
            System.IO.File.WriteAllText(ProfilesPath, JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
            RefreshProfiles(name);
            StatusText.Text = $"'{name}' profili kaydedildi.";
        }
        catch (Exception ex) { StatusText.Text = "Profil kaydedilemedi: " + ex.Message; }
    }

    private void LoadProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not string name) { StatusText.Text = "Yüklenecek bir profil seç."; return; }
        try
        {
            var profiles = JsonSerializer.Deserialize<Dictionary<string, Settings>>(System.IO.File.ReadAllText(ProfilesPath)) ?? new();
            if (!profiles.TryGetValue(name, out var s)) { StatusText.Text = "Profil bulunamadı."; RefreshProfiles(); return; }
            SizeSlider.Value = Math.Clamp(s.Size, SizeSlider.Minimum, SizeSlider.Maximum);
            ThicknessSlider.Value = Math.Clamp(s.Thickness, ThicknessSlider.Minimum, ThicknessSlider.Maximum);
            GapSlider.Value = Math.Clamp(s.Gap, GapSlider.Minimum, GapSlider.Maximum);
            LeftLengthSlider.Value = Math.Clamp(s.LeftLength, LeftLengthSlider.Minimum, LeftLengthSlider.Maximum);
            RightLengthSlider.Value = Math.Clamp(s.RightLength, RightLengthSlider.Minimum, RightLengthSlider.Maximum);
            TopLengthSlider.Value = Math.Clamp(s.TopLength, TopLengthSlider.Minimum, TopLengthSlider.Maximum);
            BottomLengthSlider.Value = Math.Clamp(s.BottomLength, BottomLengthSlider.Minimum, BottomLengthSlider.Maximum);
            CenterDotCheck.IsChecked = s.CenterDot; CenterDotSizeSlider.Value = Math.Clamp(s.CenterDotSize, 1, 12); RotationSlider.Value = Math.Clamp(s.Rotation, -180, 180);
            ShowLeftCheck.IsChecked = s.ShowLeft; ShowRightCheck.IsChecked = s.ShowRight; ShowTopCheck.IsChecked = s.ShowTop; ShowBottomCheck.IsChecked = s.ShowBottom;
            GlowCheck.IsChecked = s.GlowEnabled; GlowStrengthSlider.Value = Math.Clamp(s.GlowStrength, 0, 30); CenterRingCheck.IsChecked = s.CenterRing; CenterRingSizeSlider.Value = Math.Clamp(s.CenterRingSize, 4, 60);
            RainbowCheck.IsChecked = s.RainbowEnabled; RainbowSpeedSlider.Value = Math.Clamp(s.RainbowSpeed, 80, 1200);
            OpacitySlider.Value = Math.Clamp(s.Opacity, 0.1, 1.0);
            OutlineCheck.IsChecked = s.OutlineEnabled;
            OutlineThicknessSlider.Value = Math.Clamp(s.OutlineThickness, 1, 8);
            _color = s.Color; _outlineColor = s.OutlineColor; _preset = s.Preset;
            CustomColorBox.Text = _color;
            CustomOutlineColorBox.Text = _outlineColor;
            OffsetXSlider.Value = Math.Clamp(s.OffsetX, OffsetXSlider.Minimum, OffsetXSlider.Maximum);
            OffsetYSlider.Value = Math.Clamp(s.OffsetY, OffsetYSlider.Minimum, OffsetYSlider.Maximum);
            OverlayCheck.IsChecked = s.Overlay;
            if (!string.IsNullOrWhiteSpace(s.Hotkey))
            {
                _overlayHotkey = s.Hotkey;
                HotkeyBox.Text = _overlayHotkey;
                var hwnd = new WindowInteropHelper(this).Handle;
                UnregisterHotKey(hwnd, HotkeyId);
                if (!RegisterOverlayHotkey()) StatusText.Text = "Kayıtlı kısayol kullanılamıyor; yeni bir tuş seç.";
            }
            Rainbow_Changed(this, new RoutedEventArgs());
            DrawPreview();
            StatusText.Text = $"'{name}' profili yüklendi.";
        }
        catch (Exception ex) { StatusText.Text = "Profil yüklenemedi: " + ex.Message; }
    }

    private void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not string oldName)
        {
            StatusText.Text = "Yeniden adlandırmak için bir profil seç.";
            return;
        }

        var newName = ProfileNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            StatusText.Text = "Yeni profil adını yaz.";
            return;
        }
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            StatusText.Text = "Profil adı zaten aynı.";
            return;
        }

        try
        {
            var profiles = System.IO.File.Exists(ProfilesPath)
                ? JsonSerializer.Deserialize<Dictionary<string, Settings>>(System.IO.File.ReadAllText(ProfilesPath)) ?? new()
                : new();
            if (!profiles.ContainsKey(oldName))
            {
                StatusText.Text = "Seçili profil bulunamadı.";
                RefreshProfiles();
                return;
            }
            if (profiles.ContainsKey(newName))
            {
                StatusText.Text = "Bu isimde bir profil zaten var.";
                return;
            }

            profiles[newName] = profiles[oldName];
            profiles.Remove(oldName);
            System.IO.File.WriteAllText(ProfilesPath, JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
            RefreshProfiles(newName);
            StatusText.Text = $"'{oldName}' profili '{newName}' olarak yeniden adlandırıldı.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Profil yeniden adlandırılamadı: " + ex.Message;
        }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not string name)
        {
            StatusText.Text = "Silmek için bir profil seç.";
            return;
        }

        var answer = MessageBox.Show(
            $"'{name}' profili kalıcı olarak silinsin mi?",
            "WHIPPINMTA — Profil sil",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            var profiles = JsonSerializer.Deserialize<Dictionary<string, Settings>>(System.IO.File.ReadAllText(ProfilesPath)) ?? new();
            if (!profiles.Remove(name))
            {
                StatusText.Text = "Profil bulunamadı.";
                RefreshProfiles();
                return;
            }
            System.IO.File.WriteAllText(ProfilesPath, JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
            ProfileNameBox.Text = name;
            RefreshProfiles();
            StatusText.Text = $"'{name}' profili silindi.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Profil silinemedi: " + ex.Message;
        }
    }


    private void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not string name) { StatusText.Text = "Dışa aktarmak için bir profil seç."; return; }
        try
        {
            var profiles = JsonSerializer.Deserialize<Dictionary<string, Settings>>(System.IO.File.ReadAllText(ProfilesPath)) ?? new();
            if (!profiles.TryGetValue(name, out var settings)) { StatusText.Text = "Profil bulunamadı."; return; }
            var dialog = new SaveFileDialog { Title = "WHIPPINMTA profilini dışa aktar", Filter = "WHIPPINMTA Profil (*.whipprofile.json)|*.whipprofile.json|JSON dosyası (*.json)|*.json", FileName = name.Replace(" ", "_") + ".whipprofile.json", AddExtension = true, DefaultExt = ".whipprofile.json" };
            if (dialog.ShowDialog(this) != true) return;
            System.IO.File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            StatusText.Text = $"'{name}' profili dosyaya aktarıldı.";
        }
        catch (Exception ex) { StatusText.Text = "Dışa aktarma hatası: " + ex.Message; }
    }

    private void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "WHIPPINMTA profili içe aktar", Filter = "WHIPPINMTA Profil (*.whipprofile.json;*.json)|*.whipprofile.json;*.json|Tüm dosyalar (*.*)|*.*", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(dialog.FileName));
            if (settings is null || string.IsNullOrWhiteSpace(settings.Color) || string.IsNullOrWhiteSpace(settings.Preset))
                throw new InvalidDataException("Dosya geçerli bir WHIPPINMTA profili değil.");
            var name = ProfileNameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name) || name == "Yeni profil")
                name = System.IO.Path.GetFileNameWithoutExtension(System.IO.Path.GetFileNameWithoutExtension(dialog.FileName));
            if (string.IsNullOrWhiteSpace(name)) name = "İçe aktarılan profil";
            var profiles = System.IO.File.Exists(ProfilesPath) ? JsonSerializer.Deserialize<Dictionary<string, Settings>>(System.IO.File.ReadAllText(ProfilesPath)) ?? new() : new();
            if (profiles.ContainsKey(name))
            {
                var answer = MessageBox.Show($"'{name}' zaten var. Üzerine yazılsın mı?", "Profil çakışması", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
            }
            profiles[name] = settings;
            System.IO.File.WriteAllText(ProfilesPath, JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
            ProfileNameBox.Text = name;
            RefreshProfiles(name);
            StatusText.Text = $"'{name}' profili içe aktarıldı. Yüklemek için profili seçip yükle.";
        }
        catch (Exception ex) { StatusText.Text = "İçe aktarma başarısız: " + ex.Message; }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var s = CaptureSettings() with { Hotkey = _overlayHotkey };
            System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "whippinmta-settings.json"), JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
            StatusText.Text = $"Ayarlar, konum ve {_overlayHotkey} kısayolu kaydedildi.";
        }
        catch (Exception ex) { StatusText.Text = "Kaydetme hatası: " + ex.Message; }
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
        {
            if (WindowState == WindowState.Maximized) return;
            DragMove();
        }
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeWindow_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(hwnd, HotkeyId);
        _hotkeySource?.RemoveHook(HotkeyWndProc);
        _rainbowTimer.Stop();
        _overlay?.Close();
        base.OnClosed(e);
    }
}

public sealed class OverlayWindow : Window
{
    private readonly Canvas _canvas = new();

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public OverlayWindow()
    {
        ShowActivated = false;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.VirtualScreenLeft; Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth; Height = SystemParameters.VirtualScreenHeight;
        IsHitTestVisible = false; Content = _canvas;
        SourceInitialized += (s, e) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            style |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style));
        };
        Loaded += (s, e) => UpdateCross(18, 2, 5, "#FFFF4242", "classic", 0, 0);
    }
    public void UpdateCross(double size, double thick, double gap, string color, string preset, double offsetX, double offsetY, double opacity = 1, bool outlineEnabled = true, double outlineThickness = 2, string outlineColor = "#FF000000", double leftLength = -1, double rightLength = -1, double topLength = -1, double bottomLength = -1, bool centerDot = false, double centerDotSize = 3, double rotation = 0, bool showLeft = true, bool showRight = true, bool showTop = true, bool showBottom = true, bool glowEnabled = false, double glowStrength = 12, bool centerRing = false, double centerRingSize = 16)
    {
        _canvas.Children.Clear();
        MainWindow.AddCross(_canvas, Width / 2 + offsetX, Height / 2 + offsetY, size, thick, gap, color, preset, opacity, outlineEnabled, outlineThickness, outlineColor, leftLength, rightLength, topLength, bottomLength, centerDot, centerDotSize, rotation, showLeft, showRight, showTop, showBottom, glowEnabled, glowStrength, centerRing, centerRingSize);
    }
}
