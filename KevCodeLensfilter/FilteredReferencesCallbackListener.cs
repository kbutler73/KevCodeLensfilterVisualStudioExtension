using System.ComponentModel.Composition;
using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Language.CodeLens;
using Microsoft.VisualStudio.Language.CodeLens.Remoting;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Utilities;

namespace KevCodeLensfilter;

[Export(typeof(ICodeLensCallbackListener))]
[ContentType("CSharp")]
public sealed class FilteredReferencesCallbackListener : ICodeLensCallbackListener
{
    public FilteredReferencesCallbackListener()
    {
    }

    private static readonly HashSet<string> TestMethodAttributes = new(StringComparer.Ordinal)
    {
        "NUnit.Framework.TestAttribute",
        "NUnit.Framework.TestCaseAttribute",
        "NUnit.Framework.TestCaseSourceAttribute",
        "NUnit.Framework.TheoryAttribute",
        "NUnit.Framework.SetUpAttribute",
        "NUnit.Framework.TearDownAttribute",
        "NUnit.Framework.OneTimeSetUpAttribute",
        "NUnit.Framework.OneTimeTearDownAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.DataTestMethodAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.TestInitializeAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.TestCleanupAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.ClassInitializeAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.ClassCleanupAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.AssemblyInitializeAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.AssemblyCleanupAttribute",
        "Xunit.FactAttribute",
        "Xunit.TheoryAttribute",
    };

    private static readonly HashSet<string> TestClassAttributes = new(StringComparer.Ordinal)
    {
        "NUnit.Framework.TestFixtureAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute",
    };

    public async Task<string[][]> GetFilteredReferencesAsync(
        CodeLensDescriptor descriptor,
        CodeLensDescriptorContext descriptorContext,
        CancellationToken cancellationToken)
    {
        var workspace = GetWorkspace();
        var solution = workspace.CurrentSolution;
        var documentIds = solution.GetDocumentIdsWithFilePath(descriptor.FilePath);
        var documentId = documentIds.SingleOrDefault(id => id.ProjectId.Id == descriptor.ProjectGuid);

        if (documentId is null && documentIds.Length == 1)
        {
            documentId = documentIds[0];
        }

        if (documentId is null)
        {
            throw new InvalidOperationException($"Could not find the Code Lens document '{descriptor.FilePath}' in the current solution.");
        }

        var document = solution.GetDocument(documentId)
            ?? throw new InvalidOperationException($"The Code Lens document '{descriptor.FilePath}' is unavailable.");
        var span = descriptorContext.ApplicableSpan
            ?? throw new InvalidOperationException("The Code Lens request did not include a source span.");
        var sourceSpan = new TextSpan(span.Start, span.Length);
        var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Could not read syntax for '{descriptor.FilePath}'.");
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Could not resolve symbols for '{descriptor.FilePath}'.");
        var syntaxNode = syntaxRoot.FindNode(sourceSpan, getInnermostNodeForTie: true);
        ISymbol? targetSymbol = null;

        for (var current = syntaxNode; current is not null; current = current.Parent)
        {
            targetSymbol = semanticModel.GetDeclaredSymbol(current, cancellationToken);
            if (targetSymbol is not null)
            {
                break;
            }
        }

        if (targetSymbol is null)
        {
            throw new InvalidOperationException($"Could not resolve the Code Lens target '{descriptor.ElementDescription}'.");
        }

        var references = await SymbolFinder.FindReferencesAsync(targetSymbol, solution, cancellationToken).ConfigureAwait(false);
        var rows = new List<(string FilePath, int Line, string[] Fields)>();
        var seenLocations = new HashSet<string>(StringComparer.Ordinal);
        var semanticModels = new Dictionary<DocumentId, SemanticModel?>();

        foreach (var reference in references.SelectMany(group => group.Locations))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!reference.Location.IsInSource || await IsTestCodeAsync(reference, semanticModels, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var referencedDocument = reference.Document;
            var text = await referencedDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var referenceSpan = reference.Location.SourceSpan;
            var line = text.Lines.GetLineFromPosition(referenceSpan.Start);
            var uniqueLocation = $"{referencedDocument.Id}:{referenceSpan.Start}:{referenceSpan.Length}";

            if (!seenLocations.Add(uniqueLocation))
            {
                continue;
            }

            var lineStart = line.Start;
            var fields = new[]
            {
                referencedDocument.FilePath ?? referencedDocument.Name,
                line.LineNumber.ToString(CultureInfo.InvariantCulture),
                (referenceSpan.Start - lineStart).ToString(CultureInfo.InvariantCulture),
                line.ToString().TrimEnd(),
                (referenceSpan.Start - lineStart).ToString(CultureInfo.InvariantCulture),
                (referenceSpan.Start - lineStart + referenceSpan.Length).ToString(CultureInfo.InvariantCulture),
                string.Empty,
                line.LineNumber > 0 ? text.Lines[line.LineNumber - 1].ToString().TrimEnd() : string.Empty,
                line.LineNumber > 1 ? text.Lines[line.LineNumber - 2].ToString().TrimEnd() : string.Empty,
                line.LineNumber + 1 < text.Lines.Count ? text.Lines[line.LineNumber + 1].ToString().TrimEnd() : string.Empty,
                line.LineNumber + 2 < text.Lines.Count ? text.Lines[line.LineNumber + 2].ToString().TrimEnd() : string.Empty,
            };

            rows.Add((
                referencedDocument.FilePath ?? referencedDocument.Name,
                line.LineNumber,
                fields));
        }

        return rows
            .OrderBy(row => row.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Line)
            .Select(row => row.Fields)
            .ToArray();
    }

