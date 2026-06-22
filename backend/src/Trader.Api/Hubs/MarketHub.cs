using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Trader.Api.Extensions;
using Trader.Api.Routing;
using Trader.Application.Streaming;

namespace Trader.Api.Hubs;

/// <summary>Realtime market ticks: subscribes to Kite WebSocket server-side and pushes batched LTP updates.</summary>
[Authorize]
public sealed class MarketHub : Hub
{
    public const string Path = ApiRoutes.HubsMarket;

    private readonly IKiteTickerSessionManager _sessions;

    public MarketHub(IKiteTickerSessionManager sessions) => _sessions = sessions;

    public static string UserGroupName(Guid userId) => $"u:{userId:D}";

    /// <summary>Subscribe to an instrument by numeric Kite token. Requires Zerodha connected and valid JWT (use <c>access_token</c> query on negotiate).</summary>
    public async Task SubscribeInstrument(string instrumentToken)
    {
        var userId = Context.User!.GetUserId();
        if (!uint.TryParse(
                instrumentToken.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var token)
            || token == 0)
            throw new HubException("Invalid instrumentToken.");

        await _sessions.SubscribeAsync(Context.ConnectionId, userId, token, Context.ConnectionAborted)
            .ConfigureAwait(false);
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroupName(userId)).ConfigureAwait(false);
    }

    public async Task UnsubscribeInstrument(string instrumentToken)
    {
        var userId = Context.User!.GetUserId();
        if (!uint.TryParse(
                instrumentToken.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var token)
            || token == 0)
            throw new HubException("Invalid instrumentToken.");

        await _sessions.UnsubscribeAsync(Context.ConnectionId, userId, token, Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await _sessions.RemoveConnectionAsync(Context.ConnectionId, Context.ConnectionAborted).ConfigureAwait(false);
        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }
}
