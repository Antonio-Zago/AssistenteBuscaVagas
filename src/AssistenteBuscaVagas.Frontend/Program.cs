using AssistenteBuscaVagas.Frontend.Components;
using AssistenteBuscaVagas.Frontend.Servicos;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// O frontend só fala com o supervisor. "http://supervisor" é resolvido pelo service discovery do Aspire.
// Sem a resiliência padrão: a conversa e a avaliação com o Claude podem passar de 30 s, e repetir o POST
// repetiria uma ação paga.
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers ainda é marcada como experimental no pacote.
builder.Services.AddHttpClient<ApiSupervisor>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Supervisor:Url"] ?? "http://supervisor/");
    c.Timeout = TimeSpan.FromMinutes(15);
}).RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

var app = builder.Build();

app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
