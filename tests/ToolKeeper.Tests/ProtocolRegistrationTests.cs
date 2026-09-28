using Microsoft.Win32;
using ToolKeeper.Services;
using Xunit;

namespace ToolKeeper.Tests;

public sealed class ProtocolRegistrationTests
{
    private const string ProtocolPath = @"Software\Classes\toolkeeper";
    private const string OpenCommandPath = ProtocolPath + @"\shell\open\command";

    [Fact]
    public void NewRegistrationRecordsOwnershipAndTheRestrictedActivationCommand()
    {
        using var fixture = new RegistryFixture();
        var command = ToolKeeperProtocolRegistration.CreateCommand(@"C:\Program Files\ToolKeeper\ToolKeeper.exe", null);

        ToolKeeperProtocolRegistration.Register(fixture.Root, command);

        using var protocol = fixture.Root.OpenSubKey(ProtocolPath)!;
        using var open = fixture.Root.OpenSubKey(OpenCommandPath)!;
        Assert.Equal(ToolKeeperProtocolRegistration.RegistrationOwner,
            protocol.GetValue(ToolKeeperProtocolRegistration.RegistrationOwnerValueName));
        Assert.Equal("URL:ToolKeeper Protocol", protocol.GetValue(null));
        Assert.Equal("", protocol.GetValue("URL Protocol"));
        Assert.Equal("\"C:\\Program Files\\ToolKeeper\\ToolKeeper.exe\" --protocol-activate \"%1\"", open.GetValue(null));
        Assert.Null(open.GetValue("DelegateExecute"));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(null, true)]
    [InlineData("AnotherApp.Protocol.v1", false)]
    [InlineData("AnotherApp.Protocol.v1", true)]
    public void UnownedRegistrationIsPreservedEvenWhenTheCommandMatches(string? owner, bool sameCommand)
    {
        using var fixture = new RegistryFixture();
        var newCommand = ToolKeeperProtocolRegistration.CreateCommand(@"C:\Tools\ToolKeeper.exe", null);
        var originalCommand = sameCommand ? newCommand : "\"C:\\AnotherApp\\Handler.exe\" \"%1\"";
        using (var protocol = fixture.Root.CreateSubKey(ProtocolPath))
        {
            protocol.SetValue(null, "Existing user registration");
            protocol.SetValue("URL Protocol", "");
            protocol.SetValue("CustomMetadata", "Keep this value");
            if (owner is not null) protocol.SetValue(ToolKeeperProtocolRegistration.RegistrationOwnerValueName, owner);
        }
        using (var open = fixture.Root.CreateSubKey(OpenCommandPath))
        {
            open.SetValue(null, originalCommand);
            open.SetValue("DelegateExecute", "{11111111-1111-1111-1111-111111111111}");
        }

        var error = Assert.Throws<InvalidOperationException>(() =>
            ToolKeeperProtocolRegistration.Register(fixture.Root, newCommand));

        Assert.Contains("preserved", error.Message);
        using var preserved = fixture.Root.OpenSubKey(ProtocolPath)!;
        using var preservedCommand = fixture.Root.OpenSubKey(OpenCommandPath)!;
        Assert.Equal("Existing user registration", preserved.GetValue(null));
        Assert.Equal("", preserved.GetValue("URL Protocol"));
        Assert.Equal("Keep this value", preserved.GetValue("CustomMetadata"));
        Assert.Equal(owner, preserved.GetValue(ToolKeeperProtocolRegistration.RegistrationOwnerValueName));
        Assert.Equal(owner is null ? 3 : 4, preserved.GetValueNames().Length);
        Assert.Equal(originalCommand, preservedCommand.GetValue(null));
        Assert.Equal("{11111111-1111-1111-1111-111111111111}", preservedCommand.GetValue("DelegateExecute"));
        Assert.Equal(2, preservedCommand.GetValueNames().Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedRegistrationRemovesObsoleteDelegateEvenWhenCommandIsUnchanged(bool sameCommand)
    {
        using var fixture = new RegistryFixture();
        var command = ToolKeeperProtocolRegistration.CreateCommand(@"C:\Updated tools\ToolKeeper.exe", null);
        using (var protocol = fixture.Root.CreateSubKey(ProtocolPath))
        {
            protocol.SetValue(ToolKeeperProtocolRegistration.RegistrationOwnerValueName,
                ToolKeeperProtocolRegistration.RegistrationOwner);
            protocol.SetValue("URL Protocol", "");
            protocol.SetValue("CustomMetadata", "Preserve unrelated metadata");
        }
        using (var open = fixture.Root.CreateSubKey(OpenCommandPath))
        {
            open.SetValue(null, sameCommand ? command : "\"C:\\Old tools\\ToolKeeper.exe\" --activate \"%1\"");
            open.SetValue("DelegateExecute", "{11111111-1111-1111-1111-111111111111}");
        }

        ToolKeeperProtocolRegistration.Register(fixture.Root, command);
        ToolKeeperProtocolRegistration.Register(fixture.Root, command);

        using var protocolAfter = fixture.Root.OpenSubKey(ProtocolPath)!;
        using var openAfter = fixture.Root.OpenSubKey(OpenCommandPath)!;
        Assert.Equal(command, openAfter.GetValue(null));
        Assert.Null(openAfter.GetValue("DelegateExecute"));
        Assert.Equal(ToolKeeperProtocolRegistration.RegistrationOwner,
            protocolAfter.GetValue(ToolKeeperProtocolRegistration.RegistrationOwnerValueName));
        Assert.Equal("Preserve unrelated metadata", protocolAfter.GetValue("CustomMetadata"));
    }

    [Fact]
    public void OwnedPartialRegistrationCanBeCompletedAfterAnInterruptedWrite()
    {
        using var fixture = new RegistryFixture();
        using (var protocol = fixture.Root.CreateSubKey(ProtocolPath))
            protocol.SetValue(ToolKeeperProtocolRegistration.RegistrationOwnerValueName,
                ToolKeeperProtocolRegistration.RegistrationOwner);
        var command = ToolKeeperProtocolRegistration.CreateCommand(@"C:\Tools\ToolKeeper.exe", null);

        ToolKeeperProtocolRegistration.Register(fixture.Root, command);

        using var open = fixture.Root.OpenSubKey(OpenCommandPath)!;
        Assert.Equal(command, open.GetValue(null));
    }

    private sealed class RegistryFixture : IDisposable
    {
        // Register receives this isolated root, so its Software\Classes path is only simulated.
        // Tests never call TryRegister or access HKCU\Software\Classes\toolkeeper itself.
        private readonly string _path = @"Software\ToolKeeper.Tests\ProtocolRegistration\" + Guid.NewGuid().ToString("N");
        public RegistryKey Root { get; }

        public RegistryFixture()
        {
            Root = Registry.CurrentUser.CreateSubKey(_path, writable: true)
                ?? throw new InvalidOperationException("Cannot create an isolated registration test key.");
        }

        public void Dispose()
        {
            Root.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
        }
    }
}
