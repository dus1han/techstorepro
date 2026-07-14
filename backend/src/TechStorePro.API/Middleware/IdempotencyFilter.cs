using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TechStorePro.Application.Common.Interfaces;
using TechStorePro.Domain.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace TechStorePro.API.Middleware;

/// <summary>
/// Honours the <c>Idempotency-Key</c> header on state-changing requests (api-design.md §5).
///
/// <b>The failure this exists to prevent is a double-clicked till.</b> The cashier taps "Take payment",
/// the network hesitates, they tap again — and without this, the shop has sold the laptop twice, taken
/// the money twice, and issued two invoices that both look entirely legitimate. A duplicate write-off or
/// a duplicate payment is not discovered until someone counts the drawer.
///
/// How it works, and why in this order:
///
/// <list type="number">
/// <item>The key is <b>claimed before the work runs</b>, by inserting a record with no status. The unique
///   index on (company, key) makes that claim atomic — two clicks 50ms apart cannot both win it. Claiming
///   it afterwards would leave the exact window this closes wide open.</item>
/// <item>If the claim fails because the key already exists and that request <b>finished</b>, its stored
///   response is replayed. The caller cannot tell a replay from the original, which is the point: their
///   retry succeeds, and nothing happens twice.</item>
/// <item>If it exists but has <b>not</b> finished, the first request is still in flight: 409. Executing
///   concurrently would be the double-post itself.</item>
/// <item>If the same key arrives with a <b>different body</b>, that is not a retry — it is a caller bug or
///   a reused key, and replaying the first answer would hide it. 422.</item>
/// </list>
///
/// A request that <b>fails</b> releases its key: the record is deleted, so the caller may fix the input
/// and try again with the same key. Holding the key on failure would make a mistyped payment
/// unrepeatable, which is a worse trap than the one being closed.
/// </summary>
public class IdempotencyFilter : IAsyncActionFilter
{
    public const string HeaderName = "Idempotency-Key";

    private static readonly string[] StateChanging = ["POST", "PUT", "PATCH", "DELETE"];

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;

        if (!StateChanging.Contains(request.Method)
            || !request.Headers.TryGetValue(HeaderName, out var header)
            || string.IsNullOrWhiteSpace(header))
        {
            await next();
            return;
        }

        var services = context.HttpContext.RequestServices;
        var db = services.GetRequiredService<IApplicationDbContext>();
        var tenant = services.GetRequiredService<ITenantContext>();
        var clock = services.GetRequiredService<IDateTime>();

        if (!tenant.HasTenant)
        {
            // No company means no scope to claim the key in. Such a request cannot reach a tenant
            // controller anyway (the Tenant policy refuses it), so there is nothing to protect here.
            await next();
            return;
        }

        var key = header.ToString();
        var endpoint = $"{request.Method} {request.Path}";
        var hash = await HashBodyAsync(context);

        var existing = await db.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Key == key && r.Endpoint == endpoint);

        if (existing is not null)
        {
            if (existing.RequestHash != hash)
            {
                context.Result = new ObjectResult(new ProblemDetails
                {
                    Status = StatusCodes.Status422UnprocessableEntity,
                    Title = "Idempotency key reused",
                    Detail = "This Idempotency-Key has already been used for a different request. "
                             + "Generate a new key rather than reusing one — replaying the first "
                             + "response for a different request would hide the mistake."
                })
                {
                    StatusCode = StatusCodes.Status422UnprocessableEntity
                };

                return;
            }

            if (!existing.IsComplete)
            {
                context.Result = new ObjectResult(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Request already in flight",
                    Detail = "An identical request is still being processed. Wait for it rather than "
                             + "sending it again."
                })
                {
                    StatusCode = StatusCodes.Status409Conflict
                };

                return;
            }

            // The retry. It gets the original answer, and nothing happens a second time.
            context.Result = new ContentResult
            {
                StatusCode = existing.StatusCode,
                Content = existing.ResponseBody,
                ContentType = "application/json"
            };

            return;
        }

        var record = new IdempotencyRecord
        {
            Key = key,
            Endpoint = endpoint,
            RequestHash = hash
        };

        db.IdempotencyRecords.Add(record);

        try
        {
            // Claim it before doing the work. If a concurrent click got here first, the unique index
            // throws, and the loser is told the request is already in flight rather than repeating it.
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Request already in flight",
                Detail = "An identical request is already being processed."
            })
            {
                StatusCode = StatusCodes.Status409Conflict
            };

            return;
        }

        var executed = await next();

        if (executed.Exception is not null && !executed.ExceptionHandled)
        {
            // The work failed. Release the key so the caller can correct the request and retry with it —
            // a mistyped payment that could never be re-sent would be a worse trap than a double-post.
            db.IdempotencyRecords.Remove(record);
            await db.SaveChangesAsync();

            return;
        }

        var (status, body) = Describe(executed.Result);

        record.StatusCode = status;
        record.ResponseBody = body;
        record.CompletedAt = clock.UtcNow;

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The response, as the caller saw it, so a replay is byte-for-byte the same answer.
    /// </summary>
    private static (int Status, string? Body) Describe(IActionResult? result) => result switch
    {
        ObjectResult obj => (
            obj.StatusCode ?? StatusCodes.Status200OK,
            obj.Value is null ? null : JsonSerializer.Serialize(obj.Value, JsonOptions)),

        StatusCodeResult code => (code.StatusCode, null),

        _ => (StatusCodes.Status200OK, null)
    };

    /// <summary>
    /// Matches the API's own serialisation, so the body a replay returns is the body the first caller got
    /// rather than a differently-cased version of it.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private static async Task<string> HashBodyAsync(ActionExecutingContext context)
    {
        var request = context.HttpContext.Request;

        request.EnableBuffering();
        request.Body.Position = 0;

        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        request.Body.Position = 0;

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)));
    }
}
