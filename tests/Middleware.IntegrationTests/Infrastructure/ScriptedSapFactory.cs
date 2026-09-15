using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Dtos.Responses;
using Middleware.Domain.Enums;
using Middleware.Infrastructure.Sap;
using Moq;

namespace Middleware.IntegrationTests.Infrastructure;

/// <summary>
/// WebApplicationFactory that keeps the real SapGateway but talks to a <see cref="ScriptedSapServiceLayerClient"/>,
/// with the test-company credentials configured (needed by /stock-transfers/based-on-document, /direct,
/// /with-bins), no SQL (in-memory job queue) and a WEBPOS notifier that always reports Synced.
/// </summary>
internal sealed class ScriptedSapFactory : WebApplicationFactory<Program>
{
    public ScriptedSapServiceLayerClient SapClient { get; } = new();

    public InMemoryJobQueueRepository JobRepository { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sap:TestCompanyDb"] = "TEST_COMPANY",
                ["Sap:TestPassword"] = "test-password",
                // The scripted client answers instantly — keep every request on the inline (201) path.
                ["JobQueue:QueueAfterSeconds"] = "30"
            });
        });

        builder.ConfigureServices(services =>
        {
            Replace<ISapServiceLayerClient>(services, _ => SapClient);
            Replace<ISapSessionManager>(services, _ => new FixedSessionManager());
            Replace<IJobQueueRepository>(services, _ => JobRepository);

            var notifier = new Mock<IWebPosStockTransferNotifier>();
            notifier
                .Setup(n => n.NotifyAsync(
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<NotifyWebPosStockTransferLine>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WebPosSyncInfo(WebPosSyncStatus.Synced));
            Replace<IWebPosStockTransferNotifier>(services, _ => notifier.Object);

            var goodsReceiptNotifier = new Mock<IWebPosGoodsReceiptNotifier>();
            goodsReceiptNotifier
                .Setup(n => n.NotifyAsync(
                    It.IsAny<int>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<NotifyWebPosGoodsReceiptLine>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WebPosSyncInfo(WebPosSyncStatus.Synced));
            Replace<IWebPosGoodsReceiptNotifier>(services, _ => goodsReceiptNotifier.Object);

            // The warmup service pre-loads ItemGroups through the real gateway → extra scripted-client
            // calls that would pollute the exact call sequence these tests assert on.
            var warmup = services.SingleOrDefault(d =>
                d.ServiceType == typeof(IHostedService) && d.ImplementationType?.Name == "SapSessionWarmupService");
            if (warmup is not null)
            {
                services.Remove(warmup);
            }
        });
    }

    private static void Replace<TService>(IServiceCollection services, Func<IServiceProvider, TService> factory)
        where TService : class
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(TService));
        if (descriptor is not null)
        {
            services.Remove(descriptor);
        }

        services.AddSingleton<TService>(factory);
    }

    private sealed class FixedSessionManager : ISapSessionManager
    {
        public Task<string> GetSessionTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult("fake-session-token");

        public Task<string> GetSessionTokenAsync(string companyDb, string password, CancellationToken cancellationToken = default)
            => Task.FromResult("fake-session-token");

        public Task InvalidateSessionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task InvalidateSessionAsync(string companyDb, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
