using FluentAssertions;
using Middleware.Infrastructure.Configuration;
using Middleware.Infrastructure.WebPos;

namespace Middleware.IntegrationTests.WebPos;

/// <summary>
/// Production incident 2026-09-09: WebPos:BaseUrl was configured as
/// "https://e-proxy.hitellcorp.com/external/", so every call (login included) hit
/// /external/apilogin/external and WEBPOS answered "El token de acceso es inválido o esta expirado."
/// Nothing reached HITELL and nothing was recorded. These tests pin the two defences.
/// </summary>
public sealed class WebPosBaseUrlAndTokenTests
{
    [Theory]
    [InlineData("https://e-proxy.hitellcorp.com/external/", "https://e-proxy.hitellcorp.com/", true)]
    [InlineData("https://e-proxy.hitellcorp.com/external", "https://e-proxy.hitellcorp.com/", true)]
    [InlineData("https://e-proxy.hitellcorp.com/EXTERNAL/", "https://e-proxy.hitellcorp.com/", true)]
    [InlineData("https://e-proxy.hitellcorp.com/", "https://e-proxy.hitellcorp.com/", false)]
    [InlineData("https://e-proxy.hitellcorp.com", "https://e-proxy.hitellcorp.com/", false)]
    [InlineData("http://210.17.1.58:8888/", "http://210.17.1.58:8888/", false)]
    [InlineData("  http://210.17.1.58:8888  ", "http://210.17.1.58:8888/", false)]
    public void NormalizeBaseUrl_StripsExternalSuffixAndEnsuresTrailingSlash(string configured, string expected, bool stripped)
    {
        var (url, strippedExternal) = WebPosSettings.NormalizeBaseUrl(configured);

        url.Should().Be(expected);
        strippedExternal.Should().Be(stripped);
        new WebPosSettings { BaseUrl = configured }.ResolvedBaseUrl.Should().Be(expected);
    }

    [Fact]
    public void IsTokenRejected_DetectsWebPosInvalidTokenAnswer()
    {
        const string body = """{"procesoCorrecto":false,"error":1,"mensaje":"El token de acceso es inválido o esta expirado.","retorno":null}""";

        WebPosResponses.IsTokenRejected(body).Should().BeTrue();
    }

    [Theory]
    [InlineData("""{"procesoCorrecto":true,"error":0,"mensaje":null,"retorno":{}}""")]
    [InlineData("""{"procesoCorrecto":false,"error":1,"mensaje":"El producto no existe.","retorno":null}""")]
    [InlineData("""{"procesoCorrecto":false,"error":1,"mensaje":"La tienda no pertenece al negocio","retorno":null}""")]
    [InlineData("no es json")]
    [InlineData("")]
    public void IsTokenRejected_IgnoresBusinessRejectionsAndGarbage(string body)
    {
        WebPosResponses.IsTokenRejected(body).Should().BeFalse();
    }
}
