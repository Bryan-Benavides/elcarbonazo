using ElCarbonazo.Api.Domain;
using ElCarbonazo.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace ElCarbonazo.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Mesa> Mesas => Set<Mesa>();
    public DbSet<GrupoMesa> GruposMesa => Set<GrupoMesa>();
    public DbSet<GrupoMesaMiembro> GruposMesaMiembros => Set<GrupoMesaMiembro>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<SesionUsuario> SesionesUsuario => Set<SesionUsuario>();
    public DbSet<TokenRefreshSesion> TokensRefreshSesion => Set<TokenRefreshSesion>();
    public DbSet<EventoAuditoria> EventosAuditoria => Set<EventoAuditoria>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tabla usuario
        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.ToTable("usuario", table =>
            {
                table.HasCheckConstraint(
                    "CK_usuario_rol",
                    "rol IN ('Jefa', 'Encargado de Caja', 'Mesero', 'Personal de Cocina', 'Supervisor')");
                table.HasCheckConstraint(
                    "CK_usuario_nombre_normalizado",
                    "nombre = normalize(nombre, NFC) AND nombre = lower(btrim(nombre))");
                table.HasCheckConstraint(
                    "CK_usuario_nombre_longitud",
                    "char_length(nombre) BETWEEN 3 AND 50");
                table.HasCheckConstraint(
                    "CK_usuario_email_normalizado",
                    "email = normalize(email, NFC) AND email = lower(btrim(email))");
                table.HasCheckConstraint(
                    "CK_usuario_email_longitud",
                    "char_length(email) BETWEEN 3 AND 254");
                table.HasCheckConstraint(
                    "CK_usuario_intentos_fallidos",
                    "intentos_fallidos BETWEEN 0 AND 5");
            });
            entity.HasKey(e => e.IdUsuario);
            entity.Property(e => e.IdUsuario).HasColumnName("id_usuario").ValueGeneratedOnAdd();
            entity.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(50).IsRequired();
            entity.Property(e => e.ContrasenaHash).HasColumnName("contrasena_hash").HasMaxLength(255).IsRequired();
            entity.Property(e => e.Rol).HasColumnName("rol").HasMaxLength(50).IsRequired();
            entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(254).IsRequired();
            entity.Property(e => e.Activo).HasColumnName("activo").HasDefaultValue(true).IsRequired();
            entity.Property(e => e.IntentosFallidos)
                .HasColumnName("intentos_fallidos")
                .HasDefaultValue(0)
                .IsRequired();
            entity.Property(e => e.BloqueoHasta).HasColumnName("bloqueo_hasta").IsRequired(false);

            entity.HasIndex(e => e.Nombre).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
        });

        // Tabla mesa
        modelBuilder.Entity<Mesa>(entity =>
        {
            entity.ToTable("mesa", table =>
            {
                table.HasCheckConstraint("CK_mesa_numero", "numero BETWEEN 1 AND 15");
                table.HasCheckConstraint("CK_mesa_capacidad", "capacidad BETWEEN 1 AND 20");
                table.HasCheckConstraint(
                    "CK_mesa_estado",
                    "estado IN ('Libre', 'Ocupada', 'Reservada', 'Agrupada')");
                table.HasCheckConstraint(
                    "CK_mesa_grupo_estado",
                    "(estado = 'Agrupada') = (id_grupo IS NOT NULL)");
            });
            entity.HasKey(e => e.IdMesa);
            entity.Property(e => e.IdMesa).HasColumnName("id_mesa").ValueGeneratedOnAdd();
            entity.Property(e => e.IdGrupo).HasColumnName("id_grupo").IsRequired(false);
            entity.Property(e => e.Numero).HasColumnName("numero").IsRequired();
            entity.Property(e => e.Capacidad).HasColumnName("capacidad").IsRequired();
            entity.Property(e => e.Estado).HasColumnName("estado").HasMaxLength(20).IsRequired();
            entity.Property(e => e.Version).IsRowVersion();

            // Índice único para numero
            entity.HasIndex(e => e.Numero).IsUnique();

            entity.HasOne(e => e.GrupoActivo)
                .WithMany(e => e.MesasActivas)
                .HasForeignKey(e => e.IdGrupo)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SesionUsuario>(entity =>
        {
            entity.ToTable("sesion_usuario");
            entity.HasKey(session => session.IdSesion);
            entity.Property(session => session.IdSesion).HasColumnName("id_sesion").ValueGeneratedNever();
            entity.Property(session => session.IdUsuario).HasColumnName("id_usuario").IsRequired();
            entity.Property(session => session.FechaCreacion).HasColumnName("fecha_creacion").IsRequired();
            entity.Property(session => session.UltimaActividad).HasColumnName("ultima_actividad").IsRequired();
            entity.Property(session => session.ExpiracionAbsoluta).HasColumnName("expiracion_absoluta").IsRequired();
            entity.Property(session => session.FechaRevocacion).HasColumnName("fecha_revocacion").IsRequired(false);
            entity.HasIndex(session => session.IdUsuario)
                .IsUnique()
                .HasFilter("fecha_revocacion IS NULL");
            entity.HasOne(session => session.Usuario)
                .WithMany()
                .HasForeignKey(session => session.IdUsuario)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TokenRefreshSesion>(entity =>
        {
            entity.ToTable("token_refresh_sesion");
            entity.HasKey(token => token.IdToken);
            entity.Property(token => token.IdToken).HasColumnName("id_token").ValueGeneratedOnAdd();
            entity.Property(token => token.IdSesion).HasColumnName("id_sesion").IsRequired();
            entity.Property(token => token.TokenHash).HasColumnName("token_hash").HasColumnType("character(64)").HasMaxLength(64).IsRequired();
            entity.Property(token => token.FechaEmision).HasColumnName("fecha_emision").IsRequired();
            entity.Property(token => token.FechaConsumo).HasColumnName("fecha_consumo").IsRequired(false);
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasIndex(token => token.IdSesion)
                .IsUnique()
                .HasFilter("fecha_consumo IS NULL");
            entity.HasOne(token => token.Sesion)
                .WithMany(session => session.TokensRefresh)
                .HasForeignKey(token => token.IdSesion)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EventoAuditoria>(entity =>
        {
            entity.ToTable("evento_auditoria", table =>
            {
                table.HasCheckConstraint(
                    "CK_evento_auditoria_resultado",
                    "resultado IN ('Exitoso', 'Rechazado', 'Error')");
            });
            entity.HasKey(audit => audit.IdEvento);
            entity.Property(audit => audit.IdEvento).HasColumnName("id_evento").ValueGeneratedOnAdd();
            entity.Property(audit => audit.FechaHora).HasColumnName("fecha_hora").IsRequired();
            entity.Property(audit => audit.TipoEvento).HasColumnName("tipo_evento").HasMaxLength(50).IsRequired();
            entity.Property(audit => audit.Resultado).HasColumnName("resultado").HasMaxLength(20).IsRequired();
            entity.Property(audit => audit.IdUsuario).HasColumnName("id_usuario").IsRequired(false);
            entity.Property(audit => audit.IdSesion).HasColumnName("id_sesion").IsRequired(false);
            entity.Property(audit => audit.DireccionIp).HasColumnName("direccion_ip").HasColumnType("inet").IsRequired(false);
            entity.Property(audit => audit.IdInstalacion).HasColumnName("id_instalacion").IsRequired(false);
            entity.Property(audit => audit.TipoEntidad).HasColumnName("tipo_entidad").HasMaxLength(30).IsRequired(false);
            entity.Property(audit => audit.IdEntidad).HasColumnName("id_entidad").HasMaxLength(64).IsRequired(false);
            entity.Property(audit => audit.TraceId).HasColumnName("trace_id").HasMaxLength(64).IsRequired();
            entity.Property(audit => audit.Detalles)
                .HasColumnName("detalles")
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'{}'::jsonb")
                .IsRequired();
            entity.HasIndex(audit => audit.FechaHora);
            entity.HasIndex(audit => new { audit.TipoEvento, audit.FechaHora });
            entity.HasIndex(audit => new { audit.IdUsuario, audit.FechaHora });
            entity.HasIndex(audit => new { audit.TipoEntidad, audit.IdEntidad, audit.FechaHora });
            entity.HasOne<Usuario>()
                .WithMany()
                .HasForeignKey(audit => audit.IdUsuario)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SesionUsuario>()
                .WithMany()
                .HasForeignKey(audit => audit.IdSesion)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GrupoMesa>(entity =>
        {
            entity.ToTable("grupo_mesa", table =>
            {
                table.HasCheckConstraint(
                    "CK_grupo_mesa_cierre",
                    "(activo AND fecha_cierre IS NULL AND id_usuario_cierre IS NULL) OR " +
                    "(NOT activo AND fecha_cierre IS NOT NULL AND id_usuario_cierre IS NOT NULL)");
            });
            entity.HasKey(e => e.IdGrupo);
            entity.Property(e => e.IdGrupo).HasColumnName("id_grupo").ValueGeneratedOnAdd();
            entity.Property(e => e.IdUsuarioCreacion).HasColumnName("id_usuario_creacion").IsRequired();
            entity.Property(e => e.IdUsuarioCierre).HasColumnName("id_usuario_cierre").IsRequired(false);
            entity.Property(e => e.FechaCreacion)
                .HasColumnName("fecha_creacion")
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .IsRequired();
            entity.Property(e => e.FechaCierre).HasColumnName("fecha_cierre").IsRequired(false);
            entity.Property(e => e.Activo).HasColumnName("activo").HasDefaultValue(true).IsRequired();

            entity.HasOne(e => e.UsuarioCreacion)
                .WithMany(e => e.GruposCreados)
                .HasForeignKey(e => e.IdUsuarioCreacion)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.UsuarioCierre)
                .WithMany(e => e.GruposCerrados)
                .HasForeignKey(e => e.IdUsuarioCierre)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GrupoMesaMiembro>(entity =>
        {
            entity.ToTable("grupo_mesa_miembro");
            entity.HasKey(e => new { e.IdGrupo, e.IdMesa });
            entity.Property(e => e.IdGrupo).HasColumnName("id_grupo");
            entity.Property(e => e.IdMesa).HasColumnName("id_mesa");
            entity.Property(e => e.FechaAsignacion)
                .HasColumnName("fecha_asignacion")
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .IsRequired();

            entity.HasOne(e => e.Grupo)
                .WithMany(e => e.Miembros)
                .HasForeignKey(e => e.IdGrupo)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Mesa)
                .WithMany(e => e.HistorialGrupos)
                .HasForeignKey(e => e.IdMesa)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Pedido>(entity =>
        {
            entity.ToTable("pedido", table =>
            {
                table.HasCheckConstraint(
                    "CK_pedido_destino_xor",
                    "(id_mesa IS NULL) <> (id_grupo IS NULL)");
                table.HasCheckConstraint(
                    "CK_pedido_estado",
                    "estado IN ('Abierto', 'En Preparación', 'Listo', 'Pagado', 'Cancelado')");
                table.HasCheckConstraint(
                    "CK_pedido_montos",
                    "subtotal >= 0 AND impuestos >= 0 AND total >= 0 AND total = subtotal + impuestos");
            });
            entity.HasKey(e => e.IdPedido);
            entity.Property(e => e.IdPedido).HasColumnName("id_pedido").ValueGeneratedOnAdd();
            entity.Property(e => e.IdMesa).HasColumnName("id_mesa").IsRequired(false);
            entity.Property(e => e.IdGrupo).HasColumnName("id_grupo").IsRequired(false);
            entity.Property(e => e.IdUsuario).HasColumnName("id_usuario").IsRequired();
            entity.Property(e => e.FechaHora).HasColumnName("fecha_hora").IsRequired();
            entity.Property(e => e.Subtotal).HasColumnName("subtotal").HasPrecision(12, 2).IsRequired();
            entity.Property(e => e.Impuestos).HasColumnName("impuestos").HasPrecision(12, 2).IsRequired();
            entity.Property(e => e.Total).HasColumnName("total").HasPrecision(12, 2).IsRequired();
            entity.Property(e => e.Estado).HasColumnName("estado").HasMaxLength(20).IsRequired();

            entity.HasIndex(e => e.IdMesa)
                .IsUnique()
                .HasFilter("id_mesa IS NOT NULL AND estado IN ('Abierto', 'En Preparación', 'Listo')");
            entity.HasIndex(e => e.IdGrupo)
                .IsUnique()
                .HasFilter("id_grupo IS NOT NULL AND estado IN ('Abierto', 'En Preparación', 'Listo')");

            entity.HasOne(e => e.Mesa)
                .WithMany(e => e.Pedidos)
                .HasForeignKey(e => e.IdMesa)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Grupo)
                .WithMany(e => e.Pedidos)
                .HasForeignKey(e => e.IdGrupo)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Usuario)
                .WithMany(e => e.Pedidos)
                .HasForeignKey(e => e.IdUsuario)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Seed data: 15 mesas
        var mesas = new List<Mesa>();
        for (int i = 1; i <= 15; i++)
        {
            mesas.Add(new Mesa
            {
                IdMesa = i,
                Numero = i,
                Capacidad = 4,
                Estado = "Libre",
                IdGrupo = null
            });
        }
        modelBuilder.Entity<Mesa>().HasData(mesas);

    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        NormalizeUserIdentities();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        NormalizeUserIdentities();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void NormalizeUserIdentities()
    {
        foreach (var entry in ChangeTracker.Entries<Usuario>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            UserIdentity.NormalizeAndValidate(entry.Entity);
        }
    }
}
