using System;
using System.Threading;
using System.Windows;

namespace RestartWatch;

public static class Program
{
    private const string MutexName = "Global\\RestartWatch_SingleInstance_Mutex";

    [STAThread]
    public static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out bool createdNew);

        if (!createdNew)
        {
            // Another instance is already running
            System.Windows.MessageBox.Show("Restart Watch is already running.", "Restart Watch",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
