using System.CommandLine;
using YandexTrackerCLI.Commands;

var parseResult = RootCommandBuilder.Build().Parse(args);
return await parseResult.InvokeAsync(new InvocationConfiguration());
