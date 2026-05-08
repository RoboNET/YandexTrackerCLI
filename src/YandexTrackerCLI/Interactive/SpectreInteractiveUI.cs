namespace YandexTrackerCLI.Interactive;

using Spectre.Console;

/// <summary>
/// Реализация <see cref="IInteractiveUI"/> поверх Spectre.Console. Все декорации
/// (спиннер, label) пишутся в <c>stderr</c>, чтобы не мешать bit-exact выводу
/// на <c>stdout</c>.
/// </summary>
public sealed class SpectreInteractiveUI : IInteractiveUI
{
    private readonly IAnsiConsole _ansi;

    /// <summary>
    /// Создаёт UI поверх явного <see cref="IAnsiConsole"/>. Полезно для тестов.
    /// </summary>
    /// <param name="ansi">Spectre.Console-консоль; ожидается, что её writer указывает в stderr.</param>
    public SpectreInteractiveUI(IAnsiConsole ansi)
    {
        ArgumentNullException.ThrowIfNull(ansi);
        _ansi = ansi;
    }

    /// <summary>
    /// Создаёт UI с дефолтной stderr-консолью (Spectre auto-detect для colors/unicode).
    /// </summary>
    /// <returns>Новый <see cref="SpectreInteractiveUI"/>.</returns>
    public static SpectreInteractiveUI CreateForStderr()
    {
        var ansi = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(Console.Error),
        });
        return new SpectreInteractiveUI(ansi);
    }

    /// <inheritdoc />
    public bool IsRich => true;

    /// <inheritdoc />
    public Task<T> Status<T>(string label, Func<IStatusContext, Task<T>> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return _ansi.Status().StartAsync(label, async ctx =>
        {
            var wrapper = new SpectreStatusContext(ctx);
            return await work(wrapper).ConfigureAwait(false);
        });
    }

    /// <inheritdoc />
    public Task Status(string label, Func<IStatusContext, Task> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return _ansi.Status().StartAsync(label, async ctx =>
        {
            var wrapper = new SpectreStatusContext(ctx);
            await work(wrapper).ConfigureAwait(false);
        });
    }

    private sealed class SpectreStatusContext : IStatusContext
    {
        private readonly StatusContext _ctx;

        public SpectreStatusContext(StatusContext ctx)
        {
            _ctx = ctx;
        }

        public void Update(string label)
        {
            _ctx.Status = label ?? string.Empty;
        }

        public void Spinner(SpinnerStyle style)
        {
            _ctx.Spinner = style switch
            {
                SpinnerStyle.Dots => Spectre.Console.Spinner.Known.Dots,
                SpinnerStyle.Star => Spectre.Console.Spinner.Known.Star,
                SpinnerStyle.Line => Spectre.Console.Spinner.Known.Line,
                SpinnerStyle.Clock => Spectre.Console.Spinner.Known.Clock,
                _ => Spectre.Console.Spinner.Known.Default,
            };
        }
    }
}
