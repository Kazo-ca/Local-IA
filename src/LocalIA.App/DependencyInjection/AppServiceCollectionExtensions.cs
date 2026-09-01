using LocalIA.App.Chat;
using LocalIA.App.Navigation;
using LocalIA.App.Router;
using LocalIA.App.ViewModels;
using LocalIA.App.Views;
using LocalIA.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LocalIA.App.DependencyInjection;

public static class AppServiceCollectionExtensions
{
    public static IServiceCollection AddLocalIaApp(this IServiceCollection services)
    {
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ChatEngineClientResolver>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ChatViewModel>();
        services.AddSingleton<ProfilesViewModel>();
        services.AddSingleton<ConfigurationAdvisorViewModel>();
        services.AddSingleton<ModelConfigurationViewModel>();
        services.AddSingleton<MoeViewModel>();
        services.AddSingleton<HuggingFaceSearchViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<RouterHistoryViewModel>();
        services.AddSingleton<IRouterConflictPrompter, RouterConflictPrompter>();
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<MainWindow>();
        services.AddTransient<NewModelWizardViewModel>();
        services.AddTransient<NewModelWizardWindow>();

        return services;
    }
}
