using ElCarbonazo.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Data;

internal static class InventarioModelConfiguration
{
    internal static void ConfigureInventario(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ingrediente>(entity =>
        {
            entity.ToTable("ingrediente", table =>
            {
                table.HasCheckConstraint("CK_ingrediente_nombre", "char_length(nombre) BETWEEN 1 AND 120 AND char_length(nombre_clave) BETWEEN 1 AND 240");
                table.HasCheckConstraint("CK_ingrediente_unidad", "unidad_medida IN ('lb', 'kg', 'oz', 'g', 'unidad', 'ml', 'lt')");
                table.HasCheckConstraint("CK_ingrediente_stock", "stock_actual >= 0 AND stock_minimo >= 0");
                table.HasCheckConstraint("CK_ingrediente_version", "version > 0");
            });
            entity.HasKey(item => item.IdIngrediente);
            entity.Property(item => item.IdIngrediente).HasColumnName("id_ingrediente").ValueGeneratedOnAdd();
            entity.Property(item => item.Nombre).HasColumnName("nombre").HasMaxLength(120).IsRequired();
            entity.Property(item => item.NombreClave).HasColumnName("nombre_clave").HasMaxLength(240).UseCollation("C").IsRequired();
            entity.Property(item => item.UnidadMedida).HasColumnName("unidad_medida").HasMaxLength(20).IsRequired();
            entity.Property(item => item.StockActual).HasColumnName("stock_actual").HasPrecision(12, 4).HasDefaultValue(0m);
            entity.Property(item => item.StockMinimo).HasColumnName("stock_minimo").HasPrecision(12, 4).HasDefaultValue(0m);
            entity.Property(item => item.Activo).HasColumnName("activo").HasDefaultValue(true);
            entity.Property(item => item.FechaCreacionUtc).HasColumnName("fecha_creacion_utc").IsRequired();
            entity.Property(item => item.FechaActualizacionUtc).HasColumnName("fecha_actualizacion_utc").IsRequired();
            entity.Property(item => item.Version).HasColumnName("version").HasDefaultValue(1L).IsConcurrencyToken();
            entity.HasIndex(item => item.NombreClave).IsUnique();
        });

        modelBuilder.Entity<Receta>(entity =>
        {
            entity.ToTable("receta", table => table.HasCheckConstraint("CK_receta_version", "version > 0"));
            entity.HasKey(item => item.IdReceta);
            entity.Property(item => item.IdReceta).HasColumnName("id_receta").ValueGeneratedOnAdd();
            entity.Property(item => item.IdProducto).HasColumnName("id_producto");
            entity.Property(item => item.Activa).HasColumnName("activa").HasDefaultValue(true);
            entity.Property(item => item.NotasPreparacion).HasColumnName("notas_preparacion").HasMaxLength(500);
            entity.Property(item => item.FechaActualizacionUtc).HasColumnName("fecha_actualizacion_utc").IsRequired();
            entity.Property(item => item.Version).HasColumnName("version").HasDefaultValue(1L).IsConcurrencyToken();
            entity.HasIndex(item => item.IdProducto).IsUnique();
            entity.HasOne(item => item.Producto).WithMany().HasForeignKey(item => item.IdProducto).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DetalleReceta>(entity =>
        {
            entity.ToTable("detalle_receta", table => table.HasCheckConstraint("CK_detalle_receta_cantidad", "cantidad_requerida > 0"));
            entity.HasKey(item => item.IdDetalleReceta);
            entity.Property(item => item.IdDetalleReceta).HasColumnName("id_detalle_receta").ValueGeneratedOnAdd();
            entity.Property(item => item.IdReceta).HasColumnName("id_receta");
            entity.Property(item => item.IdIngrediente).HasColumnName("id_ingrediente");
            entity.Property(item => item.CantidadRequerida).HasColumnName("cantidad_requerida").HasPrecision(12, 4);
            entity.HasIndex(item => new { item.IdReceta, item.IdIngrediente }).IsUnique();
            entity.HasOne(item => item.Receta).WithMany(item => item.Detalles).HasForeignKey(item => item.IdReceta).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Ingrediente).WithMany().HasForeignKey(item => item.IdIngrediente).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MovimientoInventario>(entity =>
        {
            entity.ToTable("movimiento_inventario", table =>
            {
                table.HasCheckConstraint("CK_movimiento_tipo_delta", "(tipo_movimiento IN ('EntradaCompra', 'AjustePositivo') AND cantidad_delta > 0) OR (tipo_movimiento IN ('AjusteNegativo', 'ConsumoPedido') AND cantidad_delta < 0)");
                table.HasCheckConstraint("CK_movimiento_saldos", "stock_anterior >= 0 AND stock_posterior >= 0 AND stock_posterior = stock_anterior + cantidad_delta");
                table.HasCheckConstraint("CK_movimiento_origen", "(tipo_movimiento = 'ConsumoPedido' AND id_pedido IS NOT NULL AND id_comanda IS NOT NULL) OR (tipo_movimiento <> 'ConsumoPedido' AND id_pedido IS NULL AND id_comanda IS NULL)");
                table.HasCheckConstraint("CK_movimiento_motivo", "tipo_movimiento NOT IN ('AjustePositivo', 'AjusteNegativo') OR (motivo IS NOT NULL AND char_length(btrim(motivo)) BETWEEN 1 AND 250)");
            });
            entity.HasKey(item => item.IdMovimiento);
            entity.Property(item => item.IdMovimiento).HasColumnName("id_movimiento").ValueGeneratedOnAdd();
            entity.Property(item => item.IdIngrediente).HasColumnName("id_ingrediente");
            entity.Property(item => item.TipoMovimiento).HasColumnName("tipo_movimiento").HasMaxLength(30).IsRequired();
            entity.Property(item => item.CantidadDelta).HasColumnName("cantidad_delta").HasPrecision(12, 4);
            entity.Property(item => item.StockAnterior).HasColumnName("stock_anterior").HasPrecision(12, 4);
            entity.Property(item => item.StockPosterior).HasColumnName("stock_posterior").HasPrecision(12, 4);
            entity.Property(item => item.IdPedido).HasColumnName("id_pedido");
            entity.Property(item => item.IdComanda).HasColumnName("id_comanda");
            entity.Property(item => item.IdUsuario).HasColumnName("id_usuario");
            entity.Property(item => item.Motivo).HasColumnName("motivo").HasMaxLength(250);
            entity.Property(item => item.FechaUtc).HasColumnName("fecha_utc").IsRequired();
            entity.HasIndex(item => new { item.IdComanda, item.IdIngrediente }).IsUnique().HasFilter("tipo_movimiento = 'ConsumoPedido'");
            entity.HasIndex(item => new { item.IdIngrediente, item.IdMovimiento });
            entity.HasOne(item => item.Ingrediente).WithMany().HasForeignKey(item => item.IdIngrediente).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Pedido).WithMany().HasForeignKey(item => item.IdPedido).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Comanda).WithMany().HasForeignKey(item => item.IdComanda).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Usuario).WithMany().HasForeignKey(item => item.IdUsuario).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
