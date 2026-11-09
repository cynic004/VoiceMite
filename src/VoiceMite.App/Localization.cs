using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace VoiceMite.App;

public sealed class Localization : INotifyPropertyChanged
{
    private Dictionary<string,string> strings = [];
    public string Language { get; private set; } = "zh-CN";
    public event PropertyChangedEventHandler? PropertyChanged;
    public Localization(string language) => SetLanguage(language);
    public string this[string key] => strings.GetValueOrDefault(key, strings.GetValueOrDefault("Unknown", "Please try again."));
    public void SetLanguage(string language)
    {
        Language = language == "en" ? "en" : "zh-CN";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"VoiceMite.App.Strings.{Language}.json")!;
        strings = JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;
        PropertyChanged?.Invoke(this, new("Item[]"));
    }
    public void Bind(DependencyObject target, DependencyProperty property, string key) => BindingOperations.SetBinding(target, property, new Binding($"[{key}]") { Source = this });
}

internal static class Ui
{
    internal static Localization L = null!;
    internal static readonly SolidColorBrush Ink = new(Color.FromRgb(25,44,67));
    internal static readonly SolidColorBrush Muted = new(Color.FromRgb(82,105,128));
    internal static readonly SolidColorBrush Blue = new(Color.FromRgb(34,109,190));
    internal static TextBlock Text(string key, double size=14, bool bold=false)
    {
        var t = new TextBlock { FontSize=size, FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal, TextWrapping=TextWrapping.Wrap, Foreground=Ink, Margin=new(0,4,0,5) };
        L.Bind(t,TextBlock.TextProperty,key); return t;
    }
    internal static Button Button(string key, Action action, bool primary=false)
    {
        var b = new Button { Margin=new(0,4,8,4), Padding=new(14,9,14,9), MinHeight=38, MinWidth=70, Cursor=System.Windows.Input.Cursors.Hand, Background=primary?Blue:Brushes.White, Foreground=primary?Brushes.White:Ink, BorderBrush=new SolidColorBrush(Color.FromRgb(202,219,234)), BorderThickness=new(1) };
        L.Bind(b,ContentControl.ContentProperty,key); b.Click+=(_,_)=>action(); return b;
    }
    internal static CheckBox Check(string key, bool value, Action<bool> action)
    {
        var label=Text(key,14);label.Margin=new(4,0,0,0);label.MaxWidth=500;
        var c = new CheckBox { IsChecked=value, Content=label, Margin=new(0,8,0,8), Foreground=Ink, VerticalContentAlignment=VerticalAlignment.Center };
        L.Bind(c,System.Windows.Automation.AutomationProperties.NameProperty,key); c.Checked+=(_,_)=>action(true); c.Unchecked+=(_,_)=>action(false); return c;
    }
    internal static void Label(Panel p,string key) => p.Children.Add(Text(key,13));
    internal static TextBox Edit(Panel p,string key,string value,Action<string> changed,bool multiline=false)
    {
        Label(p,key); var e=new TextBox { Text=value, Padding=new(8), Margin=new(0,0,0,8), MinHeight=36, TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap, AcceptsReturn=multiline, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, MaxLength=multiline?16000:2048 };
        L.Bind(e,System.Windows.Automation.AutomationProperties.NameProperty,key);
        e.TextChanged+=(_,_)=>changed(e.Text); p.Children.Add(e); return e;
    }
}
