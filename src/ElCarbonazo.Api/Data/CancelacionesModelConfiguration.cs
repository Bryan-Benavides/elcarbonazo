using ElCarbonazo.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Data;

internal static class CancelacionesModelConfiguration
{
    internal static void ConfigureCancelaciones(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConsumoDetalle>(entity =>
        {
            entity.ToTable("consumo_detalle", table => table.HasCheckConstraint("CK_consumo_detalle_cantidad", "cantidad > 0"));
            entity.HasKey(x => new { x.IdDetalle, x.IdIngrediente });
            entity.Property(x => x.IdDetalle).HasColumnName("id_detalle");
            entity.Property(x => x.IdIngrediente).HasColumnName("id_ingrediente");
            entity.Property(x => x.Cantidad).HasColumnName("cantidad").HasPrecision(12, 4);
            entity.HasOne<DetallePedido>().WithMany().HasForeignKey(x => x.IdDetalle).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Ingrediente>().WithMany().HasForeignKey(x => x.IdIngrediente).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AnulacionPedido>(entity =>
        {
            entity.ToTable("anulacion_pedido", table =>
            {
                table.HasCheckConstraint("CK_anulacion_cantidad_monto", "cantidad_anulada > 0 AND monto_revertido >= 0");
                table.HasCheckConstraint("CK_anulacion_estado", "estado_comanda_previo IN ('Abierta', 'En Preparación', 'Lista')");
                table.HasCheckConstraint("CK_anulacion_motivo", "char_length(motivo) BETWEEN 5 AND 250 AND motivo = btrim(motivo) AND motivo = normalize(motivo, NFC)");
            });
            entity.HasKey(x => x.IdAnulacion);
            entity.Property(x => x.IdAnulacion).HasColumnName("id_anulacion").ValueGeneratedOnAdd();
            entity.Property(x => x.IdPedido).HasColumnName("id_pedido");
            entity.Property(x => x.IdComanda).HasColumnName("id_comanda");
            entity.Property(x => x.IdDetalle).HasColumnName("id_detalle");
            entity.Property(x => x.CantidadAnulada).HasColumnName("cantidad_anulada");
            entity.Property(x => x.MontoRevertido).HasColumnName("monto_revertido").HasPrecision(12, 2);
            entity.Property(x => x.EstadoComandaPrevio).HasColumnName("estado_comanda_previo").HasMaxLength(30).IsRequired();
            entity.Property(x => x.IdUsuarioSolicita).HasColumnName("id_usuario_solicita");
            entity.Property(x => x.IdUsuarioAutoriza).HasColumnName("id_usuario_autoriza");
            entity.Property(x => x.Motivo).HasColumnName("motivo").HasMaxLength(250).IsRequired();
            entity.Property(x => x.InventarioRevertido).HasColumnName("inventario_revertido");
            entity.Property(x => x.FechaUtc).HasColumnName("fecha_utc");
            entity.HasOne<Pedido>().WithMany().HasForeignKey(x => x.IdPedido).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Comanda>().WithMany().HasForeignKey(x => x.IdComanda).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<DetallePedido>().WithMany().HasForeignKey(x => x.IdDetalle).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(x => x.IdUsuarioSolicita).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(x => x.IdUsuarioAutoriza).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OperacionAnulacion>(entity =>
        {
            entity.ToTable("operacion_anulacion", table =>
            {
                table.HasCheckConstraint("CK_operacion_anulacion_estado", "estado IN ('Autorizada', 'Completada', 'Rechazada', 'Cancelada')");
                table.HasCheckConstraint("CK_operacion_anulacion_resultado", "(estado = 'Autorizada' AND hash_intencion IS NOT NULL AND fecha_terminal_utc IS NULL AND status_resultado IS NULL AND codigo_resultado IS NULL AND resultado_json IS NULL) OR (estado IN ('Completada', 'Rechazada', 'Cancelada') AND fecha_terminal_utc IS NOT NULL AND status_resultado IS NOT NULL AND codigo_resultado IS NOT NULL AND resultado_json IS NOT NULL AND (hash_intencion IS NOT NULL OR estado = 'Cancelada'))");
                table.HasCheckConstraint("CK_operacion_anulacion_hash", "hash_intencion IS NULL OR hash_intencion ~ '^[0-9a-f]{64}$'");
            });
            entity.HasKey(x => x.IdOperacion);
            entity.Property(x => x.IdOperacion).HasColumnName("id_operacion").ValueGeneratedNever();
            entity.Property(x => x.IdUsuarioPropietario).HasColumnName("id_usuario_propietario");
            entity.Property(x => x.HashIntencion).HasColumnName("hash_intencion").HasMaxLength(64);
            entity.Property(x => x.Estado).HasColumnName("estado").HasMaxLength(20).IsRequired();
            entity.Property(x => x.FechaCreacionUtc).HasColumnName("fecha_creacion_utc");
            entity.Property(x => x.FechaTerminalUtc).HasColumnName("fecha_terminal_utc");
            entity.Property(x => x.StatusResultado).HasColumnName("status_resultado");
            entity.Property(x => x.CodigoResultado).HasColumnName("codigo_resultado").HasMaxLength(80);
            entity.Property(x => x.ResultadoJson).HasColumnName("resultado_json").HasColumnType("jsonb");
            entity.HasOne<Usuario>().WithMany().HasForeignKey(x => x.IdUsuarioPropietario).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AutorizacionAnulacion>(entity =>
        {
            entity.ToTable("autorizacion_anulacion", table =>
            {
                table.HasCheckConstraint("CK_autorizacion_hash", "hash_token ~ '^[0-9a-f]{64}$'");
                table.HasCheckConstraint("CK_autorizacion_fecha", "fecha_expiracion_utc > fecha_emision_utc AND fecha_expiracion_utc <= fecha_emision_utc + interval '2 minutes' AND (fecha_consumo_utc IS NULL OR fecha_consumo_utc >= fecha_emision_utc)");
            });
            entity.HasKey(x => x.IdAutorizacion);
            entity.Property(x => x.IdAutorizacion).HasColumnName("id_autorizacion").ValueGeneratedOnAdd();
            entity.Property(x => x.IdOperacion).HasColumnName("id_operacion");
            entity.Property(x => x.IdSesion).HasColumnName("id_sesion");
            entity.Property(x => x.IdUsuarioAutoriza).HasColumnName("id_usuario_autoriza");
            entity.Property(x => x.HashToken).HasColumnName("hash_token").HasMaxLength(64).IsRequired();
            entity.Property(x => x.FechaEmisionUtc).HasColumnName("fecha_emision_utc");
            entity.Property(x => x.FechaExpiracionUtc).HasColumnName("fecha_expiracion_utc");
            entity.Property(x => x.FechaConsumoUtc).HasColumnName("fecha_consumo_utc");
            entity.HasIndex(x => x.HashToken).IsUnique();
            entity.HasOne<OperacionAnulacion>().WithMany().HasForeignKey(x => x.IdOperacion).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SesionUsuario>().WithMany().HasForeignKey(x => x.IdSesion).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(x => x.IdUsuarioAutoriza).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AlertaAnulacion>(entity =>
        {
            entity.ToTable("alerta_anulacion", table => table.HasCheckConstraint("CK_alerta_acuse", "(fecha_acuse_utc IS NULL AND id_usuario_acuse IS NULL) OR (fecha_acuse_utc IS NOT NULL AND id_usuario_acuse IS NOT NULL)"));
            entity.HasKey(x => x.IdAnulacion);
            entity.Property(x => x.IdAnulacion).HasColumnName("id_anulacion").ValueGeneratedNever();
            entity.Property(x => x.FechaAcuseUtc).HasColumnName("fecha_acuse_utc");
            entity.Property(x => x.IdUsuarioAcuse).HasColumnName("id_usuario_acuse");
            entity.HasOne<AnulacionPedido>().WithOne().HasForeignKey<AlertaAnulacion>(x => x.IdAnulacion).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Usuario>().WithMany().HasForeignKey(x => x.IdUsuarioAcuse).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LimiteAutorizacion>(entity =>
        {
            entity.ToTable("limite_autorizacion", table =>
            {
                table.HasCheckConstraint("CK_limite_autorizacion_intentos", "intentos_fallidos BETWEEN 0 AND 5");
                table.HasCheckConstraint("CK_limite_autorizacion_hash", "hash_nombre ~ '^[0-9a-f]{64}$'");
            });
            entity.HasKey(x => new { x.IdUsuarioSolicita, x.HashNombre });
            entity.Property(x => x.IdUsuarioSolicita).HasColumnName("id_usuario_solicita");
            entity.Property(x => x.HashNombre).HasColumnName("hash_nombre").HasMaxLength(64);
            entity.Property(x => x.IntentosFallidos).HasColumnName("intentos_fallidos");
            entity.Property(x => x.BloqueoHastaUtc).HasColumnName("bloqueo_hasta_utc");
            entity.Property(x => x.UltimaActividadUtc).HasColumnName("ultima_actividad_utc");
            entity.HasOne<Usuario>().WithMany().HasForeignKey(x => x.IdUsuarioSolicita).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
