using Acme.Contracts;
using Acme.Core.Composition;
using Acme.Core.Data;
using Acme.Core.Localization;
using Acme.Core.Messaging;
using Acme.Core.Platform;
using Acme.PurchaseRequest.Api;
using Acme.PurchaseRequest.Application;
using Acme.PurchaseRequest.Domain;
using Acme.PurchaseRequest.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Acme.PurchaseRequest;

/// <summary>Reference scenario: the standard module every tenant has. Copy its patterns, not its code (use `scripts/new-module`).</summary>
public sealed class PurchaseRequestModule : IPlatformModule
{
    public static PlatformService Service => PlatformServices.PurchaseRequest;

    public static void Register(IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddModuleDbContext<PurchaseRequestDbContext>(Service.Schema);
        services.AddModuleOutbox<PurchaseRequestDbContext>(Service.Schema);
        services.AddModuleMessages(typeof(PurchaseRequestModule).Assembly);

        services.AddScoped<IRequestRepository, RequestRepository>();
        services.AddScoped<IQuoteFiles, QuoteFiles>();
        services.AddScoped<IRequestSettings, DefaultRequestSettings>();
        services.AddScoped<IApprovalPolicyResolver, ApprovalPolicyResolver>();
        services.AddScoped<RequestService>();
        services.AddKeyedScoped<IApprovalPolicy, SingleApproverPolicy>(SingleApproverPolicy.Key);

        services.AddEventConsumer<ApprovalReminderDue.V1, ApprovalReminderConsumer, PurchaseRequestDbContext>(Service, Modules.PurchaseRequest);
    }

    public static void MapEndpoints(IEndpointRouteBuilder api) => PurchaseRequestEndpoints.Map(api);
}
