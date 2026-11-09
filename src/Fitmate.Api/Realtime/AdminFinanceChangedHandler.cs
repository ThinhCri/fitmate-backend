using Fitmate.Application.Outbox;
using Microsoft.AspNetCore.SignalR;

namespace Fitmate.Api.Realtime;

// Payload deliberately contains no orders, identities or monetary values.
// Clients re-read authorized APIs after invalidation; reconnect catches missed events.
public sealed class AdminFinanceChangedHandler(IHubContext<AdminFinanceHub> hub) : IOutboxHandler
{
    public string Type => "admin.finance.changed";
    public Task HandleAsync(string payloadJson, CancellationToken ct) =>
        hub.Clients.Group(AdminFinanceHub.Group).SendAsync("finance.updated", new { scope = "ai-sales" }, ct);
}
