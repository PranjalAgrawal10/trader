using System.Collections.Concurrent;
using KiteConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Application.Broker;
using Trader.Application.Configuration;
using Trader.Application.Streaming;

namespace Trader.Infrastructure.Streaming;

/// <summary>
/// Maintains one <see cref="Ticker"/> per user (Kite allows one WS per API key per user session).
/// Reference-counts instrument tokens across SignalR connections.
/// </summary>
public sealed class KiteTickerSessionManager : IKiteTickerSessionManager, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMarketTickDispatcher _dispatcher;
    private readonly IOptions<ZerodhaKiteOptions> _kiteOptions;
    private readonly ILogger<KiteTickerSessionManager> _logger;
    private readonly ConcurrentDictionary<string, ConnectionSubscriptions> _byConnection = new();
    private readonly ConcurrentDictionary<Guid, UserTickerRuntime> _userSessions = new();

    public KiteTickerSessionManager(
        IServiceScopeFactory scopeFactory,
        IMarketTickDispatcher dispatcher,
        IOptions<ZerodhaKiteOptions> kiteOptions,
        ILogger<KiteTickerSessionManager> logger)
    {
        _scopeFactory = scopeFactory;
        _dispatcher = dispatcher;
        _kiteOptions = kiteOptions;
        _logger = logger;
    }

    public async Task SubscribeAsync(string connectionId, Guid userId, uint instrumentToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionId);
        if (instrumentToken == 0)
            throw new InvalidOperationException("instrumentToken is required.");

        await RequireZerodhaAndTokenAsync(userId, ct).ConfigureAwait(false);

        var apiKey = _kiteOptions.Value.ApiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("Zerodha Kite is not configured (ZerodhaKite:ApiKey).");

        var accessToken = await GetAccessTokenAsync(userId, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(accessToken))
            throw new InvalidOperationException("No valid Kite session. Reconnect Zerodha.");

        var conn = _byConnection.GetOrAdd(connectionId, _ => new ConnectionSubscriptions(userId));
        var added = conn.TryAddInstrument(userId, instrumentToken);
        if (!added)
            return;

        var runtime = GetOrCreateRuntime(userId, apiKey, accessToken);
        runtime.SubscribeInstrument(instrumentToken);
    }

    public Task UnsubscribeAsync(string connectionId, Guid userId, uint instrumentToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionId);
        if (!_byConnection.TryGetValue(connectionId, out var conn))
            return Task.CompletedTask;

        if (!conn.TryRemoveInstrument(userId, instrumentToken))
            return Task.CompletedTask;

        if (_userSessions.TryGetValue(userId, out var runtime))
            runtime.UnsubscribeInstrument(instrumentToken);

        return Task.CompletedTask;
    }

    public Task RemoveConnectionAsync(string connectionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionId);
        if (!_byConnection.TryRemove(connectionId, out var conn))
            return Task.CompletedTask;

        var userId = conn.UserId;
        foreach (var token in conn.DrainInstruments())
        {
            if (_userSessions.TryGetValue(userId, out var runtime))
                runtime.UnsubscribeInstrument(token);
        }

        return Task.CompletedTask;
    }

    private UserTickerRuntime GetOrCreateRuntime(Guid userId, string apiKey, string accessToken)
    {
        if (_userSessions.TryGetValue(userId, out var existing))
            return existing;

        var created = new UserTickerRuntime(
            userId,
            apiKey,
            accessToken,
            _dispatcher,
            _logger,
            onEmpty: RemoveUserSession);

        if (_userSessions.TryAdd(userId, created))
            return created;

        created.DisposeQuietly();
        return _userSessions[userId];
    }

    private void RemoveUserSession(Guid userId) => _userSessions.TryRemove(userId, out _);

    private async Task RequireZerodhaAndTokenAsync(Guid userId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<IBrokerSetupGateway>();
        var snapshot = await setup.GetSnapshotAsync(userId, ct).ConfigureAwait(false);
        if (snapshot is null || !string.Equals(snapshot.BrokerProvider, "Zerodha", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Zerodha (Kite) is not connected.");
    }

    private async Task<string?> GetAccessTokenAsync(Guid userId, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<IBrokerSetupGateway>();
        return await setup.GetKiteAccessTokenAsync(userId, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        foreach (var kv in _userSessions.ToArray())
        {
            kv.Value.DisposeQuietly();
        }

        _userSessions.Clear();
        _byConnection.Clear();
    }

    private sealed class ConnectionSubscriptions
    {
        private readonly object _gate = new();
        private readonly HashSet<uint> _tokens = new();

        public ConnectionSubscriptions(Guid userId) => UserId = userId;

        public Guid UserId { get; }

        public bool TryAddInstrument(Guid userId, uint instrumentToken)
        {
            if (userId != UserId)
                throw new InvalidOperationException("Connection belongs to a different user.");
            lock (_gate)
            {
                return _tokens.Add(instrumentToken);
            }
        }

        public bool TryRemoveInstrument(Guid userId, uint instrumentToken)
        {
            if (userId != UserId)
                return false;
            lock (_gate)
            {
                return _tokens.Remove(instrumentToken);
            }
        }

        public IEnumerable<uint> DrainInstruments()
        {
            lock (_gate)
            {
                var list = _tokens.ToArray();
                _tokens.Clear();
                return list;
            }
        }
    }

    private sealed class UserTickerRuntime : IDisposable
    {
        private readonly object _gate = new();
        private readonly Guid _userId;
        private readonly string _apiKey;
        private readonly string _accessToken;
        private readonly IMarketTickDispatcher _dispatcher;
        private readonly ILogger _logger;
        private readonly Action<Guid> _onEmpty;
        private readonly Dictionary<uint, int> _tokenRefs = new();
        private Ticker? _ticker;

        private void OnTickerError(string message) =>
            _logger.LogWarning("Kite ticker error for user {UserId}: {Message}", _userId, message);

        public UserTickerRuntime(
            Guid userId,
            string apiKey,
            string accessToken,
            IMarketTickDispatcher dispatcher,
            ILogger logger,
            Action<Guid> onEmpty)
        {
            _userId = userId;
            _apiKey = apiKey;
            _accessToken = accessToken;
            _dispatcher = dispatcher;
            _logger = logger;
            _onEmpty = onEmpty;
        }

        public void SubscribeInstrument(uint token)
        {
            lock (_gate)
            {
                EnsureTickerLocked();
                if (_tokenRefs.TryGetValue(token, out var n))
                    _tokenRefs[token] = n + 1;
                else
                {
                    _tokenRefs[token] = 1;
                    _ticker!.Subscribe(new[] { token });
                    _ticker.SetMode(new[] { token }, "ltp");
                }
            }
        }

        public void UnsubscribeInstrument(uint token)
        {
            lock (_gate)
            {
                if (!_tokenRefs.TryGetValue(token, out var n))
                    return;

                if (n <= 1)
                {
                    _tokenRefs.Remove(token);
                    _ticker?.UnSubscribe(new[] { token });
                }
                else
                {
                    _tokenRefs[token] = n - 1;
                }

                if (_tokenRefs.Count == 0)
                {
                    TearDownTickerLocked();
                    _onEmpty(_userId);
                }
            }
        }

        private void EnsureTickerLocked()
        {
            if (_ticker is not null)
                return;

            var ticker = new Ticker(_apiKey, _accessToken, null, true, 5, 50, false, null);
            ticker.OnTick += OnTick;
            ticker.OnError += OnTickerError;
            ticker.Connect();
            _ticker = ticker;
        }

        private void TearDownTickerLocked()
        {
            if (_ticker is null)
                return;

            try
            {
                _ticker.OnTick -= OnTick;
                _ticker.OnError -= OnTickerError;
                _ticker.Close();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Kite ticker close for user {UserId}", _userId);
            }
            finally
            {
                _ticker = null;
            }
        }

        private void OnTick(Tick tick)
        {
            var dto = new MarketTickDto(
                tick.InstrumentToken,
                tick.LastPrice,
                tick.Volume,
                UnixTimestamp(tick));
            _dispatcher.Publish(_userId, dto);
        }

        private static long? UnixTimestamp(Tick tick)
        {
            if (tick.Timestamp is not { } ts)
                return null;
            return new DateTimeOffset(ts).ToUnixTimeSeconds();
        }

        public void DisposeQuietly()
        {
            lock (_gate)
            {
                _tokenRefs.Clear();
                TearDownTickerLocked();
            }
        }

        public void Dispose() => DisposeQuietly();
    }
}
