using System.Net;
using System.Net.Http.Json;
using Saaskit.Contracts.Sample;

namespace Saaskit.Sample.Tests;

/// <summary>The typed client other modules use, against a stand-in for the module's API.</summary>
[Trait("service", "sample-key")]
public sealed class SampleClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_client_reads_an_item_from_the_modules_api()
    {
        var id = Guid.NewGuid();
        var handler = new Stub(request => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new ItemView(id, "Widget", "\"v1\"")) });
        var client = new SampleClient(new HttpClient(handler) { BaseAddress = new Uri("http://sample-key") });

        var item = await client.GetItemAsync(id, Ct);

        item.ShouldBe(new ItemView(id, "Widget", "\"v1\""));
        handler.Requests.Single().RequestUri!.AbsolutePath.ShouldBe($"/api/sample-key/v1/items/{id}");
    }

    [Fact]
    public async Task The_client_returns_null_for_an_item_that_does_not_exist()
    {
        var client = new SampleClient(new HttpClient(new Stub(_ => new HttpResponseMessage(HttpStatusCode.NotFound))) { BaseAddress = new Uri("http://sample-key") });

        (await client.GetItemAsync(Guid.NewGuid(), Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task The_client_fails_on_any_other_error()
    {
        var client = new SampleClient(new HttpClient(new Stub(_ => new HttpResponseMessage(HttpStatusCode.BadGateway))) { BaseAddress = new Uri("http://sample-key") });

        await Should.ThrowAsync<HttpRequestException>(() => client.GetItemAsync(Guid.NewGuid(), Ct));
    }

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }
}
