using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
namespace WHIPPINMTA;
public partial class MainWindow : Window
{
 private string _color="#FFFF4242"; private string _preset="classic"; private OverlayWindow? _overlay;
 private sealed record Settings(double Size,double Thickness,double Gap,string Color,string Preset,bool Overlay);
 public MainWindow(){InitializeComponent(); DrawPreview(); }
 private void Preset_Click(object sender,RoutedEventArgs e){_preset=(string)((Button)sender).Tag; if(_preset=="dot"){SizeSlider.Value=6;GapSlider.Value=0;} else if(_preset=="precision"){SizeSlider.Value=24;GapSlider.Value=7;ThicknessSlider.Value=1;} else if(_preset=="minimal"){SizeSlider.Value=14;GapSlider.Value=4;} else if(_preset=="circle"){SizeSlider.Value=24;GapSlider.Value=4;} else {SizeSlider.Value=18;GapSlider.Value=5;} DrawPreview();}
 private void Color_Click(object sender,RoutedEventArgs e){_color=(string)((Button)sender).Tag;DrawPreview();}
 private void Setting_Changed(object sender,RoutedPropertyChangedEventArgs<double> e){if(IsLoaded)DrawPreview();}
 private void DrawPreview(){if(PreviewCanvas is null)return; PreviewCanvas.Children.Clear(); double cx=PreviewCanvas.ActualWidth/2,cy=PreviewCanvas.ActualHeight/2; if(cx<1){PreviewCanvas.Loaded+=(s,e)=>DrawPreview();return;} AddCross(PreviewCanvas,cx,cy,SizeSlider.Value,ThicknessSlider.Value,GapSlider.Value,_color,_preset);}
 internal static void AddCross(Canvas c,double x,double y,double size,double thick,double gap,string color,string preset){var brush=(Brush)new BrushConverter().ConvertFromString(color)!; void Line(double x1,double y1,double x2,double y2){var l=new Line{X1=x1,Y1=y1,X2=x2,Y2=y2,Stroke=brush,StrokeThickness=thick,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};c.Children.Add(l);} if(preset=="circle"){var el=new Ellipse{Width=size,Height=size,Stroke=brush,StrokeThickness=thick};Canvas.SetLeft(el,x-size/2);Canvas.SetTop(el,y-size/2);c.Children.Add(el);} else if(preset=="dot"){var el=new Ellipse{Width=size/3,Height=size/3,Fill=brush};Canvas.SetLeft(el,x-size/6);Canvas.SetTop(el,y-size/6);c.Children.Add(el);} else {Line(x-size/2,y,x-gap,y);Line(x+gap,y,x+size/2,y);Line(x,y-size/2,x,y-gap);Line(x,y+gap,x,y+size/2);} }
 private void Overlay_Changed(object sender,RoutedEventArgs e){if(!IsLoaded)return;if(OverlayCheck.IsChecked==true){_overlay??=new OverlayWindow();_overlay.Show();UpdateOverlay();}else{_overlay?.Close();_overlay=null;}}
 private void UpdateOverlay(){_overlay?.UpdateCross(SizeSlider.Value,ThicknessSlider.Value,GapSlider.Value,_color,_preset);}
 private void Save_Click(object sender,RoutedEventArgs e){try{var s=new Settings(SizeSlider.Value,ThicknessSlider.Value,GapSlider.Value,_color,_preset,OverlayCheck.IsChecked==true);File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"whippinmta-settings.json"),JsonSerializer.Serialize(s,new JsonSerializerOptions{WriteIndented=true}));StatusText.Text="Ayarlar kaydedildi.";}catch(Exception ex){StatusText.Text="Kaydetme hatası: "+ex.Message;}}
 protected override void OnClosed(EventArgs e){_overlay?.Close();base.OnClosed(e);}
}
public sealed class OverlayWindow:Window
{
 private readonly Canvas _canvas=new(); public OverlayWindow(){WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;Width=SystemParameters.PrimaryScreenWidth;Height=SystemParameters.PrimaryScreenHeight;Left=0;Top=0;IsHitTestVisible=false;Content=_canvas;Loaded+=(s,e)=>{UpdateCross(18,2,5,"#FFFF4242","classic");};}
 public void UpdateCross(double size,double thick,double gap,string color,string preset){_canvas.Children.Clear();MainWindow.AddCross(_canvas,Width/2,Height/2,size,thick,gap,color,preset);}
}
