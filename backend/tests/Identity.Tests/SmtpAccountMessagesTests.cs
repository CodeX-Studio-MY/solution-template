using Saaskit.Core.Email;
using Saaskit.Identity.Application;
using Saaskit.Identity.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Saaskit.Identity.Tests;

/// <summary>The account emails: what they say, and that a mail server problem is reported instead of thrown.</summary>
public sealed class SmtpAccountMessagesTests
{
    private static readonly Uri Url = new("https://login.localhost/account/invitation?tenant=demo&token=abc");

    private static AccountLink Link(string kind) => new(kind, "ali@example.com", "Ali", Url, new DateTime(2030, 1, 2, 3, 4, 0, DateTimeKind.Utc), "Demo Co");

    [Fact]
    public async Task An_invitation_names_the_tenant_and_carries_the_link_and_expiry()
    {
        var sender = new RecordingSender();

        (await new SmtpAccountMessages(sender, NullLogger<SmtpAccountMessages>.Instance).SendAsync(Link("invitation"), TestContext.Current.CancellationToken)).ShouldBeTrue();

        sender.Subject.ShouldBe("You're invited to Demo Co");
        sender.To.ShouldBe("ali@example.com");
        sender.Body!.ShouldContain(Url.ToString());
        sender.Body!.ShouldContain("2030-01-02 03:04 UTC");
    }

    [Fact]
    public async Task A_password_reset_has_its_own_subject()
    {
        var sender = new RecordingSender();

        await new SmtpAccountMessages(sender, NullLogger<SmtpAccountMessages>.Instance).SendAsync(Link("password-reset"), TestContext.Current.CancellationToken);

        sender.Subject.ShouldBe("Reset your password");
    }

    [Fact]
    public async Task A_send_failure_returns_false_and_the_log_never_holds_the_link()
    {
        var logger = new CapturingLogger();

        var sent = await new SmtpAccountMessages(new RecordingSender { Fails = true }, logger).SendAsync(Link("invitation"), TestContext.Current.CancellationToken);

        sent.ShouldBeFalse();
        logger.Lines.ShouldNotBeEmpty();
        logger.Lines.ShouldAllBe(line => !line.Contains("token=abc", StringComparison.Ordinal));
    }

    private sealed class RecordingSender : IEmailSender
    {
        public bool Fails { get; init; }

        public string? To { get; private set; }

        public string? Subject { get; private set; }

        public string? Body { get; private set; }

        public Task SendAsync(string toAddress, string toName, string subject, string textBody, CancellationToken cancellationToken = default)
        {
            if (Fails)
                throw new InvalidOperationException("The mail server is down.");
            (To, Subject, Body) = (toAddress, subject, textBody);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingLogger : ILogger<SmtpAccountMessages>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }
}
