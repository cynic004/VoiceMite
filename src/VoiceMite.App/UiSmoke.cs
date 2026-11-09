using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VoiceMite.Core;

namespace VoiceMite.App;

/// <summary>Developer-only visual and controller smoke checks. Never contacts a real service.</summary>
internal static class UiSmoke
{
    internal static bool Failed {get;private set;}
    internal static async Task RunAsync(AppController controller,string directory)
    {
        var results=new List<object>();Directory.CreateDirectory(directory);
        void Check(string name,bool passed)=>results.Add(new {name,status=passed?"passed":"failed"});
        try
        {
            controller.Settings.Stt.Kind=ServiceKind.Demo;controller.Settings.Llm.Kind=ServiceKind.Demo;
            controller.Settings.TranscribeOnly=false;controller.Settings.Mode=InputMode.Auto;controller.DemoSample();await Task.Delay(120);
            Check("Demo sample is explicitly local and asks for purpose",controller.Session.State==SessionState.Clarifying&&controller.Session.Result?.Question is not null);
            await controller.RewriteAsync(InputMode.Verbatim);
            Check("Original wording is preserved without LLM key",controller.Session.State==SessionState.Ready&&controller.Session.Result?.Text==DemoSpeechService.Example);
            foreach(var language in new[]{"zh-CN","en"})
            {
                controller.Settings.UiLanguage=language;Ui.L.SetLanguage(language);controller.Refresh();controller.ShowPanel();
                controller.Panel.Height=850;controller.Panel.Width=520;await Task.Delay(80);
                Render(controller.Panel,Path.Combine(directory,$"panel-{language}.png"));
                controller.Message("TargetChanged");Render(controller.Panel,Path.Combine(directory,$"error-{language}.png"));controller.NoticeKey=null;
                var settings=new SettingsWindow(controller) {Owner=controller.Panel};settings.Show();await Task.Delay(80);
                Render(settings,Path.Combine(directory,$"settings-{language}.png"));
                Check(language+" panel and settings render",controller.Panel.ActualWidth>=420&&settings.ActualWidth>=480);
                Ui.L.SetLanguage(language=="en"?"zh-CN":"en");await Task.Delay(50);
                var options=Descendants(settings).OfType<ComboBox>().SelectMany(x=>x.Items.OfType<ComboBoxItem>()).Where(x=>x.Tag?.ToString()=="Demo").ToList();
                Check(language+" service options update immediately on language change",options.Count==2&&options.All(x=>x.Content?.ToString()==Ui.L["Demo"]));
                settings.Close();
            }
            controller.Settings.Llm.Kind=ServiceKind.OpenAi;
            controller.Settings.TranscribeOnly=true;
            await controller.RewriteAsync(InputMode.Verbatim);
            Check("Unconfigured LLM does not block transcribe-only",controller.Session.State==SessionState.Ready);
            var draft=controller.Panel.ResultText;await controller.SelectTargetAsync();
            Check("Mouse target selection preserves the draft without recording",controller.SelectingTarget&&!controller.Audio.IsRecording&&controller.Panel.ResultText==draft);
            controller.Cancel();await Task.Delay(50);
            Check("Cancel clears editors and target",controller.Panel.ResultText==""&&controller.Panel.TranscriptText==""&&!controller.Targets.HasTarget);
            var cancelled=await controller.Targets.InsertAsync("MUST NOT BE INSERTED",new CancellationToken(true));
            Check("Worker refuses a pre-cancelled insertion",!cancelled.Success&&cancelled.Code=="Cancelled"&&!controller.Targets.HasTarget);
            Check("Worker Clear returns without blocking",RunClear(controller));
        }
        catch(Exception e){results.Add(new {name="UI smoke exception",status="failed",detail=e.GetType().Name});}
        var json=JsonSerializer.Serialize(results,new JsonSerializerOptions {WriteIndented=true});
        Failed=json.Contains("\"failed\"",StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(directory,"ui-tests.json"),json);
        controller.Exit();
    }
    private static bool RunClear(AppController app){var clock=System.Diagnostics.Stopwatch.StartNew();app.Targets.Clear();return clock.ElapsedMilliseconds<100;}
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}
    }
    private static void Render(Window window,string path)
    {
        window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);
    }
}
