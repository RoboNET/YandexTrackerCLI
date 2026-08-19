namespace YandexTrackerCLI.Commands;

using System.Globalization;
using System.Text.Json;
using Core.Api.Errors;
using Core.Storage;

/// <summary>
/// Откуда взялась версия, отправленная в <c>?version=N</c>.
/// </summary>
public enum ResourceVersionSource
{
    /// <summary>Версии нет: PATCH уходит без проверки.</summary>
    None,

    /// <summary>Явный флаг <c>--version</c>.</summary>
    Explicit,

    /// <summary>Флаг <c>--overwrite-latest</c>: версию только что перечитали у API.</summary>
    OverwriteLatest,

    /// <summary>Корневое поле <c>version</c> в теле запроса.</summary>
    Body,

    /// <summary>Кэш версий: ресурс читали командой <c>get</c>.</summary>
    Cache,
}

/// <summary>
/// Решение о версии для одного PATCH: само значение, источник и (для кэша) момент чтения.
/// </summary>
/// <param name="Version">Версия либо <c>null</c>, если отправляем без неё.</param>
/// <param name="Source">Источник значения.</param>
/// <param name="ReadAt">Момент чтения ресурса — заполнен только для <see cref="ResourceVersionSource.Cache"/>.</param>
public sealed record ResourceVersionDecision(long? Version, ResourceVersionSource Source, DateTimeOffset? ReadAt);

/// <summary>
/// Связывает кэш версий (<see cref="ResourceVersionCache"/>) с командами CLI: запоминает
/// версию на чтении, выбирает её на записи и объясняет конфликт версий человеческим языком.
/// </summary>
/// <remarks>
/// <para>
/// Набор дискриминаторов ресурсов открыт: чтобы завести кэш версий для нового типа,
/// достаточно добавить сюда константу и позвать <see cref="Remember"/> с <see cref="Resolve"/>
/// в его командах. Компоненты сознательно не заведены: поддержка <c>?version=</c> у них
/// не проверена живым API.
/// </para>
/// <para>
/// Приоритет источников: явный <c>--version</c> → <c>--overwrite-latest</c> → корневое поле
/// <c>version</c> в теле → кэш → без версии. Промах кэша означает PATCH без версии, то есть
/// прежнее поведение: скрипт, который зовёт <c>update</c> без предшествующего <c>get</c>,
/// ломаться не должен.
/// </para>
/// <para>
/// Кэш вспомогательный: любая его ошибка — и на чтении, и на записи — гасится здесь и
/// наружу не выходит. В stderr она тоже не пишется: туда команды кладут ровно один
/// JSON-объект ошибки, и посторонняя строка сломала бы потребителей, которые его разбирают,
/// ничего не сообщив по существу — результат самой команды от неудачной записи в кэш не меняется.
/// </para>
/// </remarks>
public static class ResourceVersionFlow
{
    /// <summary>Дискриминатор задач в ключе кэша.</summary>
    public const string IssueResource = "issue";

    /// <summary>Дискриминатор триггеров в ключе кэша.</summary>
    public const string TriggerResource = "trigger";

    /// <summary>Дискриминатор автодействий в ключе кэша.</summary>
    public const string AutoactionResource = "autoaction";

    private const string VersionProperty = "version";

    /// <summary>
    /// Проверяет, что <c>--version</c> и <c>--overwrite-latest</c> не заданы вместе.
    /// </summary>
    /// <param name="explicitVersion">Значение <c>--version</c>.</param>
    /// <param name="overwriteLatest">Значение <c>--overwrite-latest</c>.</param>
    /// <exception cref="TrackerException">
    /// <see cref="ErrorCode.InvalidArgs"/> — заданы оба флага.
    /// </exception>
    /// <remarks>
    /// Флаги противоречат друг другу по смыслу: один требует записать поверх названной
    /// версии, другой — поверх той, что сейчас на сервере. Выбрать за пользователя нельзя.
    /// </remarks>
    public static void EnsureFlagsCompatible(long? explicitVersion, bool overwriteLatest)
    {
        if (explicitVersion.HasValue && overwriteLatest)
        {
            throw new TrackerException(ErrorCode.InvalidArgs,
                "--version and --overwrite-latest are mutually exclusive.");
        }
    }

