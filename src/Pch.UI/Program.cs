using Pch.UI.Components;
using Pch.UI.Features.EndUserChat;
using Pch.UI.Features.StageCockpit;
using Pch.Harness;
using Pch.Providers.LiveMissionProposal;
using Pch.Providers.LivePreflight;
using Pch.Providers.Mock;
using Pch.Providers.ModelRoles;
using Pch.Providers.OpenRouter;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddScoped<EndUserLiveModelTurnService>(_ => CreateEndUserLiveModelTurnService());
builder.Services.AddScoped(_ => new EndUserChatService(
    new GoldenTurnTraceRunner(),
    new ModelRoleStatusEvaluator(new MockModelRoleStatusSource()),
    _.GetRequiredService<EndUserLiveModelTurnService>()));
builder.Services.AddScoped<HarnessStageCockpitService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static EndUserLiveModelTurnService CreateEndUserLiveModelTurnService()
{
    var environment = ReadProcessEnvironment();
    var options = LivePreflightOptions.FromEnvironment(environment);
    if (!options.Enabled ||
        !options.ApiKeyAvailable ||
        options.ProviderKind is not LivePreflightProviderKind.OpenRouter)
    {
        return new EndUserLiveModelTurnService(ReadProcessEnvironment);
    }

    var openRouter = new OpenRouterModelCompletionClient(
        new HttpClient(),
        new OpenRouterOptions
        {
            Model = options.InHarnessModelId,
            Timeout = options.Timeout ?? TimeSpan.FromSeconds(30),
            CheckCreditsBeforeCompletion = options.CreditGuardEnabled,
            ApiKeyFilePath = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY_FILE")
        });
    var preflight = new LivePreflightEvaluator(new LivePreflightRunner(openRouter, openRouter));

    return new EndUserLiveModelTurnService(
        ReadProcessEnvironment,
        preflight,
        new LiveSessionConductor(),
        new EndUserLiveMissionProposalGateway(
            ReadProcessEnvironment,
            new LiveMissionProposalRunner(openRouter, openRouter)));
}

static IReadOnlyDictionary<string, string?> ReadProcessEnvironment()
{
    var values = Environment.GetEnvironmentVariables();
    var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
    foreach (var key in values.Keys.OfType<string>())
    {
        environment[key] = values[key]?.ToString();
    }

    return environment;
}
