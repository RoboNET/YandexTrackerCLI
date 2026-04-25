using System.CommandLine;
using YandexTrackerCLI.Commands;
using YandexTrackerCLI.Skill;

// Авто-проверка актуальности skill (Claude/Codex). Skip:
//   - команда yt skill *  → группа сама знает про статус
//   - --version / --help  → lightweight операции
//   - --no-skill-check    → одноразовое отключение
//   - YT_SKILL_CHECK=0    → постоянное отключение через env
// Любое исключение — silent (не должно ломать пользовательскую команду).
if (!SkillAutoCheck.ShouldSkipFromArgs(args))
{
    try
    {
        SkillAutoCheck.RunIfNeeded(Directory.GetCurrentDirectory());
    }
    catch
    {
        // never fail user's command because of auto-check
    }
}

var parseResult = RootCommandBuilder.Build().Parse(args);
return await parseResult.InvokeAsync(new InvocationConfiguration());