    /// <summary>
    /// Выбирает версию для PATCH по приоритету источников.
    /// </summary>
    /// <param name="ctx">Контекст выполнения (профиль и HTTP-клиент).</param>
    /// <param name="resourceType">Дискриминатор типа ресурса.</param>
    /// <param name="resourceId">Идентификатор ресурса.</param>
    /// <param name="getPath">Путь GET-запроса для <c>--overwrite-latest</c>.</param>
    /// <param name="explicitVersion">Значение <c>--version</c>.</param>
    /// <param name="bodyVersion">Версия, вырезанная из тела запроса.</param>
    /// <param name="noVersionCheck">Значение <c>--no-version-check</c>.</param>
    /// <param name="overwriteLatest">Значение <c>--overwrite-latest</c>.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Решение о версии.</returns>
    /// <exception cref="TrackerException">
    /// <para>
    /// Пробрасывает отказ GET-запроса при <c>--overwrite-latest</c>: не сумев прочитать
    /// текущую версию, тихо отправить PATCH без неё нельзя — пользователь просил перезапись
    /// поверх конкретного состояния, а не вслепую.
    /// </para>
    /// <para>
    /// <see cref="ErrorCode.Unexpected"/> — GET при <c>--overwrite-latest</c> прошёл, но
    /// версии в ответе нет. Случай тот же по сути: отправить мутацию без версии значило бы
    /// подменить запрошенную перезапись поверх известного состояния записью вслепую, которая
    /// затрёт правку, сделанную между GET и PATCH.
    /// </para>
    /// </exception>
    public static async Task<ResourceVersionDecision> Resolve(
        TrackerContext ctx,
        string resourceType,
        string resourceId,
        string getPath,
        long? explicitVersion,
        long? bodyVersion,
        bool noVersionCheck,
        bool overwriteLatest,
        CancellationToken ct)
    {
        if (explicitVersion.HasValue)
        {
            return new ResourceVersionDecision(explicitVersion, ResourceVersionSource.Explicit, null);
        }

        if (overwriteLatest)
        {
            var current = await ctx.Client.GetAsync(getPath, ct);
            if (ReadVersion(current) is not { } latest)
            {
                throw new TrackerException(
                    ErrorCode.Unexpected,
                    "--overwrite-latest re-read the resource, but its response carries no root `version` "
                    + "field, so there is no current version to overwrite. Sending the change without a "
                    + "version would write blindly — exactly what the flag asks not to do. Retry, or pass "
                    + "--version <n> if you know which version you are overwriting.");
            }

            return new ResourceVersionDecision(latest, ResourceVersionSource.OverwriteLatest, null);
        }

        if (bodyVersion.HasValue)
        {
            return new ResourceVersionDecision(bodyVersion, ResourceVersionSource.Body, null);
        }

        if (noVersionCheck)
        {
            return new ResourceVersionDecision(null, ResourceVersionSource.None, null);
        }

        var entry = await TryRecall(ctx, resourceType, resourceId, ct);
        return entry is null
            ? new ResourceVersionDecision(null, ResourceVersionSource.None, null)
            : new ResourceVersionDecision(entry.Version, ResourceVersionSource.Cache, entry.ReadAt);
    }

    /// <summary>
    /// Запоминает версию ресурса из ответа API, если в корне ответа есть числовое поле <c>version</c>.
    /// </summary>
    /// <param name="ctx">Контекст выполнения (нужен профиль).</param>
    /// <param name="resourceType">Дискриминатор типа ресурса.</param>
    /// <param name="resourceId">Идентификатор ресурса.</param>
    /// <param name="payload">Ответ API (тело <c>get</c> либо успешного <c>PATCH</c>).</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Задача, завершающаяся после попытки записи.</returns>
    /// <remarks>
    /// Ответ успешного PATCH запоминается наравне с <c>get</c>: без этого два <c>update</c>
    /// подряд упёрлись бы в конфликт на второй же команде.
    /// </remarks>
    public static async Task Remember(
        TrackerContext ctx,
        string resourceType,
        string resourceId,
        JsonElement payload,
        CancellationToken ct = default)
    {
        if (ReadVersion(payload) is not { } version)
        {
            return;
        }

        try
        {
            var cache = new ResourceVersionCache(ResourceVersionCache.DefaultPath);
            await cache.Set(BuildKey(ctx, resourceType, resourceId), version, readAt: null, ct);
        }
        catch (TrackerException)
        {
            // Кэш не записался (нет прав, занят лок, кончилось место). Команда уже сделала
            // своё дело — валить её из-за вспомогательного хранилища незачем.
        }
    }

