using BE;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace DAL;

public class ChurnDbContext : DbContext
{
    public ChurnDbContext(DbContextOptions<ChurnDbContext> options) : base(options) { }

    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<EventoCliente> EventosCliente => Set<EventoCliente>();
    public DbSet<FactorRiesgo> FactoresRiesgo => Set<FactorRiesgo>();
    public DbSet<Modelo> Modelos => Set<Modelo>();
    public DbSet<Prediccion> Predicciones => Set<Prediccion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Prediccion>()
            .Property(p => p.ProbabilidadAbandono)
            .HasColumnType("decimal(18,2)");

        modelBuilder.Entity<FactorRiesgo>()
            .Property(f => f.Impacto)
            .HasColumnType("decimal(18,2)");
    }
}
