using System.Text.Json;
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

namespace WHIPPINMTA;

public partial class MainWindow : Window
{
    private string _color = "#FFFF4242";
    private string _preset = "classic";
    private OverlayWindow? _overlay;
    private bool _dragging;
    private static readonly HttpClient UpdateHttp = new();
    private UpdateManifest? _availableUpdate;
    private bool _checkingUpdate;
    private sealed record UpdateManifest(string Version, string DownloadUrl, string Sha256);
    private sealed record Settings(double Size, double Thickness, double Gap, string Color, string Preset, bool Overlay, double OffsetX = 0, double OffsetY = 0);

    public MainWindow()
    {
        InitializeComponent();
        LoadSettings();
        DrawPreview();
        Loaded += async (_, _) => await CheckForUpdatesAsync(false);
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
            _color = s.Color;
            _preset = s.Preset;
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

    private void Color_Click(object sender, RoutedEventArgs e) { _color = (string)((Button)sender).Tag; DrawPreview(); }
    private void Setting_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (IsLoaded) DrawPreview(); }
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
        AddCross(PreviewCanvas, cx + OffsetXSlider.Value, cy + OffsetYSlider.Value, SizeSlider.Value, ThicknessSlider.Value, GapSlider.Value, _color, _preset);
        UpdateOverlay();
    }

    internal static void AddCross(Canvas c, double x, double y, double size, double thick, double gap, string color, string preset)
    {
        var brush = (Brush)new BrushConverter().ConvertFromString(color)!;
        void Line(double x1, double y1, double x2, double y2)
        {
            var l = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = thick, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            c.Children.Add(l);
        }
        if (preset == "circle")
        {
            var el = new Ellipse { Width = size, Height = size, Stroke = brush, StrokeThickness = thick };
            Canvas.SetLeft(el, x - size / 2); Canvas.SetTop(el, y - size / 2); c.Children.Add(el);
        }
        else if (preset == "dot")
        {
            var el = new Ellipse { Width = size / 3, Height = size / 3, Fill = brush };
            Canvas.SetLeft(el, x - size / 6); Canvas.SetTop(el, y - size / 6); c.Children.Add(el);
        }
        else
        {
            Line(x - size / 2, y, x - gap, y); Line(x + gap, y, x + size / 2, y);
            Line(x, y - size / 2, x, y - gap); Line(x, y + gap, x, y + size / 2);
        }
    }

    private void Overlay_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        if (OverlayCheck.IsChecked == true) { _overlay ??= new OverlayWindow(); _overlay.Show(); UpdateOverlay(); }
        else { _overlay?.Close(); _overlay = null; }
    }
    private void UpdateOverlay() => _overlay?.UpdateCross(SizeSlider.Value, ThicknessSlider.Value, GapSlider.Value, _color, _preset, OffsetXSlider.Value, OffsetYSlider.Value);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var s = new Settings(SizeSlider.Value, ThicknessSlider.Value, GapSlider.Value, _color, _preset, OverlayCheck.IsChecked == true, OffsetXSlider.Value, OffsetYSlider.Value);
            System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "whippinmta-settings.json"), JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
            StatusText.Text = "Ayarlar ve crosshair konumu kaydedildi.";
        }
        catch (Exception ex) { StatusText.Text = "Kaydetme hatası: " + ex.Message; }
    }

    protected override void OnClosed(EventArgs e) { _overlay?.Close(); base.OnClosed(e); }
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
        Topmost = true; ShowInTaskbar = false; Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight; Left = 0; Top = 0;
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
    public void UpdateCross(double size, double thick, double gap, string color, string preset, double offsetX, double offsetY)
    {
        _canvas.Children.Clear();
        MainWindow.AddCross(_canvas, Width / 2 + offsetX, Height / 2 + offsetY, size, thick, gap, color, preset);
    }
}
