using BLL;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Servicios;

// Cada minuto revisa qué integraciones activas tienen la sincronización vencida
// (BLLIntegracion.IntervaloSincronizacion) y las sincroniza. Igual que PrediccionBackgroundService,
// no tiene lógica propia: delega todo en la BLL.
public class IntegracionSyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IntegracionSyncBackgroundService> _logger;
    private static readonly TimeSpan Revision = TimeSpan.FromMinutes(1);

    public IntegracionSyncBackgroundService(IServiceScopeFactory scopeFactory, ILogger<IntegracionSyncBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(Revision);
            do
            {
                await EjecutarCicloAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // la aplicación se está cerrando
        }
    }

    private async Task EjecutarCicloAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var bllIntegracion = scope.ServiceProvider.GetRequiredService<BLLIntegracion>();
            var cantidad = await bllIntegracion.SincronizarPendientes(stoppingToken);

            if (cantidad > 0)
                _logger.LogInformation("Sincronización automática: {Cantidad} integración(es) procesada(s).", cantidad);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Error durante la sincronización automática de integraciones.");
        }
    }
}
