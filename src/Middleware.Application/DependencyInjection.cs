using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.UseCases.Deposits;
using Middleware.Application.UseCases.GoodsReceipt;
using Middleware.Application.UseCases.Liquidaciones;
using Middleware.Application.UseCases.PurchaseOrders;
using Middleware.Application.UseCases.StockTransfer;

namespace Middleware.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<CreateStockTransferUseCase>();
        services.AddScoped<CreateStockTransferWithBinsUseCase>();
        services.AddScoped<CreateStockTransferRequestUseCase>();
        services.AddScoped<CreateDepositUseCase>();
        services.AddScoped<CreateGoodsReceiptUseCase>();
        services.AddScoped<CreateLiquidacionUseCase>();
        services.AddScoped<GetPurchaseOrderUseCase>();
        services.AddScoped<UpdatePurchaseOrderUseCase>();

        services.AddValidatorsFromAssemblyContaining<CreateStockTransferRequestValidator>(ServiceLifetime.Scoped);

        return services;
    }
}