    private static async Task<bool> IsTestCodeAsync(
        ReferenceLocation reference,
        IDictionary<DocumentId, SemanticModel?> semanticModels,
        CancellationToken cancellationToken)
    {
        if (!semanticModels.TryGetValue(reference.Document.Id, out var semanticModel))
        {
            semanticModel = await reference.Document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            semanticModels.Add(reference.Document.Id, semanticModel);
        }

        if (semanticModel is null)
        {
            return false;
        }

        var symbol = semanticModel.GetEnclosingSymbol(reference.Location.SourceSpan.Start, cancellationToken);
        for (var current = symbol; current is not null; current = current.ContainingSymbol)
        {
            if (current is IMethodSymbol method && method.GetAttributes().Any(IsTestMethodAttribute))
            {
                return true;
            }

            if (current is INamedTypeSymbol type && type.GetAttributes().Any(IsTestClassAttribute))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTestMethodAttribute(AttributeData attribute) =>
        attribute.AttributeClass is not null && TestMethodAttributes.Contains(attribute.AttributeClass.ToDisplayString());

    private static bool IsTestClassAttribute(AttributeData attribute) =>
        attribute.AttributeClass is not null && TestClassAttributes.Contains(attribute.AttributeClass.ToDisplayString());

    private static Workspace GetWorkspace()
    {
        var componentModel = Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel
            ?? throw new InvalidOperationException("Visual Studio did not provide its component model service.");
        var languageServicesAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(
                assembly.GetName().Name,
                "Microsoft.VisualStudio.LanguageServices",
                StringComparison.Ordinal));

        if (languageServicesAssembly is null)
        {
            var assemblyPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "CommonExtensions",
                "Microsoft",
                "VBCSharp",
                "LanguageServices",
                "Microsoft.VisualStudio.LanguageServices.dll");
            if (!File.Exists(assemblyPath))
            {
                throw new FileNotFoundException("Could not locate Visual Studio's Roslyn language-services assembly.", assemblyPath);
            }

            languageServicesAssembly = Assembly.LoadFrom(assemblyPath);
        }

        var workspaceType = languageServicesAssembly
            .GetType("Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace", throwOnError: true)!;
        var getService = typeof(IComponentModel)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(method => method.Name == "GetService" && method.IsGenericMethodDefinition);
        var workspace = getService.MakeGenericMethod(workspaceType).Invoke(componentModel, null) as Workspace;

        return workspace
            ?? throw new InvalidOperationException("Visual Studio did not provide its Roslyn workspace service.");
    }
}
