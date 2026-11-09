using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VoiceMite.Core;

namespace VoiceMite.App;

internal sealed class AssistantWindow : Window
{
    private readonly AppController app;
    private readonly TextBlock state,notice,target,timing,summary;
    private readonly ProgressBar meter;
    private readonly TextBox transcript,result;
    private readonly Button record,insert,copy,rewrite,choose;
    private readonly WrapPanel choices;
    private readonly ComboBox modes;
    private readonly TextBlock demo;
    private string lastTranscript="";
    private RewriteResult? lastResult;
    private bool changingMode;
    internal string TranscriptText=>transcript.Text;
    internal string ResultText=>result.Text;
    internal AssistantWindow(AppController app)
    {
        this.app=app;Title="VoiceMite";Width=510;Height=740;MinWidth=420;MinHeight=440;MaxHeight=1000;
        ShowActivated=false;Topmost=true;Background=new SolidColorBrush(Color.FromRgb(242,248,253));Foreground=Ui.Ink;FontFamily=new("Microsoft YaHei UI");FontSize=14;
        var content=new StackPanel {Margin=new(22,16,18,20)};
        var header=new DockPanel();var settings=Ui.Button("Settings",app.OpenSettings);DockPanel.SetDock(settings,Dock.Right);header.Children.Add(settings);header.Children.Add(Ui.Text("AppName",23,true));content.Children.Add(header);
        content.Children.Add(Ui.Text("Tagline",13));
        state=new TextBlock {FontSize=17,FontWeight=FontWeights.SemiBold,Margin=new(0,10,0,6)};content.Children.Add(state);
        notice=new TextBlock {TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Color.FromRgb(134,76,18)),Margin=new(0,4,0,8)};content.Children.Add(notice);
        demo=Ui.Text("DemoNotice",12);demo.Foreground=Ui.Muted;content.Children.Add(demo);
        var recordActions=new WrapPanel(); record=Ui.Button("Start",app.ToggleRecord,true);recordActions.Children.Add(record);recordActions.Children.Add(Ui.Button("Cancel",app.Cancel));recordActions.Children.Add(Ui.Button("Hide",Hide));content.Children.Add(recordActions);
        timing=new TextBlock {TextWrapping=TextWrapping.Wrap,Foreground=Ui.Muted,Margin=new(0,4,0,5)};content.Children.Add(timing);
        meter=new ProgressBar {Minimum=0,Maximum=1,Height=5,Margin=new(0,0,0,12)};content.Children.Add(meter);
        Ui.Label(content,"ModeLabel"); modes=new ComboBox {MinHeight=35,Margin=new(0,0,0,9)};BuildModes();modes.SelectionChanged+=(_,_)=> {if(!changingMode&&modes.SelectedItem is ComboBoxItem item){app.Settings.Mode=(InputMode)item.Tag;try{app.Store.SaveSettings(app.Settings);}catch{}}};content.Children.Add(modes);
        transcript=Ui.Edit(content,"Transcript","",_=>{},true);transcript.Height=95;
        choices=new WrapPanel {Margin=new(0,3,0,7)};content.Children.Add(choices);
        summary=new TextBlock {TextWrapping=TextWrapping.Wrap,Foreground=Ui.Muted,Margin=new(0,5,0,5)};content.Children.Add(summary);
        result=Ui.Edit(content,"Result","",_=>{},true);result.Height=135;
        var actions=new WrapPanel();insert=Ui.Button("Insert",()=>_=app.InsertAsync(),true);copy=Ui.Button("Copy",app.Copy);rewrite=Ui.Button("Regenerate",()=>_=app.RewriteAsync());actions.Children.Add(insert);actions.Children.Add(copy);actions.Children.Add(rewrite);actions.Children.Add(Ui.Button("KeepOriginal",()=>_=app.RewriteAsync(InputMode.Verbatim)));actions.Children.Add(Ui.Button("Keyboard",app.OpenKeyboard));content.Children.Add(actions);
        target=new TextBlock {TextWrapping=TextWrapping.Wrap,Foreground=Ui.Muted,FontSize=12,Margin=new(0,10,0,3)};content.Children.Add(target);
        choose=Ui.Button("SelectTarget",()=>_=app.SelectTargetAsync());content.Children.Add(choose);content.Children.Add(Ui.Text("TargetHint",12));content.Children.Add(Ui.Text("ClipboardNotice",12));content.Children.Add(Ui.Text("ManualHint",12));
        var bottom=new WrapPanel();bottom.Children.Add(Ui.Button("DemoSample",app.DemoSample));bottom.Children.Add(Ui.Button("Help",()=>System.Windows.MessageBox.Show(this,Ui.L["HelpText"],Ui.L["Help"])));content.Children.Add(bottom);
        Content=new ScrollViewer {Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        Closing+=(_,e)=> {if(!app.Application.Dispatcher.HasShutdownStarted){e.Cancel=true;Hide();}};
        Ui.L.PropertyChanged+=(_,_)=> {BuildModes();UpdateState();};
        UpdateState();
    }
    private void BuildModes()
    {
        changingMode=true;modes.Items.Clear();
        foreach(var mode in Enum.GetValues<InputMode>()) {var item=new ComboBoxItem {Content=Ui.L["Mode"+mode],Tag=mode};modes.Items.Add(item);if(mode==app.Settings.Mode)modes.SelectedItem=item;}
        changingMode=false;
    }
    internal void UpdateState()
    {
        var s=app.Session;
        state.Text=Ui.L[s.State.ToString()];
        Ui.L.Bind(record,ContentControl.ContentProperty,s.State==SessionState.Recording?"Stop":"Start");
        record.IsEnabled=!app.IsBusy;
        if(s.Transcript!=lastTranscript){lastTranscript=s.Transcript;transcript.Text=s.Transcript;}
        if(!ReferenceEquals(lastResult,s.Result))
        {
            lastResult=s.Result;result.Text=s.Result?.Text??"";choices.Children.Clear();
            if(s.Result?.Question is not null)
            {
                var question=new TextBlock {TextWrapping=TextWrapping.Wrap,MaxWidth=420,Margin=new(0,4,0,6)};
                if(s.Result.Question=="__demo_choose_mode__")Ui.L.Bind(question,TextBlock.TextProperty,"DemoClarify");else question.Text=s.Result.Question;
                choices.Children.Add(question);
                foreach(var choice in s.Result.Choices??[]) {var mode=choice;choices.Children.Add(Ui.Button("Mode"+mode,()=>_=app.RewriteAsync(mode)));}
                choices.Children.Add(Ui.Button("KeepOriginal",()=>_=app.RewriteAsync(InputMode.Verbatim)));
                choices.Children.Add(Ui.Button("RecordAgain",app.Start));choices.Children.Add(Ui.Button("Cancel",app.Cancel));
            }
        }
        summary.Text=s.Result?.Summary is {Length:>0} text ? Ui.L["Summary"]+": "+text : "";
        if(app.NoticeKey is not null)SetNotice(app.NoticeKey);else if(s.ErrorCode is not null)SetNotice(AppController.ErrorKey(s.ErrorCode.Value));else SetNotice(null);
        demo.Visibility=app.Settings.Stt.Kind==ServiceKind.Demo||app.Settings.Llm.Kind==ServiceKind.Demo?Visibility.Visible:Visibility.Collapsed;
        insert.IsEnabled=s.State==SessionState.Ready&&app.Targets.HasTarget&&!app.IsBusy;
        copy.IsEnabled=result.Text.Length>0&&!app.IsBusy;
        rewrite.IsEnabled=s.State!=SessionState.Recording&&!app.IsBusy;
        choose.IsEnabled=s.State!=SessionState.Recording&&!app.IsBusy;
        transcript.IsReadOnly=s.State==SessionState.Recording||app.IsBusy;
        result.IsReadOnly=app.IsBusy;
        UpdateTarget();UpdateMeter();
    }
    internal void UpdateTarget()
    {
        target.Text=app.Targets.HasTarget?Ui.L["TargetReady"]+app.Targets.TargetDescription:Ui.L[app.TargetCode??"TargetNone"];
        insert.IsEnabled=app.Session.State==SessionState.Ready&&app.Targets.HasTarget&&!app.IsBusy;
    }
    internal void UpdateMeter()
    {
        var recording=app.Session.State==SessionState.Recording;
        timing.Text=recording?$"● {TimeSpan.FromSeconds(Math.Max(0,app.Elapsed)):m\\:ss} / 2:00 · "+Ui.L["NonStreaming"]:"";
        meter.Visibility=recording?Visibility.Visible:Visibility.Collapsed;meter.Value=recording?app.Level:0;
    }
    internal void SetNotice(string? key)=>notice.Text=key is null?"":Ui.L[key];
    internal void SetCustomNotice(string text)=>notice.Text=text;
    internal void SetInserting(bool value) {if(value)state.Text=Ui.L["Inserting"];insert.IsEnabled=!value;}
    internal void ClearEditors(){lastTranscript="";lastResult=null;transcript.Clear();result.Clear();choices.Children.Clear();summary.Text="";}
    internal void SetTranscript(string text){transcript.Text=text;lastTranscript=text;}
    internal void ApplyContrast(){Background=app.Settings.HighContrast?Brushes.White:new SolidColorBrush(Color.FromRgb(242,248,253));Foreground=Brushes.Black;}
    internal void PlaceNear(Window orb)
    {
        if(IsVisible)return;
        var screen=System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(orb).Handle);
        var dpi=VisualTreeHelper.GetDpi(orb);var pixels=screen.WorkingArea;
        var area=new Rect(pixels.Left/dpi.DpiScaleX,pixels.Top/dpi.DpiScaleY,pixels.Width/dpi.DpiScaleX,pixels.Height/dpi.DpiScaleY);
        Left=Math.Clamp(orb.Left+orb.Width,area.Left,Math.Max(area.Left,area.Right-Width));Top=Math.Clamp(orb.Top,area.Top,Math.Max(area.Top,area.Bottom-Height));
    }
}
