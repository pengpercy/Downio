using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Downio.Generators;

[Generator]
public class ViewLocatorGenerator : IIncrementalGenerator
{
    private sealed class NamedTypeSymbolComparer : IEqualityComparer<INamedTypeSymbol>
    {
        public bool Equals(INamedTypeSymbol? x, INamedTypeSymbol? y)
        {
            return SymbolEqualityComparer.Default.Equals(x, y);
        }

        public int GetHashCode(INamedTypeSymbol obj)
        {
            return SymbolEqualityComparer.Default.GetHashCode(obj);
        }
    }

    private static readonly IEqualityComparer<INamedTypeSymbol> TypeComparer = new NamedTypeSymbolComparer();

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. Find all classes ending with "ViewModel"
        var viewModels = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: (s, _) => IsClassEndingWith(s, "ViewModel"),
                transform: (ctx, _) => GetClassSymbol(ctx))
            .Where(m => m != null);

        // 2. Find all classes ending with "View" or "Window"
        var views = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: (s, _) => IsClassEndingWith(s, "View") || IsClassEndingWith(s, "Window"),
                transform: (ctx, _) => GetClassSymbol(ctx))
            .Where(m => m != null);

        // 3. Combine them
        var compilationAndClasses = context.CompilationProvider
            .Combine(viewModels.Collect())
            .Combine(views.Collect());

        // 4. Generate source
        context.RegisterSourceOutput(compilationAndClasses, (spc, source) =>
        {
            var compilation = source.Left.Left;
            var vmSymbols = source.Left.Right;
            var viewSymbols = source.Right;

            Execute(spc, vmSymbols, viewSymbols);
        });
    }

    private static bool IsClassEndingWith(SyntaxNode node, string suffix)
    {
        return node is ClassDeclarationSyntax c && c.Identifier.Text.EndsWith(suffix);
    }

    private static INamedTypeSymbol? GetClassSymbol(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);
        
        if (symbol is INamedTypeSymbol namedType && 
            !namedType.IsAbstract && 
            namedType.DeclaredAccessibility == Accessibility.Public)
        {
            return namedType;
        }
        return null;
    }

    private void Execute(SourceProductionContext context, 
        System.Collections.Immutable.ImmutableArray<INamedTypeSymbol?> viewModels, 
        System.Collections.Immutable.ImmutableArray<INamedTypeSymbol?> views)
    {
        var uniqueViewModels = viewModels
            .Where(vm => vm is not null)
            .Select(vm => vm!)
            .Distinct(TypeComparer)
            .ToList();

        var uniqueViews = views
            .Where(v => v is not null)
            .Select(v => v!)
            .Distinct(TypeComparer)
            .ToList();

        var mappings = new List<(INamedTypeSymbol ViewModel, INamedTypeSymbol View)>();

        foreach (var vm in uniqueViewModels)
        {
            var vmName = vm.Name;
            var coreName = vmName.Substring(0, vmName.Length - "ViewModel".Length);
            
            // Try finding exact match for coreName (e.g. MainWindow)
            var view = uniqueViews.FirstOrDefault(v => v.Name == coreName);
            
            // If not found, try coreName + "View"
            if (view == null)
            {
                view = uniqueViews.FirstOrDefault(v => v.Name == coreName + "View");
            }

            if (view != null)
            {
                mappings.Add((vm, view));
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using Avalonia.Controls;");
        sb.AppendLine("using Downio.ViewModels;");
        sb.AppendLine("using Downio.Views;");
        sb.AppendLine();
        sb.AppendLine("namespace Downio");
        sb.AppendLine("{");
        sb.AppendLine("    public partial class ViewLocator");
        sb.AppendLine("    {");
        sb.AppendLine("        private Control? AutoBuild(object param)");
        sb.AppendLine("        {");
        sb.AppendLine("            return param switch");
        sb.AppendLine("            {");

        foreach (var mapping in mappings)
        {
            sb.AppendLine($"                {mapping.ViewModel.ToDisplayString()} => new {mapping.View.ToDisplayString()}(),");
        }

        sb.AppendLine("                _ => null");
        sb.AppendLine("            };");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("ViewLocator.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }
}