    /// <summary>
    /// Дополняет ошибку конфликта версий контекстом: какая версия ушла, когда её прочитали
    /// и какой командой перечитать ресурс.
    /// </summary>
    /// <param name="ex">Исходная ошибка от API.</param>
    /// <param name="decision">Решение о версии, с которым уходил запрос.</param>
    /// <param name="rereadCommand">Команда CLI, перечитывающая ресурс (например <c>yt issue get TECH-1</c>).</param>
    /// <param name="now">Момент, относительно которого считается возраст записи; <c>null</c> — текущее время.</param>
    /// <returns>
    /// Обогащённая ошибка, если конфликт случился с версией из кэша; иначе — исходная,
    /// без изменений.
    /// </returns>
    /// <remarks>
    /// Объясняется только конфликт по версии из кэша: её пользователь не называл, поэтому
    /// без пояснения непонятно, откуда вообще взялась проверка. Явный <c>--version</c> и
    /// версия из тела пользователю известны, и пересказывать их ему нечего.
    /// </remarks>
    public static TrackerException Explain(
        TrackerException ex,
        ResourceVersionDecision decision,
        string rereadCommand,
        DateTimeOffset? now = null)
    {
        if (ex.Code != ErrorCode.VersionConflict
            || decision.Source != ResourceVersionSource.Cache
            || decision.Version is not { } version
            || decision.ReadAt is not { } readAt)
        {
            return ex;
        }

        var age = DescribeAge((now ?? DateTimeOffset.UtcNow) - readAt);
        return new TrackerException(
            ex.Code,
            $"{ex.Message} Версия {version.ToString(CultureInfo.InvariantCulture)} прочитана {age}, "
            + $"ресурс с тех пор изменили — перечитайте: {rereadCommand}",
            httpStatus: ex.HttpStatus,
            traceId: ex.TraceId,
            inner: ex);
    }

    /// <summary>
    /// Собирает ключ кэша для ресурса: единственная точка, где идентификатор приводится
    /// к каноническому виду, поэтому чтение и запись не могут разойтись в написании.
    /// </summary>
    /// <param name="ctx">Контекст выполнения (нужен действующий профиль с организацией).</param>
    /// <param name="resourceType">Дискриминатор типа ресурса.</param>
    /// <param name="resourceId">Идентификатор ресурса, как его ввёл пользователь.</param>
    /// <returns>Ключ для <see cref="ResourceVersionCache"/>.</returns>
    private static string BuildKey(TrackerContext ctx, string resourceType, string resourceId) =>
        ResourceVersionCache.BuildKey(
            ctx.Profile, resourceType, NormalizeResourceId(resourceType, resourceId));

    /// <summary>
    /// Приводит идентификатор ресурса к каноническому виду для ключа кэша.
    /// </summary>
    /// <param name="resourceType">Дискриминатор типа ресурса.</param>
    /// <param name="resourceId">Идентификатор, как его ввёл пользователь.</param>
    /// <returns>Идентификатор в том виде, в каком он ложится в ключ.</returns>
    /// <remarks>
    /// <para>
    /// Регистр приводится там и только там, где идентификатор регистронезависим в самом
    /// Трекере: <c>yt issue get tech-1</c> и <c>yt issue update TECH-1</c> адресуют один
    /// ресурс, и попадать в разные записи кэша они не должны — иначе защита молча не
    /// срабатывает ровно там, где пользователь вправе её ждать.
    /// </para>
    /// <para>
    /// Неизвестный тип ресурса не нормализуется: сложить два разных ресурса в одну запись
    /// хуже, чем промахнуться мимо кэша (промах — штатный путь, он лишь отправляет PATCH
    /// без версии). Заводя новый тип, приведите регистр здесь — но только выяснив, что его
    /// идентификатор действительно регистронезависим.
    /// </para>
    /// </remarks>
    internal static string NormalizeResourceId(string resourceType, string resourceId) => resourceType switch
    {
        IssueResource => resourceId.ToUpperInvariant(),
        TriggerResource or AutoactionResource => NormalizeQueueScopedId(resourceId),
        _ => resourceId,
    };

