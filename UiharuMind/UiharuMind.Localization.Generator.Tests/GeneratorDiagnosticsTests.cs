using System.Collections.Immutable;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using UiharuMind.Localization.Generator;

namespace UiharuMind.Localization.Generator.Tests;

public class GeneratorDiagnosticsTests
{
    private const string DefaultResx = """
        <root>
          <data name="UsedKey"><value>x</value></data>
          <data name="DeadKey"><value>y</value></data>
        </root>
        """;

    [Fact]
    public void Generate_UnknownKeyInAxaml_ReportsLk2001()
    {
        var diagnostics = Run(
            "View.axaml",
            """<Window xmlns="https://github.com/avaloniaui"><TextBlock Text="{loc:Loc NoSuchKey}" /></Window>""");

        Assert.Contains(diagnostics, d => d.Id == "LK2001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generate_UnknownKeyWithOtherPrefix_ReportsLk2001()
    {
        // markup:Loc 是仓库另一处用到的同款标记扩展前缀，同样要拦截
        var diagnostics = Run(
            "View.axaml",
            """<Window xmlns="https://github.com/avaloniaui"><TextBlock Text="{markup:Loc NoSuchKey}" /></Window>""");

        Assert.Contains(diagnostics, d => d.Id == "LK2001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void Generate_UnusedKey_ReportsLk2002()
    {
        var diagnostics = Run(
            "View.axaml",
            """<Window xmlns="https://github.com/avaloniaui"><TextBlock Text="{loc:Loc UsedKey}" /></Window>""");

        Assert.Contains(diagnostics, d => d.Id == "LK2002" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void Generate_AllKeysUsed_NoLk2001OrLk2002()
    {
        var diagnostics = Run(
            "View.axaml",
            """<Window xmlns="https://github.com/avaloniaui"><TextBlock Text="{loc:Loc UsedKey}" /><TextBlock Text="{loc:Loc DeadKey}" /></Window>""");

        Assert.DoesNotContain(diagnostics, d => d.Id is "LK2001" or "LK2002");
    }

    [Fact]
    public void Generate_SatelliteMissingKey_ReportsLk1003()
    {
        var generator = new LangKeySourceGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(
            new[] { generator },
            additionalTexts: new[]
            {
                new InMemoryAdditionalText("Lang.resx", DefaultResx),
                new InMemoryAdditionalText(
                    "Lang.zh-hans.resx",
                    """<root><data name="UsedKey"><value>用</value></data></root>"""),
                new InMemoryAdditionalText(
                    "View.axaml",
                    """<Window xmlns="https://github.com/avaloniaui"><TextBlock Text="{loc:Loc UsedKey}" /></Window>"""),
            });

        var compilation = CSharpCompilation.Create("Test", new[] { CSharpSyntaxTree.ParseText("") });
        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);

        Assert.Contains(diagnostics, d => d.Id == "LK1003");
    }

    [Fact]
    public void Generate_EmitsLangKeyEnum()
    {
        var generator = new LangKeySourceGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(
            new[] { generator },
            additionalTexts: new[]
            {
                new InMemoryAdditionalText("Lang.resx", DefaultResx),
                new InMemoryAdditionalText(
                    "View.axaml",
                    """<Window xmlns="https://github.com/avaloniaui"><TextBlock Text="{loc:Loc UsedKey}" /></Window>"""),
            });

        var compilation = CSharpCompilation.Create("Test", new[] { CSharpSyntaxTree.ParseText("") });
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var generated = output.SyntaxTrees
            .Select(static tree => tree.ToString())
            .FirstOrDefault(static text => text.Contains("public enum LangKey"));

        Assert.NotNull(generated);
        Assert.Contains("    UsedKey,", generated);
        Assert.Contains("    DeadKey,", generated);
    }

    private static ImmutableArray<Diagnostic> Run(string axamlPath, string axamlContent)
    {
        var generator = new LangKeySourceGenerator().AsSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(
            new[] { generator },
            additionalTexts: new[]
            {
                new InMemoryAdditionalText("Lang.resx", DefaultResx),
                new InMemoryAdditionalText(axamlPath, axamlContent),
            });

        var compilation = CSharpCompilation.Create("Test", new[] { CSharpSyntaxTree.ParseText("") });
        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        return diagnostics;
    }

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly string _path;
        private readonly string _text;

        internal InMemoryAdditionalText(string path, string text)
        {
            _path = path;
            _text = text;
        }

        public override string Path => _path;

        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From(_text, Encoding.UTF8);
    }
}