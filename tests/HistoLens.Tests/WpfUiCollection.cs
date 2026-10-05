using Xunit;

namespace HistoLens.Tests;

// WPF property descriptors share process-wide state across otherwise independent STA dispatchers.
[CollectionDefinition("HistoLens WPF UI")]
public sealed class WpfUiCollection;
