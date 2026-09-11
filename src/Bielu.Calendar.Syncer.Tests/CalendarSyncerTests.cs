using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Bielu.Calendar.Syncer.Tests;

public sealed class CalendarSyncerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly CalendarAccount _left = NewAccount("left@example.com");
    private readonly CalendarAccount _right = NewAccount("right@example.com");
    private readonly FakeCalendarProvider _provider = new();
    private readonly InMemorySyncStateStore _links = new();

    [Fact]
    public async Task MirrorsAnEventToTheOtherCalendar()
    {
        _provider.Calendar(_left.Id).Add(PlainEvent("src-1", "Standup"));

        var result = await CreateSyncer().SyncAsync(CancellationToken.None);

        result.Created.ShouldBe(1);
        var mirror = _provider.Calendar(_right.Id).ShouldHaveSingleItem();
        mirror.Subject.ShouldBe("Standup");
        mirror.Body.ShouldContain($"[bielu-sync:{_left.Id:N}:src-1]");
    }

    [Fact]
    public async Task DoesNotMirrorAMirrorBackToItsOrigin()
    {
        _provider.Calendar(_left.Id).Add(PlainEvent("src-1", "Standup"));
        var syncer = CreateSyncer();

        await syncer.SyncAsync(CancellationToken.None);
        var second = await syncer.SyncAsync(CancellationToken.None);

        // The copy now sitting in the right calendar carries a marker, so it must never be treated as a source.
        second.Created.ShouldBe(0);
        _provider.Calendar(_left.Id).Count.ShouldBe(1);
        _provider.Calendar(_right.Id).Count.ShouldBe(1);
    }

    [Fact]
    public async Task RepeatedRunsDoNotChurnUnchangedEvents()
    {
        _provider.Calendar(_left.Id).Add(PlainEvent("src-1", "Standup"));
        var syncer = CreateSyncer();

        await syncer.SyncAsync(CancellationToken.None);
        var third = await syncer.SyncAsync(CancellationToken.None);

        third.Created.ShouldBe(0);
        third.Updated.ShouldBe(0);
        third.Deleted.ShouldBe(0);
    }

    [Fact]
    public async Task PropagatesAnEditToTheMirror()
    {
        var original = PlainEvent("src-1", "Standup");
        _provider.Calendar(_left.Id).Add(original);
        var syncer = CreateSyncer();
        await syncer.SyncAsync(CancellationToken.None);

        _provider.Calendar(_left.Id)[0] = original with
        {
            Subject = "Standup (moved)",
            LastModified = Now.AddMinutes(5),
        };

        var result = await syncer.SyncAsync(CancellationToken.None);

        result.Updated.ShouldBe(1);
        _provider.Calendar(_right.Id).ShouldHaveSingleItem().Subject.ShouldBe("Standup (moved)");
    }

    [Fact]
    public async Task DeletesTheMirrorWhenTheSourceEventIsRemoved()
    {
        _provider.Calendar(_left.Id).Add(PlainEvent("src-1", "Standup"));
        var syncer = CreateSyncer();
        await syncer.SyncAsync(CancellationToken.None);

        _provider.Calendar(_left.Id).Clear();
        var result = await syncer.SyncAsync(CancellationToken.None);

        result.Deleted.ShouldBe(1);
        _provider.Calendar(_right.Id).ShouldBeEmpty();
        _links.All.ShouldBeEmpty();
    }

    [Fact]
    public async Task RemovesTheMirrorWhenTheSourceEventMovesOutOfRange()
    {
        var original = PlainEvent("src-1", "Standup");
        _provider.Calendar(_left.Id).Add(original);
        var syncer = CreateSyncer();
        await syncer.SyncAsync(CancellationToken.None);

        // The mirror still sits at the old time, which no longer reflects anything, so it has to go.
        _provider.Calendar(_left.Id)[0] = original with { Start = Now.AddYears(2), End = Now.AddYears(2).AddHours(1) };
        var result = await syncer.SyncAsync(CancellationToken.None);

        result.Deleted.ShouldBe(1);
        _provider.Calendar(_right.Id).ShouldBeEmpty();
    }

    [Fact]
    public async Task KeepsMirrorsWhoseSourceEventWasNeverInTheWindow()
    {
        var options = new CalendarSyncerOptions { LookAhead = TimeSpan.FromDays(60) };
        _provider.Calendar(_left.Id).Add(PlainEvent("src-1", "Standup") with
        {
            Start = Now.AddDays(30),
            End = Now.AddDays(30).AddHours(1),
        });
        await CreateSyncer(options: options).SyncAsync(CancellationToken.None);

        // Narrowing the look-ahead means the source event is no longer fetched. Absence outside the window
        // carries no information, so the mirror must survive rather than be mistaken for a deletion.
        options.LookAhead = TimeSpan.FromDays(10);
        var result = await CreateSyncer(options: options).SyncAsync(CancellationToken.None);

        result.Deleted.ShouldBe(0);
        _provider.Calendar(_right.Id).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task CarriesPrivacyAndAvailabilityOntoTheMirror()
    {
        _provider.Calendar(_left.Id).Add(PlainEvent("src-1", "Therapy") with
        {
            IsPrivate = true,
            Availability = EventAvailability.OutOfOffice,
        });

        await CreateSyncer().SyncAsync(CancellationToken.None);

        var mirror = _provider.Calendar(_right.Id).ShouldHaveSingleItem();
        mirror.IsPrivate.ShouldBeTrue();
        mirror.Availability.ShouldBe(EventAvailability.OutOfOffice);
    }

    [Fact]
    public async Task SkipsAccountsThatAreDisabled()
    {
        var store = new InMemoryAccountStore(_left, _right with { Enabled = false });
        _provider.Calendar(_left.Id).Add(PlainEvent("src-1", "Standup"));

        var result = await CreateSyncer(store).SyncAsync(CancellationToken.None);

        result.AccountsSynced.ShouldBe(1);
        _provider.Calendar(_right.Id).ShouldBeEmpty();
    }

    private CalendarSyncer CreateSyncer(IAccountStore? accountStore = null, CalendarSyncerOptions? options = null) =>
        new(
            accountStore ?? new InMemoryAccountStore(_left, _right),
            _links,
            new CalendarProviderRegistry([_provider], [new FakeAuthenticator()]),
            Options.Create(options ?? new CalendarSyncerOptions()),
            new FixedTimeProvider(Now),
            NullLogger<CalendarSyncer>.Instance);

    private static CalendarAccount NewAccount(string displayName) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProviderName = "fake",
            DisplayName = displayName,
            RefreshToken = "refresh",
            AccessToken = "access",
            AccessTokenExpiresAt = Now.AddHours(1),
            ConnectedAt = Now,
        };

    private static CalendarEvent PlainEvent(string id, string subject) =>
        new()
        {
            Id = id,
            Subject = subject,
            Start = Now.AddHours(1),
            End = Now.AddHours(2),
            LastModified = Now,
        };
}
