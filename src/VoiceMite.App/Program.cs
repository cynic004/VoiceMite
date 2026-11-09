using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using VoiceMite.Core;
using VoiceMite.Windows;

namespace VoiceMite.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        using var mutex = new Mutex(true, "Local\\VoiceInk.Desktop.v1", out var isNew);
        if (!isNew) return 0;
        var app = new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_,e) => { System.Windows.MessageBox.Show(Ui.L?["Unknown"] ?? "VoiceMite could not finish the action.","VoiceMite"); e.Handled=true; };
        using var controller = new AppController(app,args.Contains("--demo")||args.Contains("--ui-smoke")||args.Contains("--target-smoke"));
        if(args.Contains("--target-smoke"))
        {
            var option=Array.IndexOf(args,"--target-smoke");
            if(option+3>=args.Length)return 2;
            app.Dispatcher.BeginInvoke(async ()=>await TargetSmoke.RunAsync(controller,Path.GetFullPath(args[option+1]),Path.GetFullPath(args[option+2]),Path.GetFullPath(args[option+3])));
        }
        else if(args.Contains("--ui-smoke"))
        {
            var option=Array.IndexOf(args,"--ui-smoke");
            var directory=option+1<args.Length?Path.GetFullPath(args[option+1]):Path.Combine(Environment.CurrentDirectory,"artifacts","ui-smoke");
            app.Dispatcher.BeginInvoke(async ()=>await UiSmoke.RunAsync(controller,directory));
        }
        app.Run();
        return UiSmoke.Failed||TargetSmoke.Failed?1:0;
    }
}

internal sealed class AppController : IDisposable
{
    internal AppSettings Settings;
    internal LanguagePreferences Preferences;
    internal readonly SettingsStore Store;
    internal readonly SecretStore Secrets;
    internal readonly NativeTargetWorker Targets = new();
    internal readonly SessionCoordinator Session = new();
    internal readonly AudioRecorder Audio = new();
    internal readonly Application Application;
    internal readonly FloatingWindow Orb;
    internal readonly AssistantWindow Panel;
    private readonly DispatcherTimer timer;
    private readonly System.Windows.Forms.NotifyIcon tray;
    private DateTime recordStarted;
    private bool stopping, inserting, disposed, polling, confirmingTarget;
    private long operation;
    private CancellationTokenSource? insertCancellation;
    internal float Level;
    internal string? NoticeKey;
    internal string? TargetCode;
    internal bool SelectingTarget {get;private set;}
    internal bool IsBusy => stopping || inserting || confirmingTarget || Session.State is SessionState.Transcribing or SessionState.Formatting;
    internal double Elapsed => (DateTime.UtcNow-recordStarted).TotalSeconds;

