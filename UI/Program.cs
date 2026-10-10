using BLL;
using DAL;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Servicios;
using UI.Components;
using UI.Services;

var builder = WebApplication.CreateBuilder(args);
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.AddDbContext<ChurnDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

//DAL
builder.Services.AddScoped<AccesoDatos>();

// BLL
builder.Services.AddScoped<BLLPrediccion>();
builder.Services.AddScoped<BLLModelo>();
builder.Services.AddScoped<BLLCliente>();
builder.Services.AddScoped<BLLFactorRiesgo>();
builder.Services.AddScoped<BLLCampana>();
builder.Services.AddScoped<BLLHistorialAcciones>();
builder.Services.AddScoped<BLLIncentivo>();
builder.Services.AddScoped<BLLCanal>();
builder.Services.AddScoped<BLLRegla>();
builder.Services.AddScoped<BLLSede>();
builder.Services.AddScoped<BLLPlanSocio>();
builder.Services.AddScoped<BLLIntegracion>();

// UI
builder.Services.AddScoped<GeneradorPdf>();
builder.Services.AddScoped<ServicioEnvio>();

builder.Services.AddHostedService<PrediccionBackgroundService>();

builder.Services.AddDataProtection().SetApplicationName("TFILasalvia");

var app = builder.Build();
app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();


/*if (app.Environment.IsDevelopment())
{
    app.MapGet("/dev/probar-email", async (ServicioEnvio envio) =>
    {
        var r = await envio.EnviarEmail("socio@mailtest.com", "Prueba TFI", "Hola, este es un mail de prueba.");
        return Results.Text(r.Mensaje);
    });
}*/

/*app.MapGet("/dev/probar-telegram", async (ServicioEnvio envio) =>
{
    var r = await envio.EnviarTelegram(null, "Hola, este es un Telegram de prueba del TFI.");
    return Results.Text(r.Mensaje);
});*/


app.Run();