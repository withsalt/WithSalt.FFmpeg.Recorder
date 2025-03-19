using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using DesktopAppDemo.Views;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;

namespace DesktopAppDemo.Utils
{
    internal class MessageBox
    {
        public static MainWindow MainWindow
        {
            get
            {
                return (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow
                                            ?? throw new InvalidOperationException();
            }
        }

        public static async Task Info(string title, string content)
        {
            var box = new MessageBoxStandardParams
            {
                ContentTitle = title,
                ContentMessage = content,
                ButtonDefinitions = ButtonEnum.Ok,
                Icon = Icon.Info,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Width = 300,
                MaxWidth = 300,
                Topmost = true,
            };
            await MessageBoxManager.GetMessageBoxStandard(box).ShowAsync();
        }

        public static async Task Warning(string title, string content, Window? window = null)
        {
            var box = new MessageBoxStandardParams
            {
                ContentTitle = title,
                ContentMessage = content,
                ButtonDefinitions = ButtonEnum.Ok,
                Icon = Icon.Warning,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Width = 300,
                MaxWidth = 300,
                Topmost = true,
            };
            await MessageBoxManager.GetMessageBoxStandard(box).ShowWindowDialogAsync(window ?? MainWindow);
        }
    }
}
