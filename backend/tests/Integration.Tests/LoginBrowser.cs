using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;

namespace Acme.Integration.Tests;

/// <summary>An HTTP client that keeps cookies and never follows redirects itself, so tests can walk the sign-in flow step by step.</summary>
internal sealed partial class LoginBrowser : IDisposable
{
    private readonly HttpClient _client;
    private readonly Dictionary<string, string> _cookies = [];

    public LoginBrowser(TestServer server, string host)
    {
        _client = new HttpClient(new CookieHandler(server.CreateHandler(), _cookies)) { BaseAddress = new Uri($"https://{host}") };
    }

    public string CookieNamed(string name) => _cookies.GetValueOrDefault(name) ?? "";

    public bool HasCookie(string name) => _cookies.ContainsKey(name);

    public Task<HttpResponseMessage> GetAsync(string url) => _client.GetAsync(url);

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => _client.SendAsync(request);

    /// <summary>Sets a cookie as if the browser already had it (for replay and cross-host tests).</summary>
    public void SetCookie(string name, string value) => _cookies[name] = value;

    public Task<HttpResponseMessage> PostFormAsync(string url, IEnumerable<KeyValuePair<string, string>> fields) =>
        _client.PostAsync(url, new FormUrlEncodedContent(fields));

    /// <summary>Reads the antiforgery token from a rendered page.</summary>
    public static async Task<string> CsrfAsync(HttpResponseMessage page)
    {
        var html = await page.Content.ReadAsStringAsync();
        return CsrfField().Match(html) is { Success: true } m ? HttpUtility.HtmlDecode(m.Groups[1].Value) : throw new InvalidOperationException("No __csrf field:\n" + html);
    }

    public static string Location(HttpResponseMessage response) =>
        response.Headers.Location?.OriginalString ?? throw new InvalidOperationException($"No Location header (status {(int)response.StatusCode}).");

    public static string Query(string url, string name) =>
        QueryHelpers.ParseQuery(new Uri(new Uri("https://x"), url).Query).TryGetValue(name, out var v) ? v.ToString() : "";

    public static (string Verifier, string Challenge) Pkce()
    {
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (verifier, WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    public void Dispose() => _client.Dispose();

    [GeneratedRegex("name=\"__csrf\" value=\"([^\"]+)\"")]
    private static partial Regex CsrfField();

    private sealed class CookieHandler(HttpMessageHandler inner, Dictionary<string, string> jar) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (jar.Count > 0)
                request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", jar.Select(c => $"{c.Key}={c.Value}")));
            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var cookie in setCookies)
                {
                    var pair = cookie.Split(';')[0];
                    var name = pair[..pair.IndexOf('=', StringComparison.Ordinal)];
                    var value = pair[(pair.IndexOf('=', StringComparison.Ordinal) + 1)..];
                    if (value.Length == 0 || cookie.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase))
                        jar.Remove(name);
                    else
                        jar[name] = value;
                }
            }
            return response;
        }
    }
}
