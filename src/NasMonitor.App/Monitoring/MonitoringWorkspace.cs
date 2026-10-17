using NasMonitor.Core;
using NasMonitor.Providers.Synology;

namespace NasMonitor.App.Monitoring;

internal sealed class MonitoringWorkspace : IAsyncDisposable
{
    private readonly ProfileStore _profiles;
    private readonly ICredentialStore _credentials;
    private readonly Func<NasProfile, NasCredentials, string?, INasSession> _create;
    private readonly SemaphoreSlim _changes = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<NasProfile> _items = [];
    private bool _initialized;
    public NasMonitorService Monitor { get; } = new();
    public bool CanRemember => _credentials.IsAvailable;
    public IReadOnlyList<NasProfile> Profiles => _items.ToArray();
    public MonitoringWorkspace(ProfileStore? profiles = null, ICredentialStore? credentials = null,
        Func<NasProfile, NasCredentials, string?, INasSession>? create = null)
    {
        _profiles = profiles ?? ProfileStore.Default();
        _credentials = credentials ?? CredentialStore.Create();
        _create = create ?? ((profile, credential, otp) => new DsmSession(profile, credential, otp));
    }
    public async Task InitializeAsync()
    {
        await _changes.WaitAsync(_lifetime.Token);
        try
        {
            if (_initialized) return;
            _items.AddRange(await _profiles.LoadAsync(_lifetime.Token));
            _initialized = true;
            foreach (var profile in _items.Where(x => x.RememberCredentials))
            {
                var credential = await Task.Run(() => _credentials.Read(profile.Id), _lifetime.Token);
                if (credential is not null) Monitor.Start(profile, _create(profile, credential, null));
            }
        }
        finally { _changes.Release(); }
    }
    public async Task ConnectAsync(NasProfile profile, NasCredentials credential, string? otp)
    {
        await _changes.WaitAsync(_lifetime.Token);
        try
        {
            // Dispose the old worker before creating another: never two pollers for the same NAS.
            await Monitor.StopAsync(profile.Id);
            if (profile.RememberCredentials)
                await Task.Run(() => _credentials.Write(profile.Id, credential), _lifetime.Token);
            else await Task.Run(() => _credentials.Delete(profile.Id), _lifetime.Token);
            var next = _items.Where(x => x.Id != profile.Id).Append(profile).ToArray();
            await _profiles.SaveAsync(next, _lifetime.Token);
            _items.Clear();
            _items.AddRange(next);
            Monitor.Start(profile, _create(profile, credential, otp));
        }
        finally { _changes.Release(); }
    }
    public async Task DisconnectAsync(Guid id)
    {
        await _changes.WaitAsync(_lifetime.Token);
        try { await Monitor.StopAsync(id); }
        finally { _changes.Release(); }
    }
    public async Task RemoveAsync(Guid id)
    {
        await _changes.WaitAsync(_lifetime.Token);
        try
        {
            await Monitor.StopAsync(id);
            await Task.Run(() => _credentials.Delete(id), _lifetime.Token);
            var next = _items.Where(x => x.Id != id).ToArray();
            await _profiles.SaveAsync(next, _lifetime.Token);
            _items.RemoveAll(x => x.Id == id);
        }
        finally { _changes.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        await _changes.WaitAsync();
        try { await Monitor.DisposeAsync(); }
        finally { _changes.Release(); _lifetime.Dispose(); }
    }
}
