using Pch.UI.Components;
using Pch.UI.Features.EndUserChat;
using Pch.UI.Features.StageCockpit;
using Pch.Harness;
using Pch.Providers.LivePreflight;
using Pch.Providers.LiveTurns;
using Pch.Providers.Mock;
using Pch.Providers.ModelRoles;
using Pch.Providers.OpenAi;
using Pch.Providers.OpenRouter;
using Pch.Providers.PlannerPrimitives;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddScoped<EndUserLiveModelTurnService>(_ => CreateEndUserLiveModelTurnService());
builder.Services.AddScoped(_ => new EndUserChatService(
    new GoldenTurnTraceRunner(),
    new ModelRoleStatusEvaluator(new MockModelRoleStatusSource()),
    _.GetRequiredService<EndUserLiveModelTurnService>()));
builder.Services.AddScoped<FormBuilder>();
builder.Services.AddScoped(sp => new PlanningSessionService(
    sp.GetRequiredService<EndUserChatService>(),
    sp.GetRequiredService<FormBuilder>(),
    ReadProcessEnvironment,
    CreatePlannerPrimitiveRunner()));
builder.Services.AddSingleton<PlanningSessionStore>();
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
app.MapPost("/api/planning/session/start", async (
    PlanningSessionStore store,
    PlanningSessionService service,
    PlanningSessionStartRequest request,
    CancellationToken cancellationToken) =>
{
    var response = await store.StartAsync(service, request, cancellationToken);
    return Results.Json(response);
});
app.MapPost("/api/planning/session/{sessionId}/answer", async (
    PlanningSessionStore store,
    PlanningSessionService service,
    string sessionId,
    PlanningSessionAnswerRequest request,
    CancellationToken cancellationToken) =>
{
    var response = await store.AnswerAsync(service, sessionId, request, cancellationToken);
    return response.Status == "planning_session_unknown"
        ? Results.NotFound(response)
        : Results.Json(response);
});
app.MapGet("/api/planning/session/{sessionId}", (
    PlanningSessionStore store,
    string sessionId) =>
{
    var response = store.Get(sessionId);
    return response.Status == "planning_session_unknown"
        ? Results.NotFound(response)
        : Results.Json(response);
});
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static EndUserLiveModelTurnService CreateEndUserLiveModelTurnService()
{
    var environment = ReadProcessEnvironment();
    var options = LivePreflightOptions.FromEnvironment(environment);
    var keyAvailable = options.ApiKeyAvailable || ProviderKeyFilePresent(environment, "OPENROUTER_API_KEY_FILE");
    if (!options.Enabled ||
        !keyAvailable ||
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
        new EndUserLiveTurnGateway(
            ReadProcessEnvironment,
            new LiveTurnRunner(openRouter, openRouter)));
}

static PlannerPrimitiveModelRunner? CreatePlannerPrimitiveRunner()
{
    var environment = ReadProcessEnvironment();
    var options = PlannerModelOptions.FromEnvironment(environment);
    var openRouterKeyAvailable = options.ApiKeyAvailable || ProviderKeyFilePresent(environment, "OPENROUTER_API_KEY_FILE");
    var openAiKeyAvailable = options.ApiKeyAvailable || ProviderKeyFilePresent(environment, "OPENAI_API_KEY_FILE");
    if (!options.Enabled)
    {
        return null;
    }

    PlannerPrimitiveRunner runner;
    if (options.ProviderKind is LivePreflightProviderKind.OpenAi && openAiKeyAvailable)
    {
        var openAi = new OpenAiModelCompletionClient(
            new HttpClient(),
            new OpenAiOptions
            {
                Model = options.Model,
                Timeout = options.Timeout ?? TimeSpan.FromSeconds(30),
                ApiKeyFilePath = Environment.GetEnvironmentVariable("OPENAI_API_KEY_FILE")
            });
        runner = new PlannerPrimitiveRunner(openAi, openAi);
    }
    else if (options.ProviderKind is LivePreflightProviderKind.OpenRouter && openRouterKeyAvailable)
    {
        var openRouter = new OpenRouterModelCompletionClient(
            new HttpClient(),
            new OpenRouterOptions
            {
                Model = options.Model,
                Timeout = options.Timeout ?? TimeSpan.FromSeconds(30),
                CheckCreditsBeforeCompletion = options.CreditGuardEnabled,
                ApiKeyFilePath = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY_FILE")
            });
        runner = new PlannerPrimitiveRunner(openRouter, openRouter);
    }
    else
    {
        return null;
    }

    return runner.RunAsync;
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

static bool ProviderKeyFilePresent(IReadOnlyDictionary<string, string?> environment, string key) =>
    environment.TryGetValue(key, out var path) &&
    !string.IsNullOrWhiteSpace(path) &&
    File.Exists(path) &&
    new FileInfo(path).Length > 0;
