using NasMonitor.App.Monitoring;
using NasMonitor.Core;
using Xunit;

namespace NasMonitor.NativeTests;

public sealed class WindowsCredentialTests
{
    [WindowsFact]
    public void CredentialManagerRoundTripUpdateAndDeleteUseIsolatedRandomTarget()
    {
        var store = new WindowsCredentialStore();
        var id = Guid.NewGuid();
        try
        {
            Assert.Null(store.Read(id));
            var initial = new NasCredentials("fixture-reader", "fixture-password");
            store.Write(id, initial);
            Assert.Equal(initial, store.Read(id));
            var updated = new NasCredentials("fixture-reader", "fixture-replacement");
            store.Write(id, updated);
            Assert.Equal(updated, store.Read(id));
            store.Delete(id);
            Assert.Null(store.Read(id));
        }
        finally { store.Delete(id); }
    }
}
