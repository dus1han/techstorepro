using System.Text;
using System.Text.Json;
using TechStorePro.API.Middleware;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Domain.Identity;
using TechStorePro.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace TechStorePro.Application.Tests;

/// <summary>
/// <c>Idempotency-Key</c> (api-design.md §5) — the carried debt P4 owed and P5 pays.
///
/// <b>The failure it exists to prevent is a double-clicked till.</b> The cashier taps "Take payment", the
/// network hesitates, they tap again — and without this the shop has taken the money twice and issued two
/// invoices that both look entirely legitimate. Unlike a duplicate invoice, nobody notices until they
/// count the drawer.
///
/// It is driven against a real PostgreSQL and not a mock, because the guarantee rests on a <b>unique
/// index</b>: the key is claimed by an INSERT before the work runs, and it is the database — not a
/// check-then-write in C# — that decides which of two simultaneous clicks wins. A mock cannot promise
/// that, and a test against one would prove nothing about the two clicks that actually race.
/// </summary>
public class IdempotencyTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("techstorepro_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private Guid _companyA;
    private Guid _companyB;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var seed = CreateContext(null);
        await seed.Database.MigrateAsync();

        var a = new Company { Name = "Gulf Computers", Code = "GULF01", BaseCurrency = "AED", TimeZone = "Asia/Dubai" };
        var b = new Company { Name = "Sharjah IT", Code = "SHJ01", BaseCurrency = "AED", TimeZone = "Asia/Dubai" };

        seed.Companies.AddRange(a, b);
        await seed.SaveChangesAsync();

        _companyA = a.Id;
        _companyB = b.Id;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task A_repeated_request_is_answered_from_the_record_and_does_not_run_twice()
    {
        // The double-click. The second one must get the first one's answer — and the work must not happen
        // again, which is the whole point: one payment, not two.
        await using var db = CreateContext(_companyA);

        var executions = 0;

        var first = await InvokeAsync(db, _companyA, "key-1", """{"amount":100}""", () =>
        {
            executions++;
            return new ObjectResult(new { id = "the-invoice" }) { StatusCode = StatusCodes.Status201Created };
        });

        var second = await InvokeAsync(db, _companyA, "key-1", """{"amount":100}""", () =>
        {
            executions++;
            return new ObjectResult(new { id = "a-second-invoice" }) { StatusCode = StatusCodes.Status201Created };
        });

        executions.Should().Be(1, "the retry must not sell the laptop a second time");

        // And the caller cannot tell the replay from the original.
        var replayed = second.Result.Should().BeOfType<ContentResult>().Subject;

        replayed.StatusCode.Should().Be(StatusCodes.Status201Created);
        replayed.Content.Should().Contain("the-invoice");
        replayed.Content.Should().NotContain("a-second-invoice");

        first.Result.Should().BeOfType<ObjectResult>();

        (await db.IdempotencyRecords.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_request_with_no_key_is_left_alone()
    {
        // The header is opt-in. A GET, or a caller that does not send one, must not be blocked.
        await using var db = CreateContext(_companyA);

        var executions = 0;

        await InvokeAsync(db, _companyA, key: null, body: "{}", () =>
        {
            executions++;
            return new OkResult();
        });

        await InvokeAsync(db, _companyA, key: null, body: "{}", () =>
        {
            executions++;
            return new OkResult();
        });

        executions.Should().Be(2);
        (await db.IdempotencyRecords.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task The_same_key_with_a_different_body_is_refused()
    {
        // Not a retry — a caller bug, or a key being reused. Replaying the first answer would hide it.
        await using var db = CreateContext(_companyA);

        await InvokeAsync(db, _companyA, "key-2", """{"amount":100}""", () => new OkObjectResult(new { ok = true }));

        var second = await InvokeAsync(
            db, _companyA, "key-2", """{"amount":999}""", () => new OkObjectResult(new { ok = true }));

        var problem = second.Result.Should().BeOfType<ObjectResult>().Subject;

        problem.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task A_request_still_in_flight_is_not_run_alongside_itself()
    {
        // The genuine race: two clicks 50ms apart. The first has claimed the key and is still working, so
        // the second is told to wait rather than being allowed to post the same payment concurrently.
        await using var db = CreateContext(_companyA);

        // The first request, claimed but not finished — exactly the state the filter writes before it
        // calls the action.
        db.IdempotencyRecords.Add(new Domain.Configuration.IdempotencyRecord
        {
            CompanyId = _companyA,
            Key = "key-3",
            Endpoint = "POST /api/v1/customer-payments",
            RequestHash = Hash("""{"amount":100}""")
        });

        await db.SaveChangesAsync();

        var executions = 0;

        var second = await InvokeAsync(db, _companyA, "key-3", """{"amount":100}""", () =>
        {
            executions++;
            return new OkResult();
        });

        executions.Should().Be(0);

        var problem = second.Result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task A_failed_request_gives_its_key_back()
    {
        // Holding the key on failure would make a mistyped payment unrepeatable — a worse trap than the
        // one being closed. The caller fixes the input and retries with the same key.
        await using var db = CreateContext(_companyA);

        await InvokeAsync(db, _companyA, "key-4", "{}", () => throw new InvalidOperationException("card declined"));

        (await db.IdempotencyRecords.AnyAsync(r => r.Key == "key-4"))
            .Should().BeFalse("a request that did not happen must not be remembered as if it had");

        var executions = 0;

        var retry = await InvokeAsync(db, _companyA, "key-4", "{}", () =>
        {
            executions++;
            return new OkObjectResult(new { ok = true });
        });

        executions.Should().Be(1, "the corrected request runs");
        retry.Result.Should().BeAssignableTo<ObjectResult>();
    }

    [Fact]
    public async Task Two_companies_may_use_the_same_key()
    {
        // Keys are generated by clients and two shops will collide eventually. One must never be handed
        // the other's response — that would be a cross-tenant leak through the retry path, which no query
        // filter would catch.
        await using var asA = CreateContext(_companyA);
        await using var asB = CreateContext(_companyB);

        await InvokeAsync(asA, _companyA, "same-key", "{}", () => new OkObjectResult(new { who = "A" }));

        var executions = 0;

        var forB = await InvokeAsync(asB, _companyB, "same-key", "{}", () =>
        {
            executions++;
            return new OkObjectResult(new { who = "B" });
        });

        executions.Should().Be(1, "B's request is B's own, not a replay of A's");

        var result = forB.Result.Should().BeOfType<OkObjectResult>().Subject;
        JsonSerializer.Serialize(result.Value).Should().Contain("B");
    }

    // --- Fixture ------------------------------------------------------------------------------------

    private static string Hash(string body) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(body)));

    /// <summary>
    /// Drives the filter the way MVC does: build the executing context, let the filter decide whether to
    /// call the action, and hand back what came out.
    /// </summary>
    private async Task<ActionExecutedContext> InvokeAsync(
        ApplicationDbContext db,
        Guid companyId,
        string? key,
        string body,
        Func<IActionResult> action)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton<ITenantContext>(new StubTenant(companyId));
        services.AddSingleton<IDateTime>(new StubClock());

        var http = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };

        http.Request.Method = "POST";
        http.Request.Path = "/api/v1/customer-payments";
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        if (key is not null)
        {
            http.Request.Headers[IdempotencyFilter.HeaderName] = key;
        }

        var actionContext = new ActionContext(http, new RouteData(), new ControllerActionDescriptor());

        var executing = new ActionExecutingContext(
            actionContext,
            [],
            new Dictionary<string, object?>(),
            controller: null!);

        var executed = new ActionExecutedContext(actionContext, [], controller: null!);

        await new IdempotencyFilter().OnActionExecutionAsync(executing, () =>
        {
            // The filter short-circuits by setting Result and never calling this — which is exactly what a
            // replay and a conflict do.
            try
            {
                executed.Result = action();
            }
            catch (Exception ex)
            {
                executed.Exception = ex;
            }

            return Task.FromResult(executed);
        });

        // When the filter short-circuited, the answer is on the executing context.
        if (executing.Result is not null)
        {
            executed.Result = executing.Result;
        }

        return executed;
    }

    private ApplicationDbContext CreateContext(Guid? companyId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations", "techstorepro"))
            .Options;

        return new ApplicationDbContext(options, new StubTenant(companyId), new StubUser(), new StubClock());
    }

    private sealed class StubTenant(Guid? companyId) : ITenantContext
    {
        public Guid? CompanyId { get; } = companyId;
        public bool HasTenant => CompanyId.HasValue;
    }

    private sealed class StubUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string? Username => "tests";
        public bool IsAuthenticated => true;
        public bool IsPlatformAdmin => false;
        public string? IpAddress => "203.0.113.7";
        public string? UserAgent => "tests";
    }

    private sealed class StubClock : IDateTime
    {
        public DateTimeOffset UtcNow => new(2026, 7, 13, 9, 0, 0, TimeSpan.Zero);
    }
}
