using ElCarbonazo.Api.Data;
using ElCarbonazo.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Tests;

internal sealed class PedidosHistoricalContext(string connectionString)
    : AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Comanda>().Ignore(x => x.RevisionDetalles);
        modelBuilder.Entity<DetallePedido>().Ignore(x => x.Cancelado);
        modelBuilder.Entity<Mesa>().Ignore(x => x.PermitePedidoTrasAnulacion);
        modelBuilder.Entity<GrupoMesa>().Ignore(x => x.PermitePedidoTrasAnulacion);
    }
}
