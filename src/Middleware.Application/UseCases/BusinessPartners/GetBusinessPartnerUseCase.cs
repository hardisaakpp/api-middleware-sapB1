using Microsoft.Extensions.Logging;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Responses;

namespace Middleware.Application.UseCases.BusinessPartners;

public sealed class GetBusinessPartnerUseCase
{
    private readonly ISapGateway _gateway;
    private readonly ILogger<GetBusinessPartnerUseCase> _logger;

    public GetBusinessPartnerUseCase(ISapGateway gateway, ILogger<GetBusinessPartnerUseCase> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<BusinessPartnerResponse?> ExecuteAsync(string cardCode, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting BusinessPartner CardCode={CardCode}", cardCode);
        return await _gateway.GetBusinessPartnerAsync(cardCode, cancellationToken);
    }
}
