using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WHIPPINMTA;

public sealed class AimTrainerWindow : Window
{
    private readonly Canvas _field = new();
    private readonly TextBlock _scoreText = new();
    private readonly TextBlock _hitText = new();
    private readonly TextBlock _missText = new();
    private readonly TextBlock _accuracyText = new();
    private readonly TextBlock _timeText = new();
    private readonly ComboBox _difficulty = new();
    private readonly Slider _size = new() { Minimum = 16, Maximum = 58, Value = 30, Width = 150 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();
    private readonly Random _random = new();
    private Ellipse? _target;
    private Vector _velocity = new(3.2, 2.4);
    private int _score, _hits, _misses;
    private double _remaining = 60;
    private bool _running;

    public AimTrainerWindow()
    {
        Title = "WHIPPINMTA / AIM TRAINER";
        Width = 1120; Height = 760; MinWidth = 850; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("#FF080B14"); Foreground = Brushes.White;
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new DockPanel { Margin = new Thickness(0,0,0,16) };
        var title = new TextBlock { Text = "AIM TRAINER", FontSize = 23, FontWeight = FontWeights.Black, Foreground = Brush("#FFFF334D") };
        heading.Children.Add(title);
        var subtitle = new TextBlock { Text = "  /  REFLEKS • TAKİP • İSABET", FontSize = 10, Foreground = Brush("#FF929BAD"), VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(subtitle);
        root.Children.Add(heading);

        var controls = new WrapPanel { Margin = new Thickness(0,0,0,14), VerticalAlignment = VerticalAlignment.Center };
        AddStat(controls, "SKOR", _scoreText);
        AddStat(controls, "İSABET", _hitText);
        AddStat(controls, "KAÇIRMA", _missText);
        AddStat(controls, "İSABET ORANI", _accuracyText);
        AddStat(controls, "SÜRE", _timeText);
        controls.Children.Add(new TextBlock { Text = "  ZORLUK", Foreground = Brush("#FF929BAD"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12,0,6,0), FontSize = 10 });
        _difficulty.Items.Add("Kolay"); _difficulty.Items.Add("Normal"); _difficulty.Items.Add("Zor"); _difficulty.Items.Add("Uzman");
        _difficulty.SelectedIndex = 1; _difficulty.Width = 100; _difficulty.Margin = new Thickness(0,0,14,0);
        controls.Children.Add(_difficulty);
        controls.Children.Add(new TextBlock { Text = "HEDEF", Foreground = Brush("#FF929BAD"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,6,0), FontSize = 10 });
        controls.Children.Add(_size);
        Grid.SetRow(controls,1); root.Children.Add(controls);

        _field.Background = Brush("#FF0D1320");
        _field.ClipToBounds = true;
        _field.MouseLeftButtonDown += (_, _) => { if (_running) { _misses++; UpdateStats(); } };
        Grid.SetRow(_field,2); root.Children.Add(_field);

        var footer = new DockPanel { Margin = new Thickness(0,14,0,0) };
        var note = new TextBlock { Text = "Hedeflere tıkla. Boş alana tıklamak ıskalama sayılır. Bu antrenman oyunlardan bağımsız çalışır.", Foreground = Brush("#FF929BAD"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
        footer.Children.Add(note);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var start = MakeButton("BAŞLAT  /  60 SN", "#FF8D1021");
        start.Click += (_, _) => StartRound();
        var reset = MakeButton("SIFIRLA", "#FF182033");
        reset.Click += (_, _) => ResetRound();
        buttons.Children.Add(start); buttons.Children.Add(reset);
        DockPanel.SetDock(buttons, Dock.Right); footer.Children.Add(buttons);
        Grid.SetRow(footer,3); root.Children.Add(footer);
        Content = root;
        _timer.Tick += Tick;
        Loaded += (_, _) => { UpdateStats(); SpawnTarget(); };
        Closed += (_, _) => _timer.Stop();
    }

    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private static Button MakeButton(string text, string color) => new() { Content=text, Background=Brush(color), Foreground=Brush("#FFFFFFFF"), BorderBrush=Brush("#FF3A4356"), BorderThickness=new Thickness(1), Padding=new Thickness(16,10,16,10), Margin=new Thickness(6,0,0,0), FontWeight=FontWeights.Bold, Cursor=System.Windows.Input.Cursors.Hand };
    private static void AddStat(Panel panel, string label, TextBlock value)
    {
        var stack = new StackPanel { Margin = new Thickness(0,0,18,0) };
        stack.Children.Add(new TextBlock { Text=label, FontSize=8, Foreground=Brush("#FF7F899D") });
        value.FontSize=16; value.FontWeight=FontWeights.Bold; value.Foreground=Brush("#FFF4F6FB");
        stack.Children.Add(value); panel.Children.Add(stack);
    }

    private void StartRound()
    {
        _score=0; _hits=0; _misses=0; _remaining=60;
        _clock.Restart(); _running=true; _field.IsEnabled=true;
        SetVelocity(); SpawnTarget(); UpdateStats(); _timer.Start();
    }
    private void ResetRound()
    {
        _timer.Stop(); _clock.Reset(); _running=false;
        _score=0; _hits=0; _misses=0; _remaining=60;
        _target=null; _field.Children.Clear(); SpawnTarget(); UpdateStats();
    }
    private void SetVelocity()
    {
        var speed = _difficulty.SelectedIndex switch { 0=>1.8, 1=>3.2, 2=>4.8, _=>6.2 };
        _velocity = new Vector(speed * (_random.Next(2)==0?-1:1), speed * .72 * (_random.Next(2)==0?-1:1));
    }
    private void SpawnTarget()
    {
        if (_field.ActualWidth < 10 || _field.ActualHeight < 10) return;
        _field.Children.Clear();
        var diameter = _size.Value;
        var target = new Ellipse { Width=diameter, Height=diameter, Fill=Brush("#FFFF3048"), Stroke=Brush("#FFFFB4BD"), StrokeThickness=2, Cursor=System.Windows.Input.Cursors.Hand };
        target.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled=true;
            if (!_running) return;
            _hits++; _score += 100 + Math.Max(0, (int)(60-_remaining));
            SetVelocity(); SpawnTarget(); UpdateStats();
        };
        _target=target; _field.Children.Add(target);
        Canvas.SetLeft(target, _random.NextDouble()*Math.Max(1,_field.ActualWidth-diameter));
        Canvas.SetTop(target, _random.NextDouble()*Math.Max(1,_field.ActualHeight-diameter));
    }
    private void Tick(object? sender, EventArgs e)
    {
        if (!_running || _target is null) return;
        _remaining = Math.Max(0, 60-_clock.Elapsed.TotalSeconds);
        var x=Canvas.GetLeft(_target); var y=Canvas.GetTop(_target);
        var maxX=Math.Max(0,_field.ActualWidth-_target.Width); var maxY=Math.Max(0,_field.ActualHeight-_target.Height);
        x+=_velocity.X; y+=_velocity.Y;
        if (x<=0 || x>=maxX) _velocity.X=-_velocity.X;
        if (y<=0 || y>=maxY) _velocity.Y=-_velocity.Y;
        Canvas.SetLeft(_target,Math.Clamp(x,0,maxX)); Canvas.SetTop(_target,Math.Clamp(y,0,maxY));
        UpdateStats();
        if (_remaining<=0) { _running=false; _timer.Stop(); _clock.Stop(); _target.Opacity=.35; }
    }
    private void UpdateStats()
    {
        _scoreText.Text=_score.ToString();
        _hitText.Text=_hits.ToString();
        _missText.Text=_misses.ToString();
        var total=_hits+_misses;
        _accuracyText.Text=(total==0?0:(double)_hits/total*100).ToString("0.0")+"%";
        _timeText.Text=Math.Ceiling(_remaining).ToString("00")+"s";
    }
}
