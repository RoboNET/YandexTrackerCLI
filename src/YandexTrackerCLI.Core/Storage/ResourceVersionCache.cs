namespace YandexTrackerCLI.Core.Storage;

using System.Text.Json;
using System.Text.Json.Serialization;
using Config;
using Json;

/// <summary>
/// Запомненная версия ресурса и момент, когда её прочитали.
/// </summary>
/// <param name="Version">Версия ресурса, как её отдал API в корневом поле <c>version</c>.</param>
/// <param name="ReadAt">Момент чтения в UTC — из него считается возраст записи в сообщении о конфликте.</param>
public sealed record ResourceVersionEntry(
    [property: JsonPropertyName("version")] long Version,
    [property: JsonPropertyName("read_at")] DateTimeOffset ReadAt);

/// <summary>
/// Файловый кэш версий ресурсов для оптимистической блокировки: команда <c>get</c>
/// запоминает версию прочитанного ресурса, следующая мутирующая команда подставляет её
/// в <c>?version=N</c>.
/// </summary>
/// <remarks>
/// <para>
/// Лежит рядом с <see cref="FileStore"/>, а не в <c>Auth/</c>, потому что к аутентификации
/// отношения не имеет: это второе файловое хранилище на той же механике (атомарная запись
/// плюс кросс-процессный лок), и естественное для него место — каталог самой механики.
/// </para>
/// <para>
/// Семантика намеренно не «CLI сам перечитает ресурс перед записью»: версия берётся из
/// момента, когда ресурс прочитал <b>пользователь</b>. Чтение непосредственно перед записью
/// закрывало бы окно гонки шириной в миллисекунды и давало ложное чувство защиты — чужую
/// правку, сделанную между чтением пользователя и его записью, оно всё равно затирает.
/// </para>
/// <para>
/// Кэш вспомогательный: промах, битый файл и отказ записи не должны валить команду.
/// Ответственность за это лежит на вызывающем коде — сам класс отличает «записи нет» от
/// «файл нечитаем» только тем, что в обоих случаях возвращает <c>null</c>.
/// </para>
/// </remarks>
public sealed class ResourceVersionCache
{
    /// <summary>
    /// Срок хранения записи. По его истечении запись выбрасывается при ближайшей записи в кэш.
    /// </summary>
    /// <remarks>
    /// Это <b>уборка мусора, а не TTL</b>: пока запись жива, она используется независимо от
    /// возраста. Отказаться от блокировки из-за того, что версию прочитали давно, было бы
    /// ровно наоборот тому, зачем блокировка нужна, — чем старше прочитанная версия, тем
    /// вероятнее, что ресурс с тех пор изменили, и тем важнее получить конфликт, а не тихо
    /// затереть чужую правку. Срок ограничивает только рост файла.
    /// </remarks>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    /// <summary>
    /// Время по умолчанию, которое <see cref="Set"/> ждёт кросс-процессный лок, прежде чем сдаться.
    /// </summary>
    public static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(10);

    private readonly string _path;
    private readonly TimeSpan _lockTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Создаёт кэш, привязанный к указанному файлу.
    /// </summary>
    /// <param name="path">Абсолютный путь к файлу кэша.</param>
    /// <param name="lockTimeout">
    /// Сколько <see cref="Set"/> ждёт лок; <c>null</c> — <see cref="DefaultLockTimeout"/>.
    /// </param>
    public ResourceVersionCache(string path, TimeSpan? lockTimeout = null)
    {
        _path = path;
        _lockTimeout = lockTimeout ?? DefaultLockTimeout;
    }

    private string LockPath => _path + ".lock";

