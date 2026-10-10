using System;
using FluentEmojiKitchen.Composition;
using FluentEmojiKitchen.Configuration;
using Microsoft.UI.Xaml;
namespace FluentEmojiKitchen;

public partial class App : Application
{
    private ApplicationController? _controller;
    public App()
    {
        AppContext.SetSwitch("System.Net.DisableIPv6", true);
        ResourceManagerRequested += (_, args) => args.CustomResourceManager =
            Services.Windows.EmojiFontResources.CreateResourceManager();
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Current.DebugSettings.LayoutCycleTracingLevel = LayoutCycleTracingLevel.None;
        Current.DebugSettings.LayoutCycleDebugBreakLevel = LayoutCycleDebugBreakLevel.None;
        _controller = new ApplicationController(new AppOptions());
        _ = _controller.StartAsync();
    }
}
