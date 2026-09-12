using SapB1.ItemImport.ServiceLayer;

namespace SapB1.ItemImport.Tests;

public sealed class ServiceLayerErrorParserTests
{
    [Fact]
    public void ParsesTheNestedMessageShape()
    {
        var (code, message) = ServiceLayerErrorParser.Parse(
            """{"error":{"code":-2035,"message":{"lang":"en-us","value":"Item code already exists"}}}""");

        Assert.Equal("-2035", code);
        Assert.Equal("Item code already exists", message);
    }

    [Fact]
    public void ParsesTheFlatMessageShape()
    {
        var (code, message) = ServiceLayerErrorParser.Parse("""{"error":{"code":"-1116","message":"Invalid item group"}}""");

        Assert.Equal("-1116", code);
        Assert.Equal("Invalid item group", message);
    }

    [Fact]
    public void FallsBackToTheRawBodyWhenItIsNotJson()
    {
        var (code, message) = ServiceLayerErrorParser.Parse("<html><body>502 Bad Gateway</body></html>");

        Assert.Null(code);
        Assert.Contains("502 Bad Gateway", message, StringComparison.Ordinal);
    }

    [Fact]
    public void HandlesAnEmptyBody()
    {
        var (code, message) = ServiceLayerErrorParser.Parse(string.Empty);

        Assert.Null(code);
        Assert.NotEmpty(message);
    }
}

public sealed class ServiceLayerOptionsTests
{
    private static ServiceLayerOptions Valid() => new()
    {
        BaseUrl = "https://sap:50000/b1s/v1",
        CompanyDb = "SBODEMOGB",
        UserName = "manager",
        Password = "secret",
    };

    [Fact]
    public void NormalisesTheBaseUrlWithATrailingSlash()
    {
        var uri = Valid().Validate();

        Assert.Equal("https://sap:50000/b1s/v1/", uri.ToString());
    }

    [Fact]
    public void AppendsTheServiceLayerPathWhenItIsMissing()
    {
        var options = Valid();
        options.BaseUrl = "https://sap:50000";

        Assert.Equal("https://sap:50000/b1s/v1/", options.Validate().ToString());
    }

    [Fact]
    public void ReportsEveryMissingSettingAtOnce()
    {
        var exception = Assert.Throws<ArgumentException>(() => new ServiceLayerOptions().Validate());

        Assert.Contains("BaseUrl is required", exception.Message, StringComparison.Ordinal);
        Assert.Contains("CompanyDb is required", exception.Message, StringComparison.Ordinal);
        Assert.Contains("UserName is required", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Password is required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsPinningAndBlanketTrustTogether()
    {
        var options = Valid();
        options.ServerCertificateThumbprint = "AA:BB";
        options.TrustAnyServerCertificate = true;

        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("not both", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalisesTheCertificateThumbprint()
    {
        var options = Valid();
        options.ServerCertificateThumbprint = "aa:bb cc-dd";

        Assert.Equal("AABBCCDD", options.NormalizedThumbprint());
    }

    [Theory]
    [InlineData("A-1", "A-1")]
    [InlineData("O'Brien", "O''Brien")]
    [InlineData("a''b", "a''''b")]
    public void DoublesSingleQuotesInODataLiterals(string input, string expected)
        => Assert.Equal(expected, ServiceLayerItemGateway.EscapeODataLiteral(input));
}
