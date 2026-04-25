namespace YandexTrackerCLI.Output;

using System.Diagnostics;
using System.Text;

/// <summary>
/// <see cref="TextWriter"/>-обёртка над процессом-pager'ом (например <c>less -R -F -X</c>):
/// перенаправляет всё, что в неё пишется, в <see cref="Process.StandardInput"/>.
/// При <see cref="IDisposable.Dispose"/> закрывает stdin pager'а и дожидается его выхода.
/// </summary>
/// <remarks>
/// Если <see cref="TerminalCapabilities.UsePager"/> равно <c>false</c>, метод
/// <see cref="Create"/> возвращает <paramref name="fallback"/> напрямую, без обёртки.
/// При сбое запуска pager-процесса (например, на Windows нет <c>less</c>) метод
/// тоже падает gracefully — пишет одно warning в stderr и возвращает fallback.
/// </remarks>
public sealed class PagerWriter : TextWriter
{
    private readonly Process? _process;
    private readonly StreamWriter? _processStdin;
    private readonly TextWriter _fallback;
    private bool _disposed;

    private PagerWriter(Process? process, StreamWriter? processStdin, TextWriter fallback)
    {
        _process = process;
        _processStdin = processStdin;
        _fallback = fallback;
    }

    /// <summary>
    /// Кодировка целевого writer — наследуется от fallback.
    /// </summary>
    public override Encoding Encoding => _fallback.Encoding;

    /// <summary>
    /// Создаёт writer над pager-процессом или возвращает <paramref name="fallback"/>
    /// напрямую, если pager выключен или не удалось запустить.
    /// </summary>
    /// <param name="caps">Резолвленные возможности терминала.</param>
    /// <param name="fallback">Writer-получатель, если pager не используется
    /// (обычно <see cref="Console.Out"/>).</param>
    /// <returns>Готовый <see cref="TextWriter"/> для записи. Caller должен вызвать
    /// <see cref="IDisposable.Dispose"/>.</returns>
    public static TextWriter Create(TerminalCapabilities caps, TextWriter fallback)
    {
        if (!caps.UsePager)
        {
            return new NonOwningWrapper(fallback);
        }

        var (file, args) = ParseCommand(caps.PagerCommand);

        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = false,
            };
            var proc = Process.Start(psi);
            if (proc is null)
            {
                fallback.WriteLine("warning: failed to start pager '" + caps.PagerCommand + "', falling back to direct output.");
                return new NonOwningWrapper(fallback);
            }
            var sw = new StreamWriter(proc.StandardInput.BaseStream, fallback.Encoding)
            {
                AutoFlush = false,
            };
            return new PagerWriter(proc, sw, fallback);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                   || ex is FileNotFoundException
                                   || ex is InvalidOperationException
                                   || ex is IOException)
        {
            fallback.WriteLine("warning: failed to start pager '" + caps.PagerCommand + "': " + ex.Message);
            return new NonOwningWrapper(fallback);
        }
    }

    /// <inheritdoc/>
    public override void Write(char value)
    {
        if (_processStdin is not null)
        {
            try
            {
                _processStdin.Write(value);
            }
            catch (IOException)
            {
                // Pager exited (e.g., user pressed q) — silently swallow.
            }
        }
        else
        {
            _fallback.Write(value);
        }
    }

    /// <inheritdoc/>
    public override void Write(string? value)
    {
        if (value is null)
        {
            return;
        }
        if (_processStdin is not null)
        {
            try
            {
                _processStdin.Write(value);
            }
            catch (IOException)
            {
                // Pager exited.
            }
        }
        else
        {
            _fallback.Write(value);
        }
    }

    /// <inheritdoc/>
    public override void Flush()
    {
        if (_processStdin is not null)
        {
            try
            {
                _processStdin.Flush();
            }
            catch (IOException)
            {
                // Pager exited.
            }
        }
        else
        {
            _fallback.Flush();
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (disposing && _processStdin is not null && _process is not null)
        {
            try
            {
                _processStdin.Flush();
            }
            catch (IOException)
            {
                // ignore
            }
            try
            {
                _processStdin.Close();
            }
            catch (IOException)
            {
                // ignore
            }
            try
            {
                _process.WaitForExit(30_000);
            }
            catch (InvalidOperationException)
            {
                // process already disposed/exited
            }
            try
            {
                _process.Dispose();
            }
            catch
            {
                // best-effort
            }
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Простейший shell-style парсер для pager-команды: split по пробелам, без поддержки
    /// shell-quoting/expansion. Этого достаточно для типичных значений (<c>less -R -F -X</c>,
    /// <c>more</c>, <c>moar</c>, <c>most</c>).
    /// </summary>
    private static (string FileName, string Arguments) ParseCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return ("less", "-R -F -X");
        }
        var trimmed = command.Trim();
        var spaceIdx = trimmed.IndexOf(' ');
        if (spaceIdx < 0)
        {
            return (trimmed, string.Empty);
        }
        return (trimmed.Substring(0, spaceIdx), trimmed.Substring(spaceIdx + 1));
    }

    /// <summary>
    /// Lightweight wrapper над уже-существующим <see cref="TextWriter"/>: делегирует
    /// все записи и НЕ закрывает целевой writer (например <see cref="Console.Out"/>)
    /// при <see cref="IDisposable.Dispose"/>. Используется как fallback, когда pager
    /// не запускается, чтобы caller не различал «pager on/off» и одинаково вызывал
    /// using-блок.
    /// </summary>
    private sealed class NonOwningWrapper : TextWriter
    {
        private readonly TextWriter _inner;

        public NonOwningWrapper(TextWriter inner)
        {
            _inner = inner;
        }

        public override Encoding Encoding => _inner.Encoding;

        public override void Write(char value) => _inner.Write(value);

        public override void Write(string? value) => _inner.Write(value);

        public override void WriteLine() => _inner.WriteLine();

        public override void WriteLine(string? value) => _inner.WriteLine(value);

        public override void Flush() => _inner.Flush();
    }
}