    /// <summary>
    /// Путь к файлу кэша по умолчанию: <c>XDG_CACHE_HOME</c> (либо <c>$HOME/.cache</c>)
    /// плюс <c>yandex-tracker/resource-versions.json</c>.
    /// </summary>
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
                ?? Path.Combine(PathResolver.ResolveHome(), ".cache"),
            "yandex-tracker",
            "resource-versions.json");

    /// <summary>
    /// Собирает ключ записи из организации, профиля, типа ресурса и его идентификатора.
    /// </summary>
    /// <param name="profile">
    /// Действующий профиль. В ключ идут и его имя, и организация, потому что одного имени
    /// мало: <c>YT_ORG_TYPE</c>/<c>YT_ORG_ID</c> перекрывают организацию профиля, а
    /// <c>YT_CONFIG_PATH</c> подменяет конфиг целиком, — поэтому «default» в двух запусках
    /// может означать разные организации. Без организации в ключе версия, прочитанная в
    /// одной из них, подставилась бы в мутацию в другой: либо ложный конфликт, либо, если
    /// номера версий случайно совпали, незащищённая запись. Имя профиля при этом остаётся:
    /// в пределах одной организации оно разделяет записи разных креденшелов, и разводить
    /// их дешевле, чем доказывать, что смешивать безопасно.
    /// </param>
    /// <param name="resourceType">Дискриминатор типа ресурса (<c>issue</c>, <c>trigger</c>, <c>autoaction</c>).</param>
    /// <param name="resourceId">
    /// Идентификатор ресурса в пределах типа, уже приведённый к каноническому виду.
    /// Регистр здесь не трогается: правило зависит от типа ресурса и известно вызывающему
    /// (см. <c>ResourceVersionFlow.NormalizeResourceId</c>), а угадать его тут нельзя.
    /// </param>
    /// <returns>Ключ для <see cref="Get"/> и <see cref="Set"/>.</returns>
    /// <remarks>
    /// <para>
    /// Разделитель — U+001F (unit separator): он не встречается ни в именах профилей и
    /// идентификаторах организаций, ни в ключах задач и идентификаторах ресурсов, поэтому
    /// склеить два разных ключа в один нельзя.
    /// </para>
    /// <para>
    /// Идентификатор организации кладётся как есть: это непрозрачный идентификатор
    /// (числовой у Яндекс 360, строковый у Cloud), и приводить его регистр не к чему —
    /// API различает организации точным совпадением.
    /// </para>
    /// </remarks>
    public static string BuildKey(EffectiveProfile profile, string resourceType, string resourceId) =>
        $"{profile.OrgType.ToString()}\u001f{profile.OrgId}\u001f{profile.Name}"
        + $"\u001f{resourceType}\u001f{resourceId}";

    /// <summary>
    /// Возвращает запомненную версию ресурса.
    /// </summary>
    /// <param name="key">Ключ, собранный <see cref="BuildKey"/>.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Запись либо <c>null</c>, если её нет или файл кэша нечитаем.</returns>
    /// <remarks>
    /// Нечитаемый файл (битый JSON, нет прав, чужой формат) трактуется как промах:
    /// оптимистическая блокировка — не то, ради чего стоит валить команду пользователя.
    /// </remarks>
    public async Task<ResourceVersionEntry?> Get(string key, CancellationToken ct = default)
    {
        var all = await TryLoad(ct);
        return all is not null && all.TryGetValue(key, out var entry) ? entry : null;
    }

    /// <summary>
    /// Запоминает версию ресурса, попутно выбрасывая записи старше <see cref="Retention"/>.
    /// </summary>
    /// <param name="key">Ключ, собранный <see cref="BuildKey"/>.</param>
    /// <param name="version">Версия ресурса.</param>
    /// <param name="readAt">Момент чтения; <c>null</c> — текущее время UTC.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <exception cref="Api.Errors.TrackerException">
    /// <see cref="Api.Errors.ErrorCode.ConfigError"/> — лок не взят или файл не записался.
    /// Вызывающий обязан решить, эскалировать это или проглотить: сам по себе отказ записи
    /// в кэш результат команды не портит.
    /// </exception>
    /// <remarks>
    /// Read-modify-write сериализован дважды — внутрипроцессным семафором и рекомендательным
    /// файловым локом, покрывающим параллельные процессы <c>yt</c>. Параллельные запуски здесь
    /// реальны: агенты и скрипты гоняют несколько команд разом. Сам коммит атомарен (уникальный
    /// временный файл плюс переименование), поэтому проигравший писатель может потерять свою
    /// запись, но не обрезать чужой файл.
    /// </remarks>
    public async Task Set(string key, long version, DateTimeOffset? readAt = null, CancellationToken ct = default)
    {
        var now = readAt ?? DateTimeOffset.UtcNow;
        await _gate.WaitAsync(ct);
        try
        {
            await using var lockHandle = await FileStore.AcquireLock(LockPath, _lockTimeout, ct);

            var all = await TryLoad(ct) ?? new Dictionary<string, ResourceVersionEntry>();
            all[key] = new ResourceVersionEntry(version, now.ToUniversalTime());
            Prune(all, now);
            await Save(all, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Выбрасывает из набора записи, прочитанные раньше, чем <paramref name="now"/> минус
    /// <see cref="Retention"/>.
    /// </summary>
    /// <param name="all">Набор записей; изменяется на месте.</param>
    /// <param name="now">Момент, относительно которого считается возраст.</param>
    /// <remarks>
    /// Записи «из будущего» (сдвинутые часы, правка файла руками) не трогаются: угадывать,
    /// что с ними не так, дороже, чем хранить лишнюю строку.
    /// </remarks>
    private static void Prune(Dictionary<string, ResourceVersionEntry> all, DateTimeOffset now)
    {
        var cutoff = now - Retention;
        foreach (var stale in all.Where(kv => kv.Value.ReadAt < cutoff).Select(kv => kv.Key).ToList())
        {
            all.Remove(stale);
        }
    }

    /// <summary>
    /// Читает файл кэша, возвращая <c>null</c> при любой ошибке чтения или разбора.
    /// </summary>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Записи кэша либо <c>null</c>, если файла нет или он нечитаем.</returns>
    private async Task<Dictionary<string, ResourceVersionEntry>?> TryLoad(CancellationToken ct)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            await using var fs = FileStore.OpenSharedRead(_path);
            return await JsonSerializer.DeserializeAsync(
                fs, TrackerJsonContext.Default.DictionaryStringResourceVersionEntry, ct);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Битый или недоступный файл кэша — это промах, а не отказ команды.
            return null;
        }
    }

    private Task Save(Dictionary<string, ResourceVersionEntry> all, CancellationToken ct) =>
        FileStore.WriteAtomic(
            _path,
            (stream, token) => JsonSerializer.SerializeAsync(
                stream,
                all,
                TrackerJsonContext.Default.DictionaryStringResourceVersionEntry,
                token),
            ct);
}
