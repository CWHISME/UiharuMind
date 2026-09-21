using UiharuMind.Localization.Generator.Parsing;

namespace UiharuMind.Localization.Generator.Tests;

public class ResxResourceParserTests
{
    private const string ValidResx = """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <data name="Send"><value>Send</value></data>
          <data name="SendUserDesc"><value>Send a message</value></data>
        </root>
        """;

    [Fact]
    public void Parse_DefaultCulture_WhenFileNameHasNoCultureSuffix()
    {
        var result = ResxResourceParser.Parse("/repo/Lang.resx", ValidResx);

        Assert.Null(result.Error);
        Assert.NotNull(result.Resource);
        Assert.True(result.Resource!.IsDefaultCulture);
        Assert.Null(result.Resource.CultureName);
        Assert.Equal(2, result.Resource.Keys.Length);
        Assert.Equal("Send", result.Resource.Keys[0].Key);
    }

    [Fact]
    public void Parse_SatelliteCulture_WhenFileNameHasCultureSuffix()
    {
        var result = ResxResourceParser.Parse("/repo/Lang.zh-hans.resx", ValidResx);

        Assert.False(result.Resource!.IsDefaultCulture);
        Assert.Equal("zh-hans", result.Resource.CultureName);
    }

    [Fact]
    public void Parse_DuplicateKey_ReturnsError()
    {
        const string duplicate = """
            <root>
              <data name="Send"><value>a</value></data>
              <data name="Send"><value>b</value></data>
            </root>
            """;

        var result = ResxResourceParser.Parse("/repo/Lang.resx", duplicate);

        Assert.NotNull(result.Error);
        Assert.Contains("重复", result.Error);
    }

    [Fact]
    public void Parse_MalformedXml_ReturnsError()
    {
        var result = ResxResourceParser.Parse("/repo/Lang.resx", "<root><data");

        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Parse_NoDataEntries_ReturnsError()
    {
        var result = ResxResourceParser.Parse("/repo/Lang.resx", "<root></root>");

        Assert.NotNull(result.Error);
        Assert.Contains("没有", result.Error);
    }
}