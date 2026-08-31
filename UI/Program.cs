using BLL;
using DAL;
using Microsoft.EntityFrameworkCore;
using Servicios;
using UI.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// DAL
builder.Services.AddDbContext<ChurnDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<IChurnRepository, ChurnRepository>();

// BLL
builder.Services.AddScoped<ChurnModelService>();

// El calculo automatico arranca junto con la UI, en el mismo proceso
builder.Services.AddHostedService<PrediccionBackgroundService>();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
