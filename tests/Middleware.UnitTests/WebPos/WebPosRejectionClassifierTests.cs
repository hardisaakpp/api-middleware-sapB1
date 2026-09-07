using FluentAssertions;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.WebPos;

namespace Middleware.UnitTests.WebPos;

public sealed class WebPosRejectionClassifierTests
{
    [Theory]
    [InlineData("El producto no existe.")]
    [InlineData("  el producto no existe. ")]
    [InlineData("No entity found for query")]
    public void IsProductNotFound_RecognizesBothHitellTexts(string message) =>
        WebPosRejectionClassifier.IsProductNotFound(message).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Unable to deserialize property 'store_stock'")]
    [InlineData("La tienda no pertenece al negocio")]
    public void IsProductNotFound_IgnoresOtherMessages(string? message) =>
        WebPosRejectionClassifier.IsProductNotFound(message).Should().BeFalse();

    [Fact]
    public void ParseItems_SplitsPersistedReasonIntoPerItemEntries()
    {
        var items = WebPosRejectionClassifier.ParseItems(
            "LIMP0027 (1): No entity found for query | ACHOME001 (2): La tienda no pertenece al negocio");

        items.Should().HaveCount(2);
        items[0].Should().Be(new WebPosRejectedItem("LIMP0027", "1", "No entity found for query", WebPosRejectionTypes.ProductNotFound));
        items[1].Should().Be(new WebPosRejectedItem("ACHOME001", "2", "La tienda no pertenece al negocio", WebPosRejectionTypes.Other));
    }

    [Fact]
    public void ParseItems_ReasonWithoutItemPattern_BecomesSingleItemlessEntry()
    {
        var items = WebPosRejectionClassifier.ParseItems("WEBPOS rechazó la actualización sin detalle.");

        items.Should().ContainSingle();
        items[0].ItemCode.Should().BeNull();
        items[0].Message.Should().Be("WEBPOS rechazó la actualización sin detalle.");
        items[0].Type.Should().Be(WebPosRejectionTypes.Other);
    }

    [Fact]
    public void ClassifyReason_AllItemsMissing_IsProductNotFound() =>
        WebPosRejectionClassifier.ClassifyReason("A (1): No entity found for query | B (1): El producto no existe.")
            .Should().Be(WebPosRejectionTypes.ProductNotFound);

    [Fact]
    public void ClassifyReason_MixedOrUnknown_IsOther()
    {
        WebPosRejectionClassifier.ClassifyReason("A (1): No entity found for query | B (1): otra cosa")
            .Should().Be(WebPosRejectionTypes.Other);
        WebPosRejectionClassifier.ClassifyReason(null).Should().Be(WebPosRejectionTypes.Other);
    }
}
