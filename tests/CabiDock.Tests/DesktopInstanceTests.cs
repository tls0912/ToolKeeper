using System.IO;
using Xunit;

namespace CabiDock.Tests;

public sealed class DesktopInstanceTests
{
    [Fact]
    public void EquivalentDataDirectorySpellingsShareOwnershipAcrossThreads()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CabiDock-lease-" + Guid.NewGuid().ToString("N"));
        using var primary = new SingleInstance(directory);
        Assert.True(primary.IsPrimary);
        var secondaryOwned = true;
        Exception? failure = null;
        var contender = new Thread(() =>
        {
            try
            {
                using var secondary = new SingleInstance(directory.ToUpperInvariant() + Path.DirectorySeparatorChar + ".");
                secondaryOwned = secondary.IsPrimary;
            }
            catch (Exception error) { failure = error; }
        }) { IsBackground = true };
        contender.Start();
        Assert.True(contender.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
        Assert.False(secondaryOwned);
        primary.Dispose();
        primary.Dispose();
        using var replacement = new SingleInstance(directory);
        Assert.True(replacement.IsPrimary);
    }
}
