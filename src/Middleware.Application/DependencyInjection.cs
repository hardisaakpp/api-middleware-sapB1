using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Middleware.Application.Abstractions;
using Middleware.Application.Dtos.Requests;
using Middleware.Application.Jobs.Handlers;
using Middleware.Application.UseCases.BusinessPartners;
using Middleware.Application.UseCases.Deposits;
using Middleware.Application.UseCases.GoodsReceipt;
using Middleware.Application.UseCases.InventoryTransferRequests;
using Middleware.Application.UseCases.Items;
using Middleware.Application.UseCases.Liquidaciones;
using Middleware.Application.UseCases.PurchaseOrders;
using Middleware.Application.UseCases.StockTransfer;
using Middleware.Application.UseCases.Validations;
using Middleware.Application.UseCases.Warehouses;
using Middleware.Application.UseCases.WebPos;
using Middleware.Application.WebPos;

namespace Middleware.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<CreateStockTransferBasedOnDocumentUseCase>();
        services.AddScoped<CreateStockTransferDirectUseCase>();
        services.AddScoped<CreateStockTransferWithBinsUseCase>();
        services.AddScoped<CreateStockTransferRequestUseCase>();
        services.AddScoped<NotifyWebPosStockTransferUseCase>();
        services.AddScoped<RetryWebPosStockTransferUseCase>();
        services.AddScoped<IWebPosStockTransferNotifier, WebPosStockTransferNotifier>();
        services.AddScoped<CreateDepositUseCase>();
        services.AddScoped<CreateGoodsReceiptUseCase>();
        services.AddScoped<CreateLiquidacionUseCase>();
        services.AddScoped<CreatePurchaseOrderUseCase>();
        services.AddScoped<GetPurchaseOrderUseCase>();
        services.AddScoped<GetPurchaseOrdersUseCase>();
        services.AddScoped<UpdatePurchaseOrderUseCase>();
        services.AddScoped<GetItemUseCase>();
        services.AddScoped<GetItemStockUseCase>();
        services.AddScoped<GetItemsBatchUseCase>();
        services.AddScoped<GetBusinessPartnerUseCase>();
        services.AddScoped<GetInventoryTransferRequestsByCommentUseCase>();
        services.AddScoped<UpdateInventoryTransferRequestStatusUseCase>();
        services.AddScoped<ValidatePurchaseOrderUseCase>();
        services.AddScoped<GetWarehousesUseCase>();
        services.AddScoped<ValidateWarehousesUseCase>();

        services.AddScoped<IJobHandler, CreatePurchaseOrderJobHandler>();
        services.AddScoped<IJobHandler, UpdatePurchaseOrderJobHandler>();
        services.AddScoped<IJobHandler, GetPurchaseOrderJobHandler>();
        services.AddScoped<IJobHandler, GetPurchaseOrdersJobHandler>();
        services.AddScoped<IJobHandler, CreateStockTransferBasedOnDocumentJobHandler>();
        services.AddScoped<IJobHandler, CreateStockTransferDirectJobHandler>();
        services.AddScoped<IJobHandler, CreateStockTransferWithBinsJobHandler>();
        services.AddScoped<IJobHandler, CreateStockTransferRequestJobHandler>();
        services.AddScoped<IJobHandler, CreateDepositJobHandler>();
        services.AddScoped<IJobHandler, CreateGoodsReceiptJobHandler>();
        services.AddScoped<IJobHandler, CreateLiquidacionJobHandler>();
        services.AddScoped<IJobHandler, GetBusinessPartnerJobHandler>();
        services.AddScoped<IJobHandler, GetItemJobHandler>();
        services.AddScoped<IJobHandler, GetItemStockJobHandler>();
        services.AddScoped<IJobHandler, GetInventoryTransferRequestsByCommentJobHandler>();
        services.AddScoped<IJobHandler, UpdateInventoryTransferRequestStatusJobHandler>();
        services.AddScoped<IJobHandler, NotifyWebPosStockTransferJobHandler>();

        services.AddValidatorsFromAssemblyContaining<CreateStockTransferRequestValidator>(ServiceLifetime.Scoped);

        return services;
    }
}
