using System.Windows;

namespace PKTWinNode;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        SetupGlobalExceptionHandlers();
        base.OnStartup(e);
    }

    private void SetupGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (s, args) =>
        {
            System.Windows.MessageBox.Show(
                $"An unexpected error occurred in the application.\n\nError: {args.Exception.Message}",
                "Application Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            args.Handled = true;
        };

        System.AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as System.Exception;
            System.Windows.MessageBox.Show(
                $"A critical error occurred in the application.\n\nError: {ex?.Message ?? "Unknown error"}",
                "Critical Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            System.Windows.MessageBox.Show(
                $"An error occurred in a background task.\n\nError: {args.Exception.InnerException?.Message ?? args.Exception.Message}",
                "Background Task Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            args.SetObserved();
        };
    }
}

