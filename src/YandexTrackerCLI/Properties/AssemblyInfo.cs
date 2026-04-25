using System.Runtime.CompilerServices;

// Grants the end-to-end CLI test assembly access to internal test-only hooks
// (e.g., TrackerContextFactory.TestInnerHandlerOverride). This is strictly for tests.
[assembly: InternalsVisibleTo("YandexTrackerCLI.Tests")]
