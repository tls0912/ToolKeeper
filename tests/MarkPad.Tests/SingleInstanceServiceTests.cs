using System.IO;
using MarkPad.Services;
using Xunit;

namespace MarkPad.Tests;

public sealed class SingleInstanceServiceTests
{
    [Fact]
    public async Task SecondaryForwardsPathsAndWindowChoiceToPrimary()
    {
        var name = "MarkPad.Tests." + Guid.NewGuid().ToString("N");
        using var primary = new SingleInstanceService(name);
        using var secondary = new SingleInstanceService(name);
        Assert.True(primary.IsPrimary);
        Assert.False(secondary.IsPrimary);
        var received = new TaskCompletionSource<(string[] Paths, bool NewWindow)>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.StartListening((paths, newWindow) => received.TrySetResult((paths, newWindow)));
        string[] expected = [@"C:\Notes\筆記 with spaces.md", @"D:\Docs\second.md"];
        Assert.True(await secondary.ForwardAsync(expected, true));
        var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(expected, result.Paths);
        Assert.True(result.NewWindow);
    }

    [Fact]
    public async Task RelativePathsAreResolvedInTheSendingProcess()
    {
        var name = "MarkPad.Tests." + Guid.NewGuid().ToString("N");
        using var primary = new SingleInstanceService(name);
        using var secondary = new SingleInstanceService(name);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.StartListening((paths, _) => received.TrySetResult(paths[0]));
        const string relativePath = "notes/document.md";
        Assert.True(await secondary.ForwardAsync([relativePath], false));
        Assert.Equal(Path.GetFullPath(relativePath), await received.Task.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task ListenerAcceptsSubsequentRequestsIncludingEmptyActivation()
    {
        var name = "MarkPad.Tests." + Guid.NewGuid().ToString("N");
        using var primary = new SingleInstanceService(name);
        using var secondary = new SingleInstanceService(name);
        var count = 0;
        primary.StartListening((_, _) => Interlocked.Increment(ref count));
        Assert.True(await secondary.ForwardAsync([@"C:\one.md"], false));
        Assert.True(await secondary.ForwardAsync([], false));
        Assert.True(await secondary.ForwardAsync([@"C:\two.md"], false));
        Assert.Equal(3, Volatile.Read(ref count));
    }

    [Fact]
    public async Task OversizedAndMalformedRequestsAreRejectedBeforeConnecting()
    {
        using var instance = new SingleInstanceService("MarkPad.Tests." + Guid.NewGuid().ToString("N"));
        Assert.False(await instance.ForwardAsync(Enumerable.Repeat("x", 257).ToArray(), false));
        Assert.False(await instance.ForwardAsync([new string('x', 32768)], false));
        Assert.False(await instance.ForwardAsync(["bad\0path"], false));
        Assert.False(await instance.ForwardAsync(Enumerable.Repeat(new string('x', 32000), 9).ToArray(), false));
    }

    [Fact]
    public void ListenerOwnershipIsEnforcedAndMutexIsReleasedOnDispose()
    {
        var name = "MarkPad.Tests." + Guid.NewGuid().ToString("N");
        using (var primary = new SingleInstanceService(name))
        using (var secondary = new SingleInstanceService(name))
        {
            Assert.Throws<InvalidOperationException>(() => secondary.StartListening((_, _) => { }));
            primary.StartListening((_, _) => { });
            Assert.Throws<InvalidOperationException>(() => primary.StartListening((_, _) => { }));
        }
        using var replacement = new SingleInstanceService(name);
        Assert.True(replacement.IsPrimary);
    }
}
