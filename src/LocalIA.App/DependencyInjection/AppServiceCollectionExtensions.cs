using LocalIA.App.Chat;
using LocalIA.App.Navigation;
using LocalIA.App.ViewModels;
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
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<MainWindow>();

        return services;
    }
}
