using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pmad.Git.HttpServer;

/// <summary>
/// Extension methods for adding Git Smart HTTP services to the dependency injection container.
/// </summary>
public static class GitSmartHttpServiceCollectionExtensions
{
    /// <summary>
    /// Adds Git Smart HTTP services to the specified <see cref="IServiceCollection"/>.
    /// The service is registered as a singleton.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="options">The Git Smart HTTP configuration options.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="options"/> is null.</exception>
    public static IServiceCollection AddGitSmartHttp(this IServiceCollection services, GitSmartHttpOptions options)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        services.TryAddSingleton(options);
        services.TryAddSingleton<GitSmartHttpService>();

        return services;
    }

    /// <summary>
    /// Adds Git Smart HTTP services to the specified <see cref="IServiceCollection"/>.
    /// The service is registered as a singleton.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="configureOptions">A delegate to configure the <see cref="GitSmartHttpOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> or <paramref name="configureOptions"/> is null.</exception>
    public static IServiceCollection AddGitSmartHttp(this IServiceCollection services, Action<GitSmartHttpOptions> configureOptions)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configureOptions is null)
        {
            throw new ArgumentNullException(nameof(configureOptions));
        }

        var options = new GitSmartHttpOptions();
        configureOptions(options);

        return services.AddGitSmartHttp(options);
    }
}
