using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NasMonitor.App.Persistence;
using NasMonitor.App.Services;
using NasMonitor.App.ViewModels;
using NasMonitor.Core.Connections;
using NasMonitor.Core.Monitoring;
using NasMonitor.Core.Profiles;
using NasMonitor.Core.Security;
using NasMonitor.Infrastructure.Monitoring;
using NasMonitor.Infrastructure.Persistence;
using NasMonitor.Infrastructure.Profiles;
using NasMonitor.Platform.Windows.Security;

namespace NasMonitor.Connection.Tests.Profiles;

[TestClass]
public sealed class ProfileTests
{
    private string testDirectory = "";
    private string FilePath => Path.Combine(testDirectory, "profiles.json");

    private static NasProfile Profile =>
        new(Guid.NewGuid(), "Test NAS", new Uri("https://nas.example:5001"), "test-user");

    [TestInitialize]
    public void CreateTestDirectory()
    {
        testDirectory = Path.Combine(Path.GetTempPath(), "NasMonitor.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
    }

    [TestCleanup]
    public void RemoveTestFiles()
    {
        // Delete only the known files created by this fixture; no recursive filesystem cleanup.
        File.Delete(FilePath);
        File.Delete(FilePath + ".tmp");
        Directory.Delete(testDirectory);
    }

    [TestMethod]
    public async Task IndependentProfilesRoundTripWithoutPasswordsInJson()
    {
        using var repository = new NasProfileRepository(FilePath);
        var secrets = new MemorySecrets();
        var service = new NasProfileService(repository, secrets);
        NasProfile first = await service.SaveAsync(Profile, "synthetic-first-secret", true, CancellationToken.None);
        NasProfile second = await service.SaveAsync(Profile with { Name = "Second NAS" },
            "synthetic-second-secret",
            true,
            CancellationToken.None);

        IReadOnlyList<NasProfile> loaded = await repository.LoadAsync(CancellationToken.None);
        string json = await File.ReadAllTextAsync(FilePath);

        Assert.AreEqual(2, loaded.Count);
        Assert.AreNotEqual(first.Id, second.Id);
        Assert.AreNotEqual(first.CredentialId, second.CredentialId);
        Assert.IsFalse(json.Contains("synthetic-first-secret", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("synthetic-second-secret", StringComparison.Ordinal));
        Assert.AreEqual("synthetic-first-secret", await service.ReadPasswordAsync(first, CancellationToken.None));
        Assert.AreEqual("synthetic-second-secret", await service.ReadPasswordAsync(second, CancellationToken.None));
    }

    [TestMethod]
    public async Task EditingEndpointOrUserCannotReuseSavedPassword()
    {
        using var repository = new NasProfileRepository(FilePath);
        var secrets = new MemorySecrets();
        var service = new NasProfileService(repository, secrets);
        NasProfile saved = await service.SaveAsync(Profile, "synthetic-secret", true, CancellationToken.None);
        NasProfile changed = saved with { Endpoint = new Uri("https://different.example:5001") };

        Assert.IsNull(await service.ReadPasswordAsync(changed, CancellationToken.None));
        Assert.IsNull(await service.ReadPasswordAsync(saved with { Username = "other-user" }, CancellationToken.None));
        NasProfile updated = await service.SaveAsync(changed, null, true, CancellationToken.None);

        Assert.IsNull(updated.CredentialId);
        Assert.AreEqual(0, secrets.Items.Count);
    }

    [TestMethod]
    public async Task ForgetAndDeleteRemoveOnlyAssociatedSecret()
    {
        using var repository = new NasProfileRepository(FilePath);
        var secrets = new MemorySecrets();
        var service = new NasProfileService(repository, secrets);
        NasProfile first = await service.SaveAsync(Profile, "synthetic-first", true, CancellationToken.None);
        NasProfile second = await service.SaveAsync(Profile, "synthetic-second", true, CancellationToken.None);

        await service.SaveAsync(first, null, false, CancellationToken.None);
        Assert.AreEqual("synthetic-second", await service.ReadPasswordAsync(second, CancellationToken.None));
        await service.DeleteAsync(second, CancellationToken.None);

        Assert.AreEqual(0, secrets.Items.Count);
        Assert.AreEqual(1, (await repository.LoadAsync(CancellationToken.None)).Count);
    }

    [TestMethod]
    public async Task RenamingAndDisablingKeepsTheSameIdentitySecret()
    {
        using var repository = new NasProfileRepository(FilePath);
        var secrets = new MemorySecrets();
        var service = new NasProfileService(repository, secrets);
        NasProfile saved = await service.SaveAsync(Profile, "synthetic-secret", true, CancellationToken.None);

        NasProfile updated = await service.SaveAsync(saved with { Name = "Renamed", Enabled = false },
            null,
            true,
            CancellationToken.None);

        Assert.AreEqual(saved.CredentialId, updated.CredentialId);
        Assert.AreEqual(1, secrets.Items.Count);
    }

    [DataTestMethod]
    [DataRow("http://nas.example:5000")]
    [DataRow("https://user:password@nas.example:5001")]
    [DataRow("https://nas.example:5001/dsm")]
    [DataRow("https://nas.example:5001/?capture=yes")]
    public void UnsafeEndpointsAreRejected(string endpoint)
    {
        NasProfile profile = Profile with { Endpoint = new Uri(endpoint) };
        Assert.ThrowsException<ArgumentException>(profile.Validate);
    }

    [DataTestMethod]
    [DataRow("{\"SchemaVersion\":2,\"Profiles\":[]}")]
    [DataRow("{\"SchemaVersion\":1,\"Profiles\":[null]}")]
    public async Task UnsupportedOrInvalidDocumentIsNeverOverwritten(string original)
    {
        await File.WriteAllTextAsync(FilePath, original);
        using var repository = new NasProfileRepository(FilePath);

        await Assert.ThrowsExceptionAsync<InvalidDataException>(() =>
            repository.UpsertAsync(Profile, CancellationToken.None));

        Assert.AreEqual(original, await File.ReadAllTextAsync(FilePath));
    }

    [TestMethod]
    public async Task MalformedDocumentIsNeverOverwritten()
    {
        const string original = "{broken";
        await File.WriteAllTextAsync(FilePath, original);
        using var repository = new NasProfileRepository(FilePath);

        await Assert.ThrowsExceptionAsync<JsonException>(() => repository.UpsertAsync(Profile, CancellationToken.None));

        Assert.AreEqual(original, await File.ReadAllTextAsync(FilePath));
    }

    [TestMethod]
    public async Task LeavingConnectionsPageClearsBothSecretsAndPreservesMetadata()
    {
        using var repository = new NasProfileRepository(FilePath);
        var service = new NasProfileService(repository, new MemorySecrets());
        await using var coordinator =
            new NasMonitoringCoordinator(new UnusedSessionFactory(), service, TimeProvider.System);
        using var presenter = new WidgetMonitoringPresenter(coordinator,
            new WidgetSettingsStore(Path.GetDirectoryName(FilePath)!),
            new WidgetViewModel(),
            false);
        using var viewModel = new NasConnectionsViewModel(service, new UnusedTester(), presenter)
        {
            ProfileName = "Draft profile",
            Endpoint = "https://nas.example:5001",
            Password = "synthetic-password",
            Otp = "123456"
        };

        viewModel.LeavePage();

        Assert.AreEqual("", viewModel.Password);
        Assert.AreEqual("", viewModel.Otp);
        Assert.AreEqual("Draft profile", viewModel.ProfileName);
    }

    [TestMethod]
    [TestCategory("WindowsIntegration")]
    public async Task WindowsCredentialManagerRoundTripAndDelete()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows-only OS credential integration test.");
        }

        var store = new WindowsSecretStore();
        var id = Guid.NewGuid();
        try
        {
            Assert.IsNull(await store.ReadAsync(id, CancellationToken.None));
            await store.WriteAsync(id, "synthetic-credential-✓", CancellationToken.None);
            Assert.AreEqual("synthetic-credential-✓", await store.ReadAsync(id, CancellationToken.None));
            await store.DeleteAsync(id, CancellationToken.None);
            Assert.IsNull(await store.ReadAsync(id, CancellationToken.None));
        }
        finally
        {
            await store.DeleteAsync(id, CancellationToken.None);
        }
    }

    private sealed class MemorySecrets : ISecretStore
    {
        public Dictionary<Guid, string> Items
        {
            get;
        } = [];

        public Task WriteAsync(Guid credentialId, string password, CancellationToken cancellationToken)
        {
            Items[credentialId] = password;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(Guid credentialId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Items.GetValueOrDefault(credentialId));
        }

        public Task DeleteAsync(Guid credentialId, CancellationToken cancellationToken)
        {
            Items.Remove(credentialId);
            return Task.CompletedTask;
        }
    }

    private sealed class UnusedTester : INasConnectionTester
    {
        public Task<NasConnectionResult> TestAsync(NasProfile profile,
            string password,
            string? otp,
            IProgress<ConnectionCheck>? progress,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("A page-navigation test must not contact a NAS.");
        }
    }

    private sealed class UnusedSessionFactory : INasMetricsSessionFactory
    {
        public Task<INasMetricsSession> ConnectAsync(NasProfile profile,
            string password,
            string? otp,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("A page-navigation test must not contact a NAS.");
        }
    }
}
