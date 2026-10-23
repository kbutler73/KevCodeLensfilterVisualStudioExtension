using System.Globalization;
using System.Resources;

namespace KevCodeLensfilter.Provider;

public static class ProviderResources
{
    private static readonly ResourceManager resourceManager = new(
        "KevCodeLensfilter.Provider.ProviderResources",
        typeof(ProviderResources).Assembly);

    public static ResourceManager ResourceManager => resourceManager;

    public static CultureInfo? Culture { get; set; }

    public static string FilteredReferences =>
        resourceManager.GetString(nameof(FilteredReferences), Culture)
        ?? throw new InvalidOperationException("The FilteredReferences resource is missing.");
}
