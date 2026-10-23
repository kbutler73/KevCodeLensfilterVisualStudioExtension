using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.CodeLens;
using Microsoft.VisualStudio.Language.CodeLens.Remoting;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;

namespace KevCodeLensfilter.Provider;

[Export(typeof(IAsyncCodeLensDataPointProvider))]
[Name(ProviderId)]
[ContentType("code")]
[LocalizedName(typeof(ProviderResources), nameof(ProviderResources.FilteredReferences))]
[DetailsTemplateName("references")]
[Priority(210)]
internal sealed class FilteredReferencesDataPointProvider : IAsyncCodeLensDataPointProvider
{
    internal const string ProviderId = "KevCodeLensfilter.FilteredReferences";
    private readonly Lazy<ICodeLensCallbackService> callbackService;

    [ImportingConstructor]
    public FilteredReferencesDataPointProvider(Lazy<ICodeLensCallbackService> callbackService)
    {
        this.callbackService = callbackService;
    }

    public Task<bool> CanCreateDataPointAsync(
        CodeLensDescriptor descriptor,
        CodeLensDescriptorContext descriptorContext,
        CancellationToken cancellationToken)
    {
        var supportedKinds = CodeElementKinds.Member | CodeElementKinds.Type;
        var supportsKind = (descriptor.Kind & supportedKinds) != 0;
        return Task.FromResult(supportsKind && descriptorContext.ApplicableSpan.HasValue);
    }

    public Task<IAsyncCodeLensDataPoint> CreateDataPointAsync(
        CodeLensDescriptor descriptor,
        CodeLensDescriptorContext descriptorContext,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IAsyncCodeLensDataPoint>(
            new FilteredReferencesDataPoint(this, this.callbackService, descriptor));
    }

    private sealed class FilteredReferencesDataPoint : IAsyncCodeLensDataPoint
    {
        private static readonly IReadOnlyList<CodeLensDetailHeaderDescriptor> Headers =
        [
            new CodeLensDetailHeaderDescriptor { UniqueName = "filePath" },
            new CodeLensDetailHeaderDescriptor { UniqueName = "lineNumber" },
            new CodeLensDetailHeaderDescriptor { UniqueName = "columnNumber" },
            new CodeLensDetailHeaderDescriptor { UniqueName = "referenceText" },
            new CodeLensDetailHeaderDescriptor { UniqueName = "referenceStart" },
            new CodeLensDetailHeaderDescriptor { UniqueName = "referenceEnd" },
            new CodeLensDetailHeaderDescriptor { UniqueName = "referenceLongDescription" },
            new CodeLensDetailHeaderDescriptor { UniqueName = "referenceImageId" },
            new CodeLensDetailHeaderDescriptor { UniqueName = "textBeforeReference2" },
            new CodeLensDetailHeaderDescriptor { UniqueName = "textBeforeReference1" },
            new CodeLensDetailHeaderDescriptor { UniqueName = "textAfterReference1" },
            new CodeLensDetailHeaderDescriptor { UniqueName = "textAfterReference2" },
        ];

        private readonly FilteredReferencesDataPointProvider provider;
        private readonly Lazy<ICodeLensCallbackService> callbackService;
        private readonly CodeLensDescriptor descriptor;

        public FilteredReferencesDataPoint(
            FilteredReferencesDataPointProvider provider,
            Lazy<ICodeLensCallbackService> callbackService,
            CodeLensDescriptor descriptor)
        {
            this.provider = provider;
            this.callbackService = callbackService;
            this.descriptor = descriptor;
        }

        public event AsyncEventHandler? InvalidatedAsync;

        public CodeLensDescriptor Descriptor => this.descriptor;

        public async Task<CodeLensDataPointDescriptor?> GetDataAsync(
            CodeLensDescriptorContext descriptorContext,
            CancellationToken cancellationToken)
        {
            var references = await this.GetReferencesAsync(descriptorContext, cancellationToken).ConfigureAwait(false);
            var count = references.Length;

            return new CodeLensDataPointDescriptor
            {
                Description = $"{count} non-test reference{(count == 1 ? string.Empty : "s")}",
                IntValue = count,
                TooltipText = "References excluding NUnit, MSTest, and xUnit test methods",
            };
        }

        public async Task<CodeLensDetailsDescriptor?> GetDetailsAsync(
            CodeLensDescriptorContext descriptorContext,
            CancellationToken cancellationToken)
        {
            var references = await this.GetReferencesAsync(descriptorContext, cancellationToken).ConfigureAwait(false);
            var entries = references.Select(reference => new CodeLensDetailEntryDescriptor
            {
                Fields =
                [
                    new CodeLensDetailEntryField { Text = reference[0] },
                    new CodeLensDetailEntryField { Text = reference[1] },
                    new CodeLensDetailEntryField { Text = reference[2] },
                    new CodeLensDetailEntryField { Text = reference[3] },
                    new CodeLensDetailEntryField { Text = reference[4] },
                    new CodeLensDetailEntryField { Text = reference[5] },
                    new CodeLensDetailEntryField { Text = reference[6] },
                    new CodeLensDetailEntryField { ImageId = default },
                    new CodeLensDetailEntryField { Text = reference[7] },
                    new CodeLensDetailEntryField { Text = reference[8] },
                    new CodeLensDetailEntryField { Text = reference[9] },
                    new CodeLensDetailEntryField { Text = reference[10] },
                ],
            }).ToList();

            return new CodeLensDetailsDescriptor
            {
                Headers = Headers,
                Entries = entries,
            };
        }

        public void Dispose()
        {
        }

        private Task<string[][]> GetReferencesAsync(
            CodeLensDescriptorContext descriptorContext,
            CancellationToken cancellationToken)
        {
            return this.callbackService.Value.InvokeAsync<string[][]>(
                this,
                "GetFilteredReferencesAsync",
                new object[] { this.descriptor, descriptorContext },
                cancellationToken);
        }
    }
}
