namespace Saaskit.Gateway.Sessions;

/// <summary>
/// Inside the network the Gateway reaches the token server at its internal address. Discovery documents name the public
/// issuer (<c>https://login.{BaseDomain}/</c>), so calls to it are rewritten to the internal origin; nothing in the cluster
/// has to resolve the public sign-in hostname. The request keeps the public host (and scheme) as headers so the answer is the same.
/// </summary>
internal sealed class InternalAddressHandler(Uri publicIssuer, Uri internalOrigin) : DelegatingHandler(new HttpClientHandler())
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is { } uri)
        {
            var authority = uri.GetLeftPart(UriPartial.Authority);
            var toPublic = authority.Equals(publicIssuer.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
            var toInternal = authority.Equals(internalOrigin.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
            if (toPublic)
                request.RequestUri = new Uri(internalOrigin, uri.PathAndQuery);
            if (toPublic || toInternal)
            {
                // The token server must still see the public host, or the endpoints in its discovery document (which the browser is
                // redirected to) would name the internal address.
                request.Headers.Host = publicIssuer.Authority;
                request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", publicIssuer.Scheme);
                request.Headers.TryAddWithoutValidation("X-Forwarded-Host", publicIssuer.Authority);
            }
        }
        return base.SendAsync(request, cancellationToken);
    }
}
