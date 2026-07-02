using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.UseCases.Deposits;
using Middleware.Domain.Enums;

namespace Middleware.UnitTests.UseCases.Deposits;

public sealed class CreateDepositUseCaseTests
{
    private readonly Mock<ISapGateway> _gatewayMock = new();
    private readonly CreateDepositUseCase _sut;

    private static readonly DepositSettings Settings = new()
    {
        ProductionCompanyDb = "PROD_DB",
        TestCompanyDb = "TEST_DB"
    };

    public CreateDepositUseCaseTests()
    {
        _sut = new CreateDepositUseCase(
            _gatewayMock.Object,
            Options.Create(Settings),
            NullLogger<CreateDepositUseCase>.Instance);
    }

    [Theory]
    [InlineData(CompanyEnvironment.Production, "PROD_DB")]
    [InlineData(CompanyEnvironment.Test, "TEST_DB")]
    public async Task ExecuteAsync_SelectsCorrectCompanyDb_BasedOnEnvironment(
        CompanyEnvironment env, string expectedDb)
    {
        var request = new CreateDepositRequest(
            Type: DepositType.MT,
            Environment: env,
            DocDate: new DateOnly(2024, 6, 1),
            Comments: null,
            Lines: [new DepositLineDto("ITEM001", 5m, "WH01")]
        );

        _gatewayMock
            .Setup(g => g.CreateDepositAsync(request, expectedDb, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentCreatedResult(10, 200));

        var result = await _sut.ExecuteAsync(request);

        result.DocEntry.Should().Be(10);
        _gatewayMock.Verify(g => g.CreateDepositAsync(request, expectedDb, It.IsAny<CancellationToken>()), Times.Once);
    }
}
