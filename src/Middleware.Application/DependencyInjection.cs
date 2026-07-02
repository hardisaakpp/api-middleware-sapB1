using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.UseCases.BusinessPartners;
using Middleware.Application.UseCases.Deposits;
using Middleware.Application.UseCases.GoodsReceipt;
using Middleware.Application.UseCases.InventoryTransferRequests;
using Middleware.Application.UseCases.Items;
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
        services.AddScoped<CreatePurchaseOrderUseCase>();
        services.AddScoped<GetPurchaseOrderUseCase>();
        services.AddScoped<GetPurchaseOrdersUseCase>();
        services.AddScoped<UpdatePurchaseOrderUseCase>();
        services.AddScoped<GetItemUseCase>();
        services.AddScoped<GetItemStockUseCase>();
        services.AddScoped<GetBusinessPartnerUseCase>();
        services.AddScoped<GetInventoryTransferRequestsByCommentUseCase>();

        services.AddValidatorsFromAssemblyContaining<CreateStockTransferRequestValidator>(ServiceLifetime.Scoped);

        return services;
    }
}
