namespace YandexTrackerCLI.Auth.Federated;

using System.Net;
using System.Net.Sockets;
using System.Text;

/// <summary>
/// Результат единственного callback-запроса от браузера после редиректа
/// с authorization endpoint.
/// </summary>
/// <param name="Code">Значение параметра <c>code</c> (authorization code), если пришёл.</param>
/// <param name="State">Значение <c>state</c> — для проверки совпадения с изначальным.</param>
/// <param name="Error">Значение <c>error</c>, если провайдер вернул ошибку.</param>
public sealed record CallbackResult(string? Code, string? State, string? Error);

/// <summary>
/// Локальный одноразовый HTTP listener, слушающий <c>127.0.0.1</c> на
/// динамическом порту. Используется как <c>redirect_uri</c> для PKCE-flow:
/// принимает первый GET-запрос, парсит query-string и отвечает статической
/// HTML-страницей "authentication complete".
/// </summary>
/// <remarks>
/// Намеренно минималистичная реализация на <see cref="TcpListener"/>, чтобы
/// не тянуть зависимость от <c>HttpListener</c> и не требовать прав на
/// регистрацию HTTP.sys префиксов. Не поддерживает keep-alive, chunking,
/// больших заголовков — ровно одна связка Request/Response.
/// </remarks>
public sealed class LocalCallbackServer : IAsyncDisposable
{
    private readonly TcpListener _listener;

    /// <summary>
    /// Порт, выделенный ОС при старте (используется для сборки redirect_uri).
    /// </summary>
    public int Port { get; }

    private LocalCallbackServer(TcpListener listener, int port)
    {
        _listener = listener;
        Port = port;
    }

    /// <summary>
    /// Запускает listener на <c>127.0.0.1</c> со свободным портом.
    /// </summary>
    /// <returns>Запущенный <see cref="LocalCallbackServer"/>; вызывающий обязан <see cref="DisposeAsync"/>.</returns>
    public static LocalCallbackServer Start()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return new LocalCallbackServer(listener, port);
    }

    /// <summary>
    /// Ждёт единственный GET callback, парсит query, возвращает 200 OK с HTML.
    /// </summary>
    /// <param name="timeout">Жёсткий таймаут ожидания callback.</param>
    /// <param name="ct">Внешний cancellation token (например, из CLI).</param>
    /// <returns>Распарсенный <see cref="CallbackResult"/> с <c>code/state/error</c>.</returns>
    /// <exception cref="OperationCanceledException">Если истёк <paramref name="timeout"/> или внешний <paramref name="ct"/>.</exception>
    public async Task<CallbackResult> AwaitCallbackAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);

        using var client = await _listener.AcceptTcpClientAsync(linked.Token);
        using var stream = client.GetStream();

        // Читаем первую строку HTTP-запроса ("GET /auth/callback?... HTTP/1.1")
        var buffer = new byte[4096];
        var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), linked.Token);
        var request = Encoding.ASCII.GetString(buffer, 0, read);
        var firstLine = request.Split("\r\n", 2)[0];
        var parts = firstLine.Split(' ');
        var path = parts.Length >= 2 ? parts[1] : string.Empty;

        string? code = null, state = null, error = null;
        var qIdx = path.IndexOf('?');
        if (qIdx > 0)
        {
            var query = path[(qIdx + 1)..];
            foreach (var kv in query.Split('&'))
            {
                var eq = kv.IndexOf('=');
                if (eq < 0)
                {
                    continue;
                }

                var k = Uri.UnescapeDataString(kv[..eq]);
                var v = Uri.UnescapeDataString(kv[(eq + 1)..]);
                switch (k)
                {
                    case "code":  code = v;  break;
                    case "state": state = v; break;
                    case "error": error = v; break;
                }
            }
        }

        // Ответ — фиксированная HTML страница.
        var body = "<html><body><h1>Authentication complete</h1><p>You can close this tab.</p></body></html>";
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, linked.Token);
        await stream.WriteAsync(bodyBytes, linked.Token);

        return new CallbackResult(code, state, error);
    }

    /// <summary>
    /// Останавливает listener.
    /// </summary>
    /// <returns>Завершённая <see cref="ValueTask"/>.</returns>
    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return ValueTask.CompletedTask;
    }
}
