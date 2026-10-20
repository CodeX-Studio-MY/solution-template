using Farm360.Core.Configuration;
using Farm360.Core.Hosting.Auth;
using Farm360.Core.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Farm360.Integration.Tests;

[Trait("service", "identity")]
public sealed class AccessVersionCheckerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IAccessVersionStore _store = Substitute.For<IAccessVersionStore>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly AccessVersionChecker _checker;

    public AccessVersionCheckerTests() =>
        _checker = new AccessVersionChecker(_store, _time,
            Options.Create(new AuthOptions { AccessVersionCacheSeconds = 3, AccessVersionGraceSeconds = 60 }),
            NullLogger<AccessVersionChecker>.Instance);

    [Fact]
    public async Task Older_tokens_are_stale_and_unknown_principals_are_current()
    {
        _store.GetAsync(_tenant, "p", Arg.Any<CancellationToken>()).Returns(5L);
        (await _checker.IsCurrentAsync(_tenant, "p", "4", Ct)).ShouldBeFalse();
        (await _checker.IsCurrentAsync(_tenant, "p", "5", Ct)).ShouldBeTrue();

        _store.GetAsync(_tenant, "new", Arg.Any<CancellationToken>()).Returns((long?)null);
        (await _checker.IsCurrentAsync(_tenant, "new", "0", Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task When_the_store_fails_the_last_known_value_is_used_then_it_fails_closed()
    {
        _store.GetAsync(_tenant, "p", Arg.Any<CancellationToken>()).Returns(5L);
        (await _checker.IsCurrentAsync(_tenant, "p", "5", Ct)).ShouldBeTrue();

        _store.GetAsync(_tenant, "p", Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("redis down"));
        _time.Advance(TimeSpan.FromSeconds(30));
        (await _checker.IsCurrentAsync(_tenant, "p", "4", Ct)).ShouldBeFalse();   // last known value still applies

        _time.Advance(TimeSpan.FromSeconds(31));
        await Should.ThrowAsync<AccessCheckUnavailableException>(() => _checker.IsCurrentAsync(_tenant, "p", "5", Ct));
    }

    [Fact]
    public async Task Fails_closed_with_503_not_401()
    {
        _store.GetAsync(_tenant, "p", Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("redis down"));
        var ex = await Should.ThrowAsync<AccessCheckUnavailableException>(() => _checker.IsCurrentAsync(_tenant, "p", "1", Ct));
        ex.Error.Status.ShouldBe(503);
    }
}
