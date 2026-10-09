using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Acme.Identity.Infrastructure;

/// <summary>
/// Replaces OpenIddict's <c>ValidateAuthorizedParty</c>. The built-in rule says a token can only be presented by the client
/// it was issued to. That stays true for authorization codes and refresh tokens. In a token exchange (RFC 8693) the subject
/// token is the user's and the actor token is a machine's, presented by one of our own services; the endpoint
/// (<c>TokenExchange</c>) checks that the caller is a platform service, so the presenter rule does not apply to them.
/// </summary>
internal sealed class ExchangeAuthorizedParty : IOpenIddictServerHandler<OpenIddictServerEvents.ValidateTokenRequestContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<OpenIddictServerEvents.ValidateTokenRequestContext>()
            .UseSingletonHandler<ExchangeAuthorizedParty>()
            .SetOrder(OpenIddictServerHandlers.Exchange.ValidateAuthorizedParty.Descriptor.Order)
            .SetType(OpenIddictServerHandlerType.BuiltIn)
            .Build();

    public ValueTask HandleAsync(OpenIddictServerEvents.ValidateTokenRequestContext context)
    {
        if (context.Request.IsTokenExchangeGrantType())
            return ValueTask.CompletedTask;

        foreach (var principal in new[] { context.AuthorizationCodePrincipal, context.RefreshTokenPrincipal, context.DeviceCodePrincipal })
        {
            // Tokens with presenters can only be redeemed by one of them.
            if (principal is not null && principal.HasClaim(Claims.Private.Presenter) && !principal.HasPresenter(context.ClientId ?? ""))
            {
                context.Reject(Errors.InvalidGrant, "The token cannot be used by this client application.");
                return ValueTask.CompletedTask;
            }
        }
        return ValueTask.CompletedTask;
    }
}
