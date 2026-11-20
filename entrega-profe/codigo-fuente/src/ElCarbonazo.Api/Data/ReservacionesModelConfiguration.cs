using ElCarbonazo.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Data;

internal static class ReservacionesModelConfiguration
{
    internal static void ConfigureReservaciones(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Reservacion>(entity =>
        {
            entity.ToTable("reservacion", table =>
            {
                table.HasCheckConstraint("CK_reservacion_referencia",
                    "char_length(nombre_referencia) BETWEEN 1 AND 60 AND nombre_referencia = btrim(nombre_referencia) AND nombre_referencia = normalize(nombre_referencia, NFC) AND nombre_referencia !~ '[[:cntrl:]]'");
                table.HasCheckConstraint("CK_reservacion_personas", "cantidad_personas BETWEEN 1 AND 20");
                table.HasCheckConstraint("CK_reservacion_intervalo", "inicio_utc < fin_utc AND fin_utc <= inicio_utc + interval '8 hours'");
                table.HasCheckConstraint("CK_reservacion_estado",
                    "(estado = 'Activa' AND cancelada_utc IS NULL) OR (estado = 'Cancelada' AND cancelada_utc IS NOT NULL AND cancelada_utc >= creada_utc)");
            });
            entity.HasKey(x => x.IdReservacion);
            entity.Property(x => x.IdReservacion).HasColumnName("id_reservacion").ValueGeneratedOnAdd();
            entity.Property(x => x.IdMesa).HasColumnName("id_mesa").IsRequired();
            entity.Property(x => x.NombreReferencia).HasColumnName("nombre_referencia").HasMaxLength(60).IsRequired();
            entity.Property(x => x.CantidadPersonas).HasColumnName("cantidad_personas").IsRequired();
            entity.Property(x => x.InicioUtc).HasColumnName("inicio_utc").IsRequired();
            entity.Property(x => x.FinUtc).HasColumnName("fin_utc").IsRequired();
            entity.Property(x => x.Estado).HasColumnName("estado").HasMaxLength(12).IsRequired();
            entity.Property(x => x.CreadaUtc).HasColumnName("creada_utc").IsRequired();
            entity.Property(x => x.CanceladaUtc).HasColumnName("cancelada_utc");
            entity.HasIndex(x => new { x.IdMesa, x.Estado, x.InicioUtc });
            entity.HasOne(x => x.Mesa).WithMany(x => x.Reservaciones)
                .HasForeignKey(x => x.IdMesa).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
