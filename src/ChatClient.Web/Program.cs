using ChatClient.Web.Components;
using ChatClient.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Interactive Server components keep a live SignalR circuit per browser tab, which is
// exactly what a chat UI needs for real-time updates without any custom JavaScript.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// One chat connection (and therefore one TCP socket to the chat server) per circuit.
builder.Services.AddScoped<ChatConnection>();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
