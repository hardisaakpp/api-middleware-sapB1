using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Application.WebPos;
using Middleware.Domain.Enums;

namespace Middleware.UnitTests.WebPos;

public sealed class WebPosStockTransferNotifierTests
{
    private readonly Mock<IJobExecutionCoordinator> _jobCoordinatorMock = new();
    private readonly Mock<IPosSyncStatusRepository> _posSyncStatusMock = new();
    private readonly WebPosStockTransferNotifier _sut;

    public WebPosStockTransferNotifierTests()
    {
        _sut = new WebPosStockTransferNotifier(_jobCoordinatorMock.Object, _posSyncStatusMock.Object, NullLogger<WebPosStockTransferNotifier>.Instance);
    }

    private static IReadOnlyList<NotifyWebPosStockTransferLine> Lines() => [new NotifyWebPosStockTransferLine("ITEM001", 5m)];

    [Fact]
    public async Task NotifyAsync_JobCompletedSynchronously_ReturnsInnerResult()
    {
        var expected = new WebPosSyncInfo(WebPosSyncStatus.Synced);
        _jobCoordinatorMock
            .Setup(c => c.ExecuteAsync(
                "NotifyWebPosStockTransfer", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Func<IServiceProvider, CancellationToken, Task<WebPosSyncInfo>>>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobCompleted<WebPosSyncInfo>(expected));

        var result = await _sut.NotifyAsync(42, 1001, "WH01", "WH02", Lines(), CancellationToken.None);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task NotifyAsync_JobQueued_ReturnsQueuedStatusWithJobId()
    {
        var jobId = Guid.NewGuid();
        _jobCoordinatorMock
            .Setup(c => c.ExecuteAsync(
                "NotifyWebPosStockTransfer", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Func<IServiceProvider, CancellationToken, Task<WebPosSyncInfo>>>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobQueued<WebPosSyncInfo>(jobId));

        var result = await _sut.NotifyAsync(42, 1001, "WH01", "WH02", Lines(), CancellationToken.None);

        result.Status.Should().Be(WebPosSyncStatus.Queued);
        result.JobId.Should().Be(jobId);
    }

    [Fact]
    public async Task NotifyAsync_CoordinatorThrows_NeverPropagates_ReturnsErrorInstead()
    {
        _jobCoordinatorMock
            .Setup(c => c.ExecuteAsync(
                "NotifyWebPosStockTransfer", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Func<IServiceProvider, CancellationToken, Task<WebPosSyncInfo>>>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("WEBPOS unreachable"));

        // This is the guarantee the whole design relies on: a StockTransfer that already
        // succeeded in SAP must never fail its HTTP response because of a WEBPOS problem.
        var result = await _sut.NotifyAsync(42, 1001, "WH01", "WH02", Lines(), CancellationToken.None);

        result.Status.Should().Be(WebPosSyncStatus.Error);
        result.Reason.Should().Contain("WEBPOS unreachable");
    }
}