    /// <summary>
    /// Приводит регистр очереди в составном идентификаторе автоматизации <c>{queue}/{id}</c>.
    /// </summary>
    /// <param name="resourceId">Составной идентификатор.</param>
    /// <returns>Идентификатор с очередью в верхнем регистре.</returns>
    /// <remarks>
    /// Верхний регистр приводится только у ключа очереди. Идентификатор самой автоматизации
    /// — число, и трогать его нечему; приводить регистр всей строки значило бы заявить о
    /// нечисловых идентификаторах то, чего мы про них не знаем.
    /// </remarks>
    private static string NormalizeQueueScopedId(string resourceId)
    {
        var slash = resourceId.IndexOf('/');
        return slash < 0
            ? resourceId
            : string.Concat(resourceId[..slash].ToUpperInvariant(), resourceId[slash..]);
    }

    /// <summary>
    /// Читает корневое числовое поле <c>version</c> из ответа API.
    /// </summary>
    /// <param name="payload">Ответ API.</param>
    /// <returns>Версия либо <c>null</c>, если корень не объект, поля нет или оно не целое число.</returns>
    private static long? ReadVersion(JsonElement payload) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(VersionProperty, out var element)
        && element.ValueKind == JsonValueKind.Number
        && element.TryGetInt64(out var version)
            ? version
            : null;

    /// <summary>
    /// Достаёт запись из кэша, гася любую ошибку чтения.
    /// </summary>
    /// <param name="ctx">Контекст выполнения (нужен профиль).</param>
    /// <param name="resourceType">Дискриминатор типа ресурса.</param>
    /// <param name="resourceId">Идентификатор ресурса.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Запись либо <c>null</c> при промахе или ошибке.</returns>
    private static async Task<ResourceVersionEntry?> TryRecall(
        TrackerContext ctx, string resourceType, string resourceId, CancellationToken ct)
    {
        try
        {
            var cache = new ResourceVersionCache(ResourceVersionCache.DefaultPath);
            return await cache.Get(BuildKey(ctx, resourceType, resourceId), ct);
        }
        catch (TrackerException)
        {
            return null;
        }
    }

    /// <summary>
    /// Переводит возраст записи в русскую фразу вида «3 дня назад».
    /// </summary>
    /// <param name="age">Возраст записи.</param>
    /// <returns>Фраза для сообщения о конфликте.</returns>
    /// <remarks>
    /// Отрицательный возраст (часы уехали назад, файл кэша правили руками) описывается как
    /// «только что»: врать пользователю про будущее хуже, чем округлить.
    /// </remarks>
    internal static string DescribeAge(TimeSpan age)
    {
        if (age < TimeSpan.FromMinutes(1))
        {
            return "только что";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return Plural((long)age.TotalMinutes, "минуту", "минуты", "минут") + " назад";
        }

        if (age < TimeSpan.FromDays(1))
        {
            return Plural((long)age.TotalHours, "час", "часа", "часов") + " назад";
        }

        return Plural((long)age.TotalDays, "день", "дня", "дней") + " назад";
    }

    /// <summary>
    /// Склоняет существительное по русским правилам счёта.
    /// </summary>
    /// <param name="count">Количество.</param>
    /// <param name="one">Форма для 1 («минуту»).</param>
    /// <param name="few">Форма для 2–4 («минуты»).</param>
    /// <param name="many">Форма для 5–20 и нуля («минут»).</param>
    /// <returns>Число вместе со склонённым существительным.</returns>
    private static string Plural(long count, string one, string few, string many)
    {
        var mod100 = count % 100;
        var mod10 = count % 10;
        var form = mod100 is >= 11 and <= 14 ? many
            : mod10 == 1 ? one
            : mod10 is >= 2 and <= 4 ? few
            : many;
        return count.ToString(CultureInfo.InvariantCulture) + " " + form;
    }
}
