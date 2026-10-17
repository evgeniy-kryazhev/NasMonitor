using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;
using NasMonitor.Core;

namespace NasMonitor.Infrastructure;

/// <summary>Owns one sequential resource poller per NAS and a shared immutable state cache.</summary>
public sealed class NasMonitoringCoordinator(INasMetricsSessionFactory sessions, NasProfileService profiles,
    TimeProvider timeProvider) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, NasMonitorState> states = new();
    private readonly Dictionary<Guid, Worker> workers = [];
    private readonly SemaphoreSlim changes = new(1, 1);
    private readonly SemaphoreSlim requests = new(3, 3);
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed;

    /// <summary>Raised on a background thread; UI subscribers must dispatch to their UI thread.</summary>
    public event Action<NasMonitorState>? StateChanged;

    /// <summary>Read shared state without creating another poller or HTTP request.</summary>
    public NasMonitorState? GetState(Guid nasId)
    {
        return states.GetValueOrDefault(nasId);
    }

    /// <summary>Coalesce manual refresh requests; a stopped authorization flow requires explicit reconnection.</summary>
    public async Task RefreshAsync(Guid nasId, CancellationToken cancellationToken)
    {
        await changes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!disposed && workers.TryGetValue(nasId, out Worker? worker) && !worker.Task.IsCompleted &&
                worker.Refresh.CurrentCount == 0)
            {
                worker.Refresh.Release();
            }
        }
        finally
        {
            changes.Release();
        }
    }

    /// <summary>Start new profiles, restart changed identities, and stop removed/disabled NAS.</summary>
    public async Task SynchronizeAsync(IReadOnlyList<NasProfile> configuredProfiles, CancellationToken cancellationToken)
    {
        await changes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            foreach (Guid id in workers.Keys.Except(configuredProfiles.Select(profile => profile.Id)).ToArray())
            {
                await StopWorkerAsync(id).ConfigureAwait(false);
                states.TryRemove(id, out _);
            }
            foreach (NasProfile profile in configuredProfiles)
            {
                if (workers.TryGetValue(profile.Id, out Worker? existing) && existing.Profile == profile)
                {
                    continue;
                }
                await StopWorkerAsync(profile.Id).ConfigureAwait(false);
                StartWorker(profile, profile.Endpoint, null, null, CancellationToken.None);
            }
        }
        finally
        {
            changes.Release();
        }
    }

    /// <summary>Explicitly reconnect one saved NAS, with an optional one-time password and OTP.</summary>
    public async Task<NasMonitorState> ConnectAsync(NasProfile profile, Uri endpoint, string? password, string? otp,
        CancellationToken cancellationToken)
    {
        profile.Validate();
        if (endpoint != profile.Endpoint && endpoint != profile.ExternalEndpoint)
        {
            throw new ArgumentException("monitor.endpoint-not-configured");
        }
        Worker worker;
        await changes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            await StopWorkerAsync(profile.Id).ConfigureAwait(false);
            worker = StartWorker(profile, endpoint, password, otp, cancellationToken);
        }
        finally
        {
            changes.Release();
        }
        return await worker.FirstAttempt.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private Worker StartWorker(NasProfile profile, Uri endpoint, string? password, string? otp,
        CancellationToken cancellationToken)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        var worker = new Worker(profile, cancellation);
        workers.Add(profile.Id, worker);
        Publish(new(profile.Id, profile.Enabled ? NasMonitorStatus.Connecting : NasMonitorStatus.Disabled));
        // No widget owns this task. Multiple subscribers only read states.
        worker.Task = Task.Run(async () =>
        {
            Task polling = RunAsync(worker, endpoint, password, otp);
            password = null;
            otp = null;
            await polling.ConfigureAwait(false);
        }, CancellationToken.None);
        return worker;
    }

    private async Task RunAsync(Worker worker, Uri endpoint, string? password, string? otp)
    {
        INasMetricsSession? session = null;
        NasSnapshot? lastSnapshot = null;
        DateTimeOffset? lastSuccess = null;
        int consecutiveFailures = 0;
        bool sessionRetryUsed = false;
        bool forceRefresh = false;
        CancellationToken cancellationToken = worker.Cancellation.Token;
        try
        {
            if (!worker.Profile.Enabled)
            {
                Publish(new(worker.Profile.Id, NasMonitorStatus.Disabled), worker);
                return;
            }
            while (!cancellationToken.IsCancellationRequested)
            {
                DateTimeOffset attemptAt = timeProvider.GetUtcNow();
                try
                {
                    await requests.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        timeout.CancelAfter(TimeSpan.FromSeconds(30));
                        if (session is null)
                        {
                            password = string.IsNullOrEmpty(password)
                                ? await profiles.ReadPasswordAsync(worker.Profile, timeout.Token).ConfigureAwait(false)
                                : password;
                            if (string.IsNullOrEmpty(password))
                            {
                                throw new NasProviderException(ConnectionError.MissingPassword);
                            }
                            try
                            {
                                session = await sessions.ConnectAsync(worker.Profile with { Endpoint = endpoint },
                                    password, otp, timeout.Token).ConfigureAwait(false);
                            }
                            finally
                            {
                                password = null;
                                otp = null;
                            }
                        }
                        NasSnapshot snapshot = await session.ReadAsync(timeout.Token, forceRefresh).ConfigureAwait(false);
                        bool complete = snapshot.Metrics.All(metric => metric?.Availability == MetricAvailability.Available);
                        bool anyAvailable = snapshot.Metrics.Any(metric => metric?.Availability == MetricAvailability.Available);
                        NasMonitorStatus status = complete ? NasMonitorStatus.Online : NasMonitorStatus.Degraded;
                        if (!anyAvailable && snapshot.Metrics.Any(metric => metric?.Availability == MetricAvailability.PermissionDenied))
                        {
                            status = NasMonitorStatus.PermissionDenied;
                        }
                        else if (snapshot.Metrics.All(metric => metric?.Availability == MetricAvailability.Unsupported))
                        {
                            status = NasMonitorStatus.Unsupported;
                        }
                        lastSnapshot = snapshot;
                        if (anyAvailable)
                        {
                            lastSuccess = snapshot.Metrics.Where(metric => metric?.Availability == MetricAvailability.Available)
                                .Max(metric => metric!.CollectedAt);
                        }
                        consecutiveFailures = 0;
                        sessionRetryUsed = false;
                        Publish(new(worker.Profile.Id, status, snapshot, attemptAt, lastSuccess), worker);
                    }
                    finally
                    {
                        requests.Release();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception) when (IsExpectedFailure(exception))
                {
                    password = null;
                    otp = null;
                    (ConnectionError error, int? code) = Classify(exception);
                    NasMonitorStatus status = StatusFor(error);
                    Publish(new(worker.Profile.Id, status, lastSnapshot, attemptAt, lastSuccess, error, code),
                        error == ConnectionError.SessionExpired && !sessionRetryUsed ? null : worker);
                    if (error == ConnectionError.SessionExpired && !sessionRetryUsed)
                    {
                        sessionRetryUsed = true;
                        if (session is not null)
                        {
                            await session.DisposeAsync().ConfigureAwait(false);
                            session = null;
                        }
                        // Retry a saved password once; OTP is never reused.
                        continue;
                    }
                    if (status is NasMonitorStatus.AuthenticationRequired or NasMonitorStatus.PermissionDenied or
                        NasMonitorStatus.Unsupported || error is ConnectionError.Tls or ConnectionError.InvalidResponse)
                    {
                        return;
                    }
                    consecutiveFailures++;
                }
                TimeSpan delay = RetryDelay(consecutiveFailures);
                forceRefresh = await worker.Refresh.WaitAsync(delay, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            password = null;
            otp = null;
            if (session is not null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            if (cancellationToken.IsCancellationRequested)
            {
                Publish(new(worker.Profile.Id, NasMonitorStatus.Stopped, lastSnapshot,
                    LastSuccessAt: lastSuccess), worker);
            }
            worker.FirstAttempt.TrySetResult(GetState(worker.Profile.Id) ?? new(worker.Profile.Id, NasMonitorStatus.Stopped));
        }
    }

    private static TimeSpan RetryDelay(int failures)
    {
        if (failures == 0)
        {
            return TimeSpan.FromSeconds(10);
        }
        double seconds = Math.Min(300, 10 * Math.Pow(2, Math.Min(failures, 5)));
        return TimeSpan.FromSeconds(seconds * (0.8 + Random.Shared.NextDouble() * 0.2));
    }

    private static bool IsExpectedFailure(Exception exception)
    {
        return exception is NasProviderException or HttpRequestException or IOException or SocketException or
            AuthenticationException or OperationCanceledException or JsonException or KeyNotFoundException or
            InvalidOperationException or FormatException or OverflowException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or NotSupportedException;
    }

    private static (ConnectionError, int?) Classify(Exception exception)
    {
        return exception switch
        {
            NasProviderException failure => (failure.Error, failure.DsmCode),
            AuthenticationException => (ConnectionError.Tls, null),
            HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } => (ConnectionError.Tls, null),
            HttpRequestException or SocketException or IOException => (ConnectionError.Network, null),
            OperationCanceledException => (ConnectionError.Timeout, null),
            UnauthorizedAccessException or System.ComponentModel.Win32Exception or NotSupportedException => (ConnectionError.MissingPassword, null),
            _ => (ConnectionError.InvalidResponse, null)
        };
    }

    private static NasMonitorStatus StatusFor(ConnectionError error)
    {
        return error switch
        {
            ConnectionError.MissingPassword or ConnectionError.InvalidCredentials or ConnectionError.AccountDisabled or
                ConnectionError.OtpRequired or ConnectionError.InvalidOtp or ConnectionError.BlockedIp or
                ConnectionError.PasswordChangeRequired or ConnectionError.SessionExpired => NasMonitorStatus.AuthenticationRequired,
            ConnectionError.PermissionDenied => NasMonitorStatus.PermissionDenied,
            ConnectionError.UnsupportedApi => NasMonitorStatus.Unsupported,
            _ => NasMonitorStatus.Offline
        };
    }

    private void Publish(NasMonitorState state, Worker? worker = null)
    {
        states[state.NasId] = state;
        StateChanged?.Invoke(state);
        worker?.FirstAttempt.TrySetResult(state);
    }

    private async Task StopWorkerAsync(Guid id)
    {
        if (workers.Remove(id, out Worker? worker))
        {
            await worker.Cancellation.CancelAsync().ConfigureAwait(false);
            await worker.Task.ConfigureAwait(false);
            worker.Cancellation.Dispose();
            worker.Refresh.Dispose();
        }
    }

    /// <summary>Cancel and await all pollers and their bounded logout before releasing resources.</summary>
    public async ValueTask DisposeAsync()
    {
        await changes.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            await lifetime.CancelAsync().ConfigureAwait(false);
            foreach (Guid id in workers.Keys.ToArray())
            {
                await StopWorkerAsync(id).ConfigureAwait(false);
            }
            lifetime.Dispose();
            requests.Dispose();
        }
        finally
        {
            changes.Release();
        }
    }

    private sealed class Worker(NasProfile profile, CancellationTokenSource cancellation)
    {
        internal NasProfile Profile { get; } = profile;
        internal CancellationTokenSource Cancellation { get; } = cancellation;
        internal TaskCompletionSource<NasMonitorState> FirstAttempt { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Task { get; set; } = Task.CompletedTask;
        internal SemaphoreSlim Refresh { get; } = new(0, 1);
    }
}
