using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace YNABAutomationConsole.Ynab;

public static class YnabServiceCollectionExtensions
{
    public static async Task<IServiceCollection> AddYnabApi(
        this IServiceCollection services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        return await AddYnabApi(services, configuration, null, cancellationToken);
    }

    public static async Task<IServiceCollection> AddYnabApi(
        this IServiceCollection services,
        IConfiguration configuration,
        HttpClient? discoveryClient,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var resolvedOptions = CreateOptions(configuration);
        ValidateOptions(resolvedOptions);
        var requestRateLimiter = new YnabRequestRateLimiter();

        if (string.IsNullOrWhiteSpace(resolvedOptions.PlanId))
        {
            var plans = await DiscoverPlansAsync(
                resolvedOptions,
                discoveryClient,
                requestRateLimiter,
                cancellationToken);

            if (plans.Data.Plans.Count != 1)
            {
                throw new InvalidOperationException(
                    "A plan ID must be configured when the authenticated user does not have exactly one plan.");
            }

            resolvedOptions.PlanId = plans.Data.Plans[0].Id.ToString();
        }

        services.AddOptions<YnabOptions>()
            .Configure(options =>
            {
                options.BaseUrl = resolvedOptions.BaseUrl;
                options.ApiKey = resolvedOptions.ApiKey;
                options.PlanId = resolvedOptions.PlanId;
            })
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _),
                "Ynab:BaseUrl must be an absolute URI.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey),
                "Ynab:ApiKey must be configured.")
            .ValidateOnStart();

        services.AddTransient<YnabAuthenticationHandler>();
        services.AddSingleton<IYnabRequestRateLimiter>(requestRateLimiter);
        services.AddTransient<YnabRateLimitHandler>();
        services.AddHttpClient<IYnabApiClient, YnabApiClient>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<YnabOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
            })
            .AddHttpMessageHandler<YnabAuthenticationHandler>()
            .AddHttpMessageHandler<YnabRateLimitHandler>();

        return services;
    }

    private static async Task<PlansResponse> DiscoverPlansAsync(
        YnabOptions options,
        HttpClient? discoveryClient,
        IYnabRequestRateLimiter requestRateLimiter,
        CancellationToken cancellationToken)
    {
        if (discoveryClient is not null)
        {
            await requestRateLimiter.WaitAsync(cancellationToken);
            var apiClient = new YnabApiClient(discoveryClient, Options.Create(options));
            return await apiClient.GetPlansAsync(cancellationToken: cancellationToken);
        }

        HttpMessageHandler innerHandler;
        Uri baseAddress;
        innerHandler = new YnabAuthenticationHandler(Options.Create(options))
        {
            InnerHandler = new HttpClientHandler()
        };
        baseAddress = new Uri(options.BaseUrl, UriKind.Absolute);

        using var client = new HttpClient(
            new YnabRateLimitHandler(requestRateLimiter)
            {
                InnerHandler = innerHandler
            })
        {
            BaseAddress = baseAddress
        };
        var clientForDiscovery = new YnabApiClient(client, Options.Create(options));
        return await clientForDiscovery.GetPlansAsync(cancellationToken: cancellationToken);
    }

    private static YnabOptions CreateOptions(IConfiguration configuration)
    {
        var options = new YnabOptions();
        configuration.GetSection(YnabOptions.SectionName).Bind(options);
        return options;
    }

    private static void ValidateOptions(YnabOptions options)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
        {
            throw new OptionsValidationException(
                YnabOptions.SectionName,
                typeof(YnabOptions),
                ["Ynab:BaseUrl must be an absolute URI."]);
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new OptionsValidationException(
                YnabOptions.SectionName,
                typeof(YnabOptions),
                ["Ynab:ApiKey must be configured."]);
        }
    }
}
