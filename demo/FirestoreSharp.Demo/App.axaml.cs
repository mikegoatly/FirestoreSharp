using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using FirestoreSharp.Demo.Services;
using FirestoreSharp.Demo.ViewModels;
using FirestoreSharp.Demo.Views;

namespace FirestoreSharp.Demo;

public sealed class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var firestoreService = new FirestoreService();
            var viewModel = new MainViewModel(firestoreService);
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
