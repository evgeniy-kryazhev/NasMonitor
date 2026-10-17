using Microsoft.VisualStudio.TestTools.UnitTesting;
using NasMonitor.Core.Updates;

namespace NasMonitor.Core.Tests.Updates;

[TestClass]
public sealed class ReleaseVersionTests
{
    [TestMethod]
    public void StableTagsCompareNumerically()
    {
        Assert.IsTrue(ReleaseVersion.TryParse("v0.10.0", out Version newer));
        Assert.IsTrue(ReleaseVersion.TryParse("0.9.12", out Version older));
        Assert.IsTrue(newer > older);
    }

    [TestMethod]
    public void AmbiguousAndPrereleaseVersionsAreRejected()
    {
        string?[] invalid = [null, "", "v1.2", "v1.2.3.4", "v1.2.3-beta", "v1.2.3+build", "v01.2.3", "v-1.2.3"];
        foreach (string? tag in invalid)
        {
            Assert.IsFalse(ReleaseVersion.TryParse(tag, out _), tag);
        }
    }
}
