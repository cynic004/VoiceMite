using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using VoiceMite.Core;

namespace VoiceMite.App;

internal sealed class FloatingWindow : Window
{
    private readonly AppController app;
    private readonly TextBlock symbol,caption;
    private readonly Border orb;
    private Point start;
    private double startLeft,startTop;
    private bool dragging;
    internal FloatingWindow(AppController app)
    {
        this.app=app;
        Title="VoiceMite"; Width=86; Height=100; WindowStyle=WindowStyle.None; AllowsTransparency=true;
        Background=Brushes.Transparent; ResizeMode=ResizeMode.NoResize; ShowInTaskbar=false; Topmost=true; ShowActivated=false;
        Left=app.Settings.FloatLeft; Top=app.Settings.FloatTop;
        var stack=new StackPanel { Margin=new(10), Background=Brushes.Transparent };
        orb=new Border { Width=60,Height=60,CornerRadius=new(30),Background=new LinearGradientBrush(Color.FromRgb(237,250,255),Color.FromRgb(174,221,252),90),BorderBrush=new SolidColorBrush(Color.FromRgb(132,197,239)),BorderThickness=new(1),Effect=new DropShadowEffect { Color=Color.FromRgb(102,190,251),BlurRadius=18,ShadowDepth=0,Opacity=.5 }, Cursor=Cursors.Hand };
        symbol=new TextBlock { Text="●",FontSize=25,FontWeight=FontWeights.Bold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Foreground=Ui.Blue };
        // Segoe MDL2 Assets includes a microphone without depending on emoji font rendering.
        symbol.FontFamily=new("Segoe MDL2 Assets"); symbol.Text="\uE720";
        orb.Child=symbol; stack.Children.Add(orb);
        caption=new TextBlock { FontSize=10,HorizontalAlignment=HorizontalAlignment.Center,Foreground=Ui.Ink,Margin=new(0,4,0,0),Background=new SolidColorBrush(Color.FromArgb(225,245,251,255)),Text="●" };
        stack.Children.Add(caption); Content=stack;
        orb.MouseLeftButtonDown+=(_,e)=> { start=PointToScreen(e.GetPosition(this));startLeft=Left;startTop=Top;dragging=false;orb.CaptureMouse(); e.Handled=true; };
        orb.MouseMove+=(_,e)=>
        {
            if(!orb.IsMouseCaptured || e.LeftButton!=MouseButtonState.Pressed) return;
            var now=PointToScreen(e.GetPosition(this)); var dpi=VisualTreeHelper.GetDpi(this);
            if((now-start).Length>5) dragging=true;
            if(dragging) {Left=startLeft+(now.X-start.X)/dpi.DpiScaleX;Top=startTop+(now.Y-start.Y)/dpi.DpiScaleY;}
        };
        orb.MouseLeftButtonUp+=(_,e)=>
        {
            if(!orb.IsMouseCaptured) return;orb.ReleaseMouseCapture();
            if(dragging) {ClampToScreen();app.Settings.FloatLeft=Left;app.Settings.FloatTop=Top; try {app.Store.SaveSettings(app.Settings);}catch{} }
            else app.ToggleRecord(); e.Handled=true;
        };
        MouseRightButtonUp+=(_,_)=>
        {
            var menu=new ContextMenu();
            foreach(var entry in new (string,Action)[]{("Show",app.ShowPanel),("Settings",app.OpenSettings),("Exit",app.Exit)})
            {var item=new MenuItem();Ui.L.Bind(item,HeaderedItemsControl.HeaderProperty,entry.Item1);item.Click+=(_,_)=>entry.Item2();menu.Items.Add(item);}
            menu.IsOpen=true;
        };
        SourceInitialized+=(_,_)=>
        {
            var source=(HwndSource)PresentationSource.FromVisual(this); var handle=source.Handle;
            SetWindowLongPtr(handle,-20,new IntPtr(GetWindowLongPtr(handle,-20).ToInt64()|0x08000000L|0x00000080L));
            source.AddHook((IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled)=> {if(msg==0x21){handled=true;return new IntPtr(3);}return IntPtr.Zero;});
            ClampToScreen();
        };
        UpdateState();
    }
    internal void ClampToScreen()
    {
        var dpi=VisualTreeHelper.GetDpi(this); var point=new System.Drawing.Point((int)(Left*dpi.DpiScaleX),(int)(Top*dpi.DpiScaleY));
        var area=System.Windows.Forms.Screen.FromPoint(point).WorkingArea;
        Left=Math.Clamp(Left,area.Left/dpi.DpiScaleX,Math.Max(area.Left/dpi.DpiScaleX,area.Right/dpi.DpiScaleX-Width));
        Top=Math.Clamp(Top,area.Top/dpi.DpiScaleY,Math.Max(area.Top/dpi.DpiScaleY,area.Bottom/dpi.DpiScaleY-Height));
    }
    internal void UpdateState()
    {
        var state=app.Session.State;
        if(app.SelectingTarget){symbol.Text="\uE73E";caption.Text=Ui.L["SelectTargetShort"];ToolTip=Ui.L["SelectTargetInstructions"];return;}
        symbol.Text=state switch {SessionState.Recording=>"\uE71A",SessionState.Ready=>"\uE73E",SessionState.Error=>"\uE783",SessionState.Clarifying=>"\uE897",SessionState.Transcribing or SessionState.Formatting=>"\uE895",_=>"\uE720"};
        caption.Text=state==SessionState.Recording?$"● {TimeSpan.FromSeconds(Math.Max(0,app.Elapsed)):m\\:ss}":state==SessionState.Ready?"✓":state==SessionState.Clarifying?"?":"VoiceMite";
        ToolTip=Ui.L[state.ToString()];
        System.Windows.Automation.AutomationProperties.SetName(this,Ui.L[state.ToString()]);
        orb.BorderThickness=new(app.Settings.HighContrast?3:1);
        orb.Effect=app.Settings.HighContrast?null:new DropShadowEffect {Color=Color.FromRgb(102,190,251),BlurRadius=18,ShadowDepth=0,Opacity=.5};
    }
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]private static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
}
