using Xunit;

namespace RoAura.Tests;

// These tests deliberately open real HWNDs and assert keyboard focus. Another
// collection opening a window can steal that focus even on a different STA.
[CollectionDefinition("WPF UI", DisableParallelization = true)]
public sealed class WpfUiCollection
{
}
