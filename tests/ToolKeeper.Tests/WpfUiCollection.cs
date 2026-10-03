using Xunit;

namespace ToolKeeper.Tests;

// WPF property descriptors share process-wide state even when windows use separate STA threads.
[CollectionDefinition("WPF UI")]
public sealed class WpfUiCollection;
