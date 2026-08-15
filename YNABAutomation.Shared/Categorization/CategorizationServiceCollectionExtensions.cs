using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace YNABAutomationConsole.Categorization;

public static class CategorizationServiceCollectionExtensions
{
    public static IServiceCollection AddCategorization(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<CategorizationOptions>()
            .Bind(configuration.GetSection(CategorizationOptions.SectionName))
            .Validate(options => options.MinimumLearnedSampleSize > 0,
                "Categorization:MinimumLearnedSampleSize must be greater than zero.")
            .Validate(options => options.MinimumLearnedConsistency is >= 0 and <= 1,
                "Categorization:MinimumLearnedConsistency must be between zero and one.")
            .ValidateOnStart();
        services.AddOptions<OpenAiOptions>()
            .Bind(configuration.GetSection(OpenAiOptions.SectionName))
            .Validate(options => options.AutoApplyConfidenceThreshold is >= 0 and <= 1,
                "OpenAI:AutoApplyConfidenceThreshold must be between zero and one.")
            .Validate(options => options.MaximumHistoricalObservations > 0,
                "OpenAI:MaximumHistoricalObservations must be greater than zero.")
            .ValidateOnStart();
        services.AddOptions<TransferOptions>()
            .Bind(configuration.GetSection(TransferOptions.SectionName))
            .Validate(options => options.MatchingDateWindowDays >= 0,
                "Transfers:MatchingDateWindowDays must not be negative.")
            .ValidateOnStart();
        services.AddSingleton<IProposedChangeWriter, ConsoleProposedChangeWriter>();
        services.AddSingleton<PayeeNormalizer>();
        services.AddScoped<CategoryCandidateSelector>();
        services.AddScoped<AutoApplyPolicy>();
        services.AddScoped<YnabCategorizationProcessor>();
        services.AddScoped<TransferReconciliationService>();
        services.AddScoped<ManualTransactionResolutionService>();
        var openAiOptions = configuration.GetSection(OpenAiOptions.SectionName).Get<OpenAiOptions>() ?? new();
        if (string.IsNullOrWhiteSpace(openAiOptions.ApiKey))
        {
            services.AddSingleton<IAiCategorizer, DisabledAiCategorizer>();
        }
        else
        {
            services.AddSingleton<IAiCategorizer>(new OpenAiCategorizer(openAiOptions));
        }
        return services;
    }
}
