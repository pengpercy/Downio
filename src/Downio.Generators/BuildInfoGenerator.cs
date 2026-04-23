using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Downio.Generators;

[Generator]
public sealed class BuildInfoGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var version = context.AnalyzerConfigOptionsProvider
            .Select((provider, _) =>
            {
                provider.GlobalOptions.TryGetValue("build_property.Version", out var v);
                provider.GlobalOptions.TryGetValue("build_property.InformationalVersion", out var iv);
                return (Version: v ?? string.Empty, InformationalVersion: iv ?? string.Empty);
            });

        context.RegisterSourceOutput(version, (spc, v) =>
        {
            var versionLiteral = EscapeForCSharpString(v.Version);
            var informationalLiteral = EscapeForCSharpString(v.InformationalVersion);

            var sb = new StringBuilder();
            sb.AppendLine("#nullable enable");
            sb.AppendLine("namespace Downio;");
            sb.AppendLine();
            sb.AppendLine("public static class BuildInfo");
            sb.AppendLine("{");
            sb.AppendLine($"    public const string Version = \"{versionLiteral}\";");
            sb.AppendLine($"    public const string InformationalVersion = \"{informationalLiteral}\";");
            sb.AppendLine("}");

            spc.AddSource("BuildInfo.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        });
    }

    private static string EscapeForCSharpString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}

