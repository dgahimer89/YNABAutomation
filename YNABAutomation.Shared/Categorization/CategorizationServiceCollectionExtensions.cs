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
        services.AddSingleton<IProposedChangeWriter, ConsoleProposedChangeWriter>();
        services.AddSingleton<PayeeNormalizer>();
        services.AddScoped<CategoryCandidateSelector>();
        services.AddScoped<AutoApplyPolicy>();
        services.AddScoped<YnabCategorizationProcessor>();
        services.AddScoped<ManualTransactionResolutionService>();
        return services;
    }
}
