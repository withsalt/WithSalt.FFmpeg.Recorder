using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using DesktopAppDemo.ViewModels;

namespace DesktopAppDemo.Views
{
    public partial class MainWindow : Window
    {
        private bool _closing;
        private bool _cleanupComplete;

        public MainWindow()
        {
            InitializeComponent();
            Closing += OnWindowClosing;
        }

        private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
        {
            if (_cleanupComplete) return;
            e.Cancel = true;
            if (_closing) return;
            _closing = true;

            // Finish this Closing event before retrying Close, even if no recording is active.
            // Keep the dispatcher running while decoding and queued rendering finish.
            await Task.Yield();
            try
            {
                if (DataContext is MainWindowViewModel viewModel)
                    await viewModel.ClosingAsync();
            }
            catch (Exception ex)
            {
                Trace.TraceError("Window cleanup failed: {0}", ex);
            }
            finally
            {
                _cleanupComplete = true;
                Close();
            }
        }
    }
}
