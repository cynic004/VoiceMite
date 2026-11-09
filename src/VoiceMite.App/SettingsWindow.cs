using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VoiceMite.Core;
using VoiceMite.Windows;

namespace VoiceMite.App;

internal sealed class SettingsWindow : Window
{
    private readonly AppController app;
    private readonly AppSettings draft;
    private LanguagePreferences preferences;
    private readonly StackPanel body=new() {Margin=new(24)};
    private readonly TextBlock feedback=new() {TextWrapping=TextWrapping.Wrap,Foreground=Ui.Muted,Margin=new(0,10,0,10)};
    private PasswordBox sttKey=null!,llmKey=null!;
    private readonly CancellationTokenSource lifetime=new();
    private bool testing,building;
    private readonly Dictionary<string,bool> timeoutValid=new() {{"stt",true},{"llm",true}};
    internal SettingsWindow(AppController app)
    {
        this.app=app;draft=app.Settings with {Stt=app.Settings.Stt with {},Llm=app.Settings.Llm with {}};preferences=app.Preferences with {};
        Title=Ui.L["Settings"]+" · VoiceMite";Width=650;Height=800;MinWidth=480;MinHeight=400;WindowStartupLocation=WindowStartupLocation.CenterScreen;Topmost=true;
        Background=new SolidColorBrush(Color.FromRgb(246,250,254));FontFamily=new("Microsoft YaHei UI");FontSize=14;
        Content=new ScrollViewer {Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        Closed+=(_,_)=> {lifetime.Cancel();lifetime.Dispose();sttKey.Clear();llmKey.Clear();};
        Build();
    }
    private void Build()
    {
        building=true;body.Children.Clear();
        body.Children.Add(Ui.Text("PrivacyTitle",23,true));body.Children.Add(Ui.Text("Privacy",13));
        var ui=Select(body,"UiLanguage",[new("简体中文","zh-CN"),new("English","en")],draft.UiLanguage,value=>
        {
            if(building)return;draft.UiLanguage=value;app.Settings.UiLanguage=value;
            // Interface language never changes consent or provider configuration.
            try {app.SaveSettings();}catch{Ui.L.SetLanguage(value);}
            Title=Ui.L["Settings"]+" · VoiceMite";
        });
        Ui.Edit(body,"SpeechLanguage",draft.SpeechLanguage,v=>draft.SpeechLanguage=v);
        Select(body,"OutputLanguage",[new("SameLanguage","same",true),new("Chinese","zh-CN",true),new("English","en",true)],draft.OutputLanguage,v=>draft.OutputLanguage=v);
        body.Children.Add(Ui.Check("TranscribeOnly",draft.TranscribeOnly,v=>draft.TranscribeOnly=v));
        ServiceSection("stt",draft.Stt,"SttSection");ServiceSection("llm",draft.Llm,"LlmSection");
        Ui.Label(body,"Microphone");var mics=new ComboBox {MinHeight=36,Margin=new(0,0,0,8)};
        void RefreshMics()
        {
            var selected=draft.MicrophoneId;
            mics.Items.Clear();var defaultItem=new ComboBoxItem {Tag=-1};Ui.L.Bind(defaultItem,ContentControl.ContentProperty,"DefaultMic");mics.Items.Add(defaultItem);mics.SelectedIndex=0;
            try { var devices=AudioRecorder.ListDevices();foreach(var mic in devices){var item=new ComboBoxItem {Content=mic.Name,Tag=mic.Id};mics.Items.Add(item);if(mic.Id==selected)mics.SelectedItem=item;}if(devices.Count==0)feedback.Text=Ui.L["NoMic"]; }
            catch {feedback.Text=Ui.L["NoMic"];}
        }
        RefreshMics();mics.SelectionChanged+=(_,_)=>{if(mics.SelectedItem is ComboBoxItem item)draft.MicrophoneId=(int)item.Tag;};body.Children.Add(mics);body.Children.Add(Ui.Button("RefreshMic",RefreshMics));
        var summary=Ui.Check("LiveSummary",false,_=>{});summary.IsEnabled=false;body.Children.Add(summary);body.Children.Add(Ui.Text("SummaryUnavailable",12));
        body.Children.Add(Ui.Check("ReduceMotion",draft.ReduceMotion,v=>draft.ReduceMotion=v));body.Children.Add(Ui.Check("HighContrast",draft.HighContrast,v=>draft.HighContrast=v));
        body.Children.Add(Ui.Text("Preferences",20,true));body.Children.Add(Ui.Text("PrefHint",12));
        body.Children.Add(Ui.Check("Learn",draft.LearnPreferences,v=>draft.LearnPreferences=v));
        Ui.Edit(body,"Script",preferences.Script,v=>preferences.Script=v).MaxLength=300;
        Ui.Edit(body,"Punctuation",preferences.Punctuation,v=>preferences.Punctuation=v).MaxLength=300;
        Ui.Edit(body,"Length",preferences.Length,v=>preferences.Length=v).MaxLength=300;
        Ui.Edit(body,"Tone",preferences.Tone,v=>preferences.Tone=v).MaxLength=300;
        Ui.Edit(body,"Terms",preferences.Terms,v=>preferences.Terms=v).MaxLength=300;
        body.Children.Add(Ui.Button("DeleteMemory",()=>
        {
            try {app.Store.DeletePreferences();app.Preferences=new();preferences=new();app.Settings.LearnPreferences=false;draft.LearnPreferences=false;app.Store.SaveSettings(app.Settings);Build();feedback.Text=Ui.L["MemoryDeleted"];}
            catch {feedback.Text=Ui.L["Unknown"];}
        }));
        body.Children.Add(feedback);
        var actions=new WrapPanel();actions.Children.Add(Ui.Button(app.Settings.PrivacyAccepted?"Save":"Accept",Save,true));actions.Children.Add(Ui.Button("Keyboard",app.OpenKeyboard));actions.Children.Add(Ui.Button("Close",Close));body.Children.Add(actions);
        body.Children.Add(Ui.Text("ClipboardNotice",12));body.Children.Add(Ui.Text("HelpText",12));building=false;
    }
    private sealed record Option(string Label,string Value,bool Localized=false);
    private static ComboBox Select(Panel panel,string key,IReadOnlyList<Option> choices,string selected,Action<string> set)
    {
        Ui.Label(panel,key);var c=new ComboBox {MinHeight=35,Margin=new(0,0,0,8)};
        foreach(var option in choices){var item=new ComboBoxItem {Tag=option.Value};if(option.Localized)Ui.L.Bind(item,ContentControl.ContentProperty,option.Label);else item.Content=option.Label;c.Items.Add(item);if(option.Value==selected)c.SelectedItem=item;}
        c.SelectionChanged+=(_,_)=>{if(c.SelectedItem is ComboBoxItem item)set((string)item.Tag);};panel.Children.Add(c);return c;
    }
    private void ServiceSection(string id,ServiceConfig config,string title)
    {
        body.Children.Add(Ui.Text(title,20,true));
        Select(body,"ServiceType",[new("Demo","Demo",true),new("OpenAi","OpenAi",true)],config.Kind.ToString(),v=>config.Kind=Enum.Parse<ServiceKind>(v));
        Ui.Edit(body,"BaseUrl",config.BaseUrl,v=>config.BaseUrl=v.Trim());Ui.Edit(body,"Model",config.Model,v=>config.Model=v.Trim());
        Ui.Edit(body,"Timeout",config.TimeoutSeconds.ToString(),v=>{timeoutValid[id]=int.TryParse(v,out var n);if(timeoutValid[id])config.TimeoutSeconds=n;}).MaxLength=3;
        Ui.Label(body,"ApiKey");var key=new PasswordBox {Padding=new(8),MinHeight=36,MaxLength=8192,Margin=new(0,0,0,6)};
        Ui.L.Bind(key,System.Windows.Automation.AutomationProperties.NameProperty,"ApiKey");body.Children.Add(key);if(id=="stt")sttKey=key;else llmKey=key;
        var keyState=Ui.Text("KeyMissing",12);try {Ui.L.Bind(keyState,TextBlock.TextProperty,string.IsNullOrEmpty(app.Secrets.Get(id))?"KeyMissing":"KeySaved");}catch {Ui.L.Bind(keyState,TextBlock.TextProperty,"SecretError");}body.Children.Add(keyState);
        var actions=new WrapPanel();
        actions.Children.Add(Ui.Button("DeleteKey",()=>{try{app.Secrets.Delete(id);key.Clear();Ui.L.Bind(keyState,TextBlock.TextProperty,"KeyMissing");}catch{feedback.Text=Ui.L["SecretError"];} }));
        actions.Children.Add(Ui.Button(id=="stt"?"TestStt":"TestLlm",()=>_=TestAsync(id,config,key)));body.Children.Add(actions);
    }
    private async Task TestAsync(string id,ServiceConfig config,PasswordBox key)
    {
        if(testing)return;
        if(!app.Settings.PrivacyAccepted && config.Kind!=ServiceKind.Demo){feedback.Text=Ui.L["Privacy"]+"\n"+Ui.L["Accept"];return;}
        testing=true;feedback.Text=Ui.L["Testing"];
        try
        {
            if(ConfigurationValidator.ValidateService(config).Count>0)throw new ServiceException(ServiceErrorCode.InvalidConfiguration);
            var secret=config.Kind==ServiceKind.Demo?"":key.Password.Length>0?key.Password:app.Secrets.Get(id)??"";
            if(id=="stt") {var service=ServiceFactory.CreateSpeech(config with {},secret);try{await service.TestAsync(lifetime.Token);}finally{(service as IDisposable)?.Dispose();}}
            else {var service=ServiceFactory.CreateRewrite(config with {},secret);try{await service.TestAsync(lifetime.Token);}finally{(service as IDisposable)?.Dispose();}}
            if(!lifetime.IsCancellationRequested)feedback.Text=Ui.L["TestPassed"];
        }
        catch(OperationCanceledException){}
        catch(ServiceException e){if(!lifetime.IsCancellationRequested)feedback.Text=Ui.L[AppController.ErrorKey(e.Code)];}
        catch {if(!lifetime.IsCancellationRequested)feedback.Text=Ui.L["Unknown"];}
        finally {testing=false;}
    }
    private void Save()
    {
        if(testing)return;
        if(timeoutValid.Values.Any(valid=>!valid)||ConfigurationValidator.Validate(draft).Count>0){feedback.Text=Ui.L["InvalidConfiguration"];return;}
        try
        {
            if(sttKey.Password.Length>0)app.Secrets.Set("stt",sttKey.Password);
            if(llmKey.Password.Length>0)app.Secrets.Set("llm",llmKey.Password);
            draft.PrivacyAccepted=true;draft.LiveSummary=false;app.Store.SaveSettings(draft);app.Store.SavePreferences(preferences);
            app.Settings=draft;app.Preferences=preferences;app.SaveSettings();sttKey.Clear();llmKey.Clear();Close();
        }
        catch(ServiceException e){feedback.Text=Ui.L[AppController.ErrorKey(e.Code)];}
        catch {feedback.Text=Ui.L["SecretError"];}
    }
}