    internal AppController(Application app,bool demo)
    {
        Application=app;
        Store=new(ProfilePaths.Resolve(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),demo));
        var loaded=Store.LoadSettings(); Settings=loaded.Value; NoticeKey=loaded.WarningCode;
        var loadedPreferences=Store.LoadPreferences();Preferences=loadedPreferences.Value;NoticeKey??=loadedPreferences.WarningCode;
        Secrets=new(Store.DirectoryPath);
        if(demo) { Settings.Stt.Kind=ServiceKind.Demo; Settings.Llm.Kind=ServiceKind.Demo; }
        Ui.L=new(Settings.UiLanguage);
        Orb=new(this); Panel=new(this);
        Session.Changed+=()=>app.Dispatcher.BeginInvoke(Refresh);
        Audio.LevelChanged+=level=>Level=level;
        Audio.LimitReached+=()=>
        {
            var id=Interlocked.Read(ref operation);
            app.Dispatcher.BeginInvoke(async ()=>{if(id==operation&&Session.State==SessionState.Recording&&Elapsed>=119.5)await StopAsync();});
        };
        timer=new() { Interval=TimeSpan.FromMilliseconds(250) };
        timer.Tick+=async (_,_)=>
        {
            if(Session.State==SessionState.Recording) { Panel.UpdateMeter(); Orb.UpdateState(); }
            if(Targets.HasTarget && !inserting&&!polling) { polling=true;var id=operation;try{var r=await Targets.PollAsync();if(id==operation&&!r.Success)TargetCode=r.Code;}finally{polling=false;} }
            Panel.UpdateTarget();
        };
        timer.Start();
        using var iconStream=typeof(AppController).Assembly.GetManifestResourceStream("VoiceMite.App.Assets.VoiceMite.ico")!;
        tray=new() { Icon=new System.Drawing.Icon(iconStream), Text="VoiceMite", Visible=true };
        tray.MouseClick+=(_,e)=> { if(e.Button==System.Windows.Forms.MouseButtons.Left) ShowPanel(); };
        BuildTray();
        SystemEvents.PowerModeChanged+=PowerChanged;
        SystemEvents.SessionSwitch+=SessionSwitched;
        Orb.Show();
        if(demo) ShowPanel();
    }

    private void BuildTray()
    {
        var old=tray.ContextMenuStrip;
        var menu=new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(Ui.L["Show"],null,(_,_)=>Application.Dispatcher.Invoke(ShowPanel));
        menu.Items.Add(Ui.L["Settings"],null,(_,_)=>Application.Dispatcher.Invoke(OpenSettings));
        menu.Items.Add(Ui.L["Exit"],null,(_,_)=>Application.Dispatcher.Invoke(Exit));
        tray.ContextMenuStrip=menu; old?.Dispose();
    }
    internal void Refresh() { if(disposed) return; Panel.UpdateState(); Orb.UpdateState(); }
    internal void ShowPanel() { Panel.PlaceNear(Orb); if(!Panel.IsVisible) Panel.Show(); }
    internal void SaveSettings()
    {
        Store.SaveSettings(Settings);
        Ui.L.SetLanguage(Settings.UiLanguage); BuildTray(); Refresh(); Panel.ApplyContrast();
    }
    internal void Message(string key) { NoticeKey=key; Panel.SetNotice(key); }
    internal void OpenKeyboard()
    {
        try {System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","osk.exe")){UseShellExecute=true});}
        catch {Message("KeyboardUnavailable");}
    }
    internal void OpenSettings()
    {
        if(Session.State==SessionState.Recording || IsBusy) { ShowPanel(); Message("Busy"); return; }
        var settings=new SettingsWindow(this);if(Panel.IsVisible)settings.Owner=Panel;settings.ShowDialog();
    }
    internal void ToggleRecord() {if(IsBusy)return;if(SelectingTarget)_=ConfirmTargetAsync();else if(Session.State==SessionState.Recording) _=StopAsync();else Start();}
    internal async void Start()
    {
        if(IsBusy) return;
        if(!Settings.PrivacyAccepted) { OpenSettings(); return; }
        var id=++operation;
        Session.BeginRecording(); Panel.ClearEditors(); NoticeKey=null; TargetCode=null;
        var captureTask=Targets.CaptureAsync();
        try
        {
            Audio.Start(Settings.MicrophoneId); recordStarted=DateTime.UtcNow; Level=0;
            ShowPanel(); Refresh();
            var capture=await captureTask;
            if(id==operation){if(!capture.Success)TargetCode=capture.Code;Refresh();}
        }
        catch { if(id==operation){Audio.Cancel();Session.Fail(ServiceErrorCode.EmptyAudio);ShowPanel();Message("RecordingFailed");Refresh();} }
    }
    internal async Task StopAsync()
    {
        if(stopping || Session.State!=SessionState.Recording) return;
        var id=operation; stopping=true; byte[]? wav=null;
        try
        {
            wav=await Audio.StopAsync();
            if(id!=operation) return;
            var stt=ServiceFactory.CreateSpeech(Settings.Stt,ReadKey("stt"));
            using var sttLifetime=stt as IDisposable;
            var llm=CreateRewrite(Settings.Mode);
            using var llmLifetime=llm as IDisposable;
            await Session.TranscribeAndFormatAsync(wav,SnapshotSettings(),stt,llm,Preferences);
        }
        catch(ServiceException e) { if(id==operation) { Session.Fail(e.Code); Message(ErrorKey(e.Code)); } }
        catch { if(id==operation) { Session.Cancel(); Message("AudioEmpty"); } }
        finally { if(wav is not null) Array.Clear(wav); stopping=false; Refresh(); }
    }
    internal void Cancel()
    {
        ++operation;SelectingTarget=false;Audio.Cancel();insertCancellation?.Cancel();Session.Cancel();Targets.Clear();TargetCode=null;NoticeKey=null;
        Panel.ClearEditors(); Refresh();
    }
    internal async Task RewriteAsync(InputMode? mode=null)
    {
        if(IsBusy || Session.State==SessionState.Recording) return;
        if(!Settings.PrivacyAccepted && !Settings.TranscribeOnly && (mode??Settings.Mode)!=InputMode.Verbatim && Settings.Llm.Kind!=ServiceKind.Demo) { OpenSettings(); return; }
        ++operation; NoticeKey=null;
        try
        {
            var llm=CreateRewrite(mode??Settings.Mode);
            try { await Session.FormatAsync(Panel.TranscriptText,SnapshotSettings(),llm,mode,Preferences); }
            finally { (llm as IDisposable)?.Dispose(); }
        }
        catch(ServiceException e) { Message(ErrorKey(e.Code)); }
        catch { Message("Unknown"); }
        Refresh();
    }
    internal void DemoSample()
    {
        if(IsBusy || Session.State==SessionState.Recording) return;
        Cancel(); ShowPanel(); Panel.SetTranscript(DemoSpeechService.Example); Message("DemoNotice");
        // Explicit local demo, independent of user's configured paid provider.
        _=RunDemoAsync();
    }
    private async Task RunDemoAsync()
    {
        var demo=Settings with { Stt=Settings.Stt with { Kind=ServiceKind.Demo }, Llm=Settings.Llm with { Kind=ServiceKind.Demo } };
        var rewrite=ServiceFactory.CreateRewrite(demo.Llm,"");
        await Session.FormatAsync(Panel.TranscriptText,demo,rewrite,null,null);
        Refresh();
    }
    internal Task SelectTargetAsync()
    {
        if(IsBusy || Session.State==SessionState.Recording)return Task.CompletedTask;
        ++operation;Targets.Clear();TargetCode=null;SelectingTarget=true;Message("SelectTargetInstructions");Panel.Hide();Orb.UpdateState();
        return Task.CompletedTask;
    }
    private async Task ConfirmTargetAsync()
    {
        var id=++operation;SelectingTarget=false;confirmingTarget=true;
        try
        {
            var capture=await Targets.CaptureAsync();if(id!=operation)return;TargetCode=capture.Success?null:capture.Code;
            Message(capture.Success?"TargetCaptured":capture.Code);Panel.UpdateTarget();ShowPanel();
        }
        finally{confirmingTarget=false;Refresh();}
    }
    internal async Task InsertAsync()
    {
        if(IsBusy || Session.State!=SessionState.Ready || string.IsNullOrWhiteSpace(Panel.ResultText)) return;
        inserting=true; insertCancellation?.Dispose(); insertCancellation=new(); var text=Panel.ResultText;
        var id=++operation;
        Panel.SetInserting(true);
        try
        {
            var result=await Targets.InsertAsync(text,insertCancellation.Token);
            if(id!=operation)return;
            TargetCode=result.Success?null:result.Code;
            Message(result.Success?"Inserted":result.Code);
            if(result.Success) { Learn(text); Targets.Clear(); }
        }
        catch(OperationCanceledException) { }
        catch { Targets.Clear();if(id==operation)Message("InsertUncertain"); }
        finally { inserting=false; Panel.SetInserting(false); Refresh(); }
    }
    internal void Copy()
    {
        if(string.IsNullOrEmpty(Panel.ResultText)) return;
        var r=SafeTargetService.CopyText(Panel.ResultText); Message(r.Success?"Copied":r.Code); if(r.Success) Learn(Panel.ResultText);
    }
    private void Learn(string text)
    {
        if(!Settings.LearnPreferences) return;
        try { Preferences=Store.LearnFromConfirmedText(text,Preferences); } catch { Message("Unknown"); }
    }
    internal string ReadKey(string service)
    {
        var kind=service=="stt"?Settings.Stt.Kind:Settings.Llm.Kind;
        if(kind==ServiceKind.Demo) return "";
        try { return Secrets.Get(service) ?? ""; } catch { throw new ServiceException(ServiceErrorCode.MissingKey); }
    }
    private AppSettings SnapshotSettings()=>Settings with { Stt=Settings.Stt with {}, Llm=Settings.Llm with {} };
    private IRewriteService CreateRewrite(InputMode mode)=>Settings.TranscribeOnly||mode==InputMode.Verbatim
        ? ServiceFactory.CreateRewrite(new ServiceConfig {Kind=ServiceKind.Demo},"")
        : ServiceFactory.CreateRewrite(Settings.Llm,ReadKey("llm"));
    internal static string ErrorKey(ServiceErrorCode code)=>code switch
    {
        ServiceErrorCode.MissingKey=>"InvalidKey", ServiceErrorCode.QuotaOrRateLimit=>"RateLimited", ServiceErrorCode.Timeout=>"TimeoutError", ServiceErrorCode.ServiceUnavailable=>"Server", ServiceErrorCode.InputTooLong=>"TooLarge", ServiceErrorCode.EmptyAudio=>"AudioEmpty", _=>code.ToString()
    };
    private void PowerChanged(object sender,PowerModeChangedEventArgs e) { if(e.Mode==PowerModes.Suspend) Application.Dispatcher.BeginInvoke(()=>{Cancel(); Message("Suspend");}); }
    private void SessionSwitched(object sender,SessionSwitchEventArgs e) { if(e.Reason==SessionSwitchReason.SessionLock) Application.Dispatcher.BeginInvoke(()=>{Cancel(); Message("Suspend");}); }
    internal void Exit() { Dispose(); Application.Shutdown(); }
    public void Dispose()
    {
        if(disposed) return; disposed=true; ++operation;
        timer.Stop(); SystemEvents.PowerModeChanged-=PowerChanged; SystemEvents.SessionSwitch-=SessionSwitched;
        Audio.Cancel(); Audio.Dispose(); Session.Dispose(); insertCancellation?.Cancel(); insertCancellation?.Dispose();
        tray.Visible=false; tray.Icon?.Dispose();tray.Dispose(); Targets.Dispose();
        Settings.FloatLeft=Orb.Left; Settings.FloatTop=Orb.Top; try { Store.SaveSettings(Settings); } catch { }
    }
}
