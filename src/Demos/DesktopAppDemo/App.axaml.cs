using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using DesktopAppDemo.ViewModels;
using DesktopAppDemo.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using WithSalt.FFmpeg.Recorder;

namespace DesktopAppDemo
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {

            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            //Logging
            builder.Logging.ClearProviders();
            builder.Logging.AddNLog();
            builder.Services.AddTransient<MainWindowViewModel>();

            var host = builder.Build();
            var mainView = host.Services.GetRequiredService<MainWindowViewModel>();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = mainView,
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}