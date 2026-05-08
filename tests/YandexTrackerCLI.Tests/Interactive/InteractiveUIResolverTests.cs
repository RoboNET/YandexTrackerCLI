namespace YandexTrackerCLI.Tests.Interactive;

using TUnit.Core;
using YandexTrackerCLI.Interactive;
using YandexTrackerCLI.Output;

/// <summary>
/// Тесты <see cref="InteractiveUIResolver"/>: формат и состояние redirect должны
/// корректно резолвить Noop vs Spectre. Помечены <c>[NotInParallel]</c> т.к.
/// мутируют AsyncLocal-делегаты, общие с другими тестами CLI.
/// </summary>
[NotInParallel("yt-cli-global-state")]
public sealed class InteractiveUIResolverTests
{
    /// <summary>JSON всегда даёт Noop, независимо от TTY.</summary>
    [Test]
    public async Task Resolve_JsonFormat_ReturnsNoop()
    {
        ForceTty();
        try
        {
            var ui = InteractiveUIResolver.Resolve(OutputFormat.Json);
            await Assert.That(ui.IsRich).IsFalse();
            await Assert.That(ui).IsTypeOf<NoopInteractiveUI>();
        }
        finally
        {
            ResetRedirectOverrides();
        }
    }

    /// <summary>Minimal всегда даёт Noop, независимо от TTY.</summary>
    [Test]
    public async Task Resolve_MinimalFormat_ReturnsNoop()
    {
        ForceTty();
        try
        {
            var ui = InteractiveUIResolver.Resolve(OutputFormat.Minimal);
            await Assert.That(ui.IsRich).IsFalse();
        }
        finally
        {
            ResetRedirectOverrides();
        }
    }

    /// <summary>Table-формат при перенаправленном stdout даёт Noop.</summary>
    [Test]
    public async Task Resolve_TableFormat_OutputRedirected_ReturnsNoop()
    {
        InteractiveUIResolver.TestIsOutputRedirected.Value = () => true;
        InteractiveUIResolver.TestIsErrorRedirected.Value = () => false;
        try
        {
            var ui = InteractiveUIResolver.Resolve(OutputFormat.Table);
            await Assert.That(ui.IsRich).IsFalse();
        }
        finally
        {
            ResetRedirectOverrides();
        }
    }

    /// <summary>Table-формат при TTY даёт Spectre.</summary>
    [Test]
    public async Task Resolve_TableFormat_TTY_ReturnsSpectre()
    {
        ForceTty();
        try
        {
            var ui = InteractiveUIResolver.Resolve(OutputFormat.Table);
            await Assert.That(ui.IsRich).IsTrue();
            await Assert.That(ui).IsTypeOf<SpectreInteractiveUI>();
        }
        finally
        {
            ResetRedirectOverrides();
        }
    }

    /// <summary><see cref="InteractiveUIResolver.TestOverride"/> побеждает любой формат и redirect.</summary>
    [Test]
    public async Task TestOverride_AlwaysUsedWhenSet()
    {
        var fake = new FakeInteractiveUI();
        InteractiveUIResolver.TestOverride.Value = fake;
        InteractiveUIResolver.TestIsOutputRedirected.Value = () => true;
        try
        {
            // Даже Json + redirect — override берёт верх.
            var ui = InteractiveUIResolver.Resolve(OutputFormat.Json);
            await Assert.That(ui).IsSameReferenceAs(fake);

            ui = InteractiveUIResolver.Resolve(OutputFormat.Table);
            await Assert.That(ui).IsSameReferenceAs(fake);
        }
        finally
        {
            InteractiveUIResolver.TestOverride.Value = null;
            ResetRedirectOverrides();
        }
    }

    private static void ForceTty()
    {
        InteractiveUIResolver.TestIsOutputRedirected.Value = () => false;
        InteractiveUIResolver.TestIsErrorRedirected.Value = () => false;
    }

    private static void ResetRedirectOverrides()
    {
        InteractiveUIResolver.TestIsOutputRedirected.Value = null;
        InteractiveUIResolver.TestIsErrorRedirected.Value = null;
    }

    private sealed class FakeInteractiveUI : IInteractiveUI
    {
        public bool IsRich => false;

        public Task<T> Status<T>(string label, Func<IStatusContext, Task<T>> work, CancellationToken ct = default)
            => work(new NoopCtx());

        public Task Status(string label, Func<IStatusContext, Task> work, CancellationToken ct = default)
            => work(new NoopCtx());

        private sealed class NoopCtx : IStatusContext
        {
            public void Update(string label) { }
            public void Spinner(SpinnerStyle style) { }
        }
    }
}
