namespace Acme.Gateway.Sessions;

/// <summary>
/// Inside the network the Gateway reaches the token server at its internal address. Discovery documents name the public
/// issuer (<c>https://login.{BaseDomain}/</c>), so calls to it are rewritten to the internal origin; nothing in the cluster
/// has to resolve the public sign-in hostname.
/// </summary>
internal sealed class InternalAddressHandler(Uri publicIssuer, Uri internalOrigin) : DelegatingHandler(new HttpClientHandler())
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is { } uri && uri.GetLeftPart(UriPartial.Authority).Equals(publicIssuer.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
            request.RequestUri = new Uri(internalOrigin, uri.PathAndQuery);
        return base.SendAsync(request, cancellationToken);
    }
}
