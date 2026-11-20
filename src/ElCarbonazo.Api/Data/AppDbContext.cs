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
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Comanda> Comandas => Set<Comanda>();
    public DbSet<DetallePedido> DetallesPedido => Set<DetallePedido>();
    public DbSet<EnvioPedido> EnviosPedido => Set<EnvioPedido>();
    public DbSet<SesionUsuario> SesionesUsuario => Set<SesionUsuario>();
    public DbSet<TokenRefreshSesion> TokensRefreshSesion => Set<TokenRefreshSesion>();
    public DbSet<EventoAuditoria> EventosAuditoria => Set<EventoAuditoria>();
    public DbSet<TurnoCaja> TurnosCaja => Set<TurnoCaja>();
    public DbSet<Cobro> Cobros => Set<Cobro>();
    public DbSet<ComprobanteCobroLinea> ComprobantesCobroLineas => Set<ComprobanteCobroLinea>();
    public DbSet<OperacionCaja> OperacionesCaja => Set<OperacionCaja>();
    public DbSet<Ingrediente> Ingredientes => Set<Ingrediente>();
    public DbSet<Receta> Recetas => Set<Receta>();
    public DbSet<DetalleReceta> DetallesReceta => Set<DetalleReceta>();
    public DbSet<MovimientoInventario> MovimientosInventario => Set<MovimientoInventario>();
    public DbSet<ConsumoDetalle> ConsumosDetalle => Set<ConsumoDetalle>();
    public DbSet<AnulacionPedido> AnulacionesPedido => Set<AnulacionPedido>();
    public DbSet<OperacionAnulacion> OperacionesAnulacion => Set<OperacionAnulacion>();
    public DbSet<AutorizacionAnulacion> AutorizacionesAnulacion => Set<AutorizacionAnulacion>();
    public DbSet<AlertaAnulacion> AlertasAnulacion => Set<AlertaAnulacion>();
    public DbSet<LimiteAutorizacion> LimitesAutorizacion => Set<LimiteAutorizacion>();
    public DbSet<Reservacion> Reservaciones => Set<Reservacion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ConfigureInventario();
        modelBuilder.ConfigureCancelaciones();
        modelBuilder.ConfigureReservaciones();

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
            entity.Property(e => e.PermitePedidoTrasAnulacion).HasColumnName("permite_pedido_tras_anulacion").HasDefaultValue(false);
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
            entity.Property(e => e.PermitePedidoTrasAnulacion).HasColumnName("permite_pedido_tras_anulacion").HasDefaultValue(false);

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

        modelBuilder.Entity<Producto>(entity =>
        {
            entity.ToTable("producto", table =>
            {
                table.HasCheckConstraint(
                    "CK_producto_nombre",
                    "char_length(nombre) BETWEEN 1 AND 120 AND nombre = normalize(nombre, NFC)");
                table.HasCheckConstraint(
                    "CK_producto_categoria",
                    "categoria IN ('Comida', 'Bebida', 'Licor')");
                table.HasCheckConstraint(
                    "CK_producto_precio",
                    "precio BETWEEN 0.01 AND 999999.99");
                table.HasCheckConstraint("CK_producto_version", "version > 0");
            });
            entity.HasKey(producto => producto.IdProducto);
            entity.Property(producto => producto.IdProducto)
                .HasColumnName("id_producto")
                .ValueGeneratedOnAdd();
            entity.Property(producto => producto.Nombre)
                .HasColumnName("nombre")
                .HasMaxLength(120)
                .IsRequired();
            entity.Property(producto => producto.Categoria)
                .HasColumnName("categoria")
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(producto => producto.Precio)
                .HasColumnName("precio")
                .HasPrecision(8, 2)
                .IsRequired();
            entity.Property(producto => producto.Disponible)
                .HasColumnName("disponible")
                .IsRequired();
            entity.Property(producto => producto.Version)
                .HasColumnName("version")
                .HasDefaultValue(1)
                .IsRequired();
            entity.HasIndex(producto => new
            {
                producto.Disponible,
                producto.Categoria,
                producto.Nombre,
                producto.IdProducto
            })
                .HasDatabaseName("IX_producto_disponible_categoria_nombre_id_producto");
        });

        modelBuilder.Entity<Comanda>(entity =>
        {
            entity.ToTable("comanda", table =>
            {
                table.HasCheckConstraint(
                    "CK_comanda_estado",
                    "estado IN ('Abierta', 'En Preparación', 'Lista', 'Cancelada')");
                table.HasCheckConstraint("CK_comanda_revision_detalles", "revision_detalles > 0");
            });
            entity.HasKey(comanda => comanda.IdComanda);
            entity.Property(comanda => comanda.IdComanda)
                .HasColumnName("id_comanda")
                .ValueGeneratedOnAdd();
            entity.Property(comanda => comanda.IdPedido)
                .HasColumnName("id_pedido")
                .IsRequired();
            entity.Property(comanda => comanda.IdUsuario)
                .HasColumnName("id_usuario")
                .IsRequired();
            entity.Property(comanda => comanda.FechaHora)
                .HasColumnName("fecha_hora")
                .IsRequired();
            entity.Property(comanda => comanda.Estado)
                .HasColumnName("estado")
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(comanda => comanda.Version)
                .IsRowVersion();
            entity.Property(comanda => comanda.RevisionDetalles).HasColumnName("revision_detalles").HasDefaultValue(1L);
            entity.HasIndex(comanda => new { comanda.Estado, comanda.FechaHora, comanda.IdComanda })
                .HasDatabaseName("IX_comanda_estado_fecha_hora_id_comanda");
            entity.HasIndex(comanda => new { comanda.IdPedido, comanda.IdComanda })
                .HasDatabaseName("IX_comanda_id_pedido_id_comanda");
            entity.HasOne(comanda => comanda.Pedido)
                .WithMany(pedido => pedido.Comandas)
                .HasForeignKey(comanda => comanda.IdPedido)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(comanda => comanda.Usuario)
                .WithMany()
                .HasForeignKey(comanda => comanda.IdUsuario)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DetallePedido>(entity =>
        {
            entity.ToTable("detalle_pedido", table =>
            {
                table.HasCheckConstraint(
                    "CK_detalle_pedido_nombre_producto",
                    "char_length(nombre_producto) BETWEEN 1 AND 120 AND nombre_producto = normalize(nombre_producto, NFC)");
                table.HasCheckConstraint(
                    "CK_detalle_pedido_cantidad",
                    "cantidad BETWEEN 1 AND 99");
                table.HasCheckConstraint(
                    "CK_detalle_pedido_precio_unitario",
                    "precio_unitario BETWEEN 0.01 AND 999999.99");
                table.HasCheckConstraint(
                    "CK_detalle_pedido_subtotal",
                    "subtotal >= 0 AND subtotal = cantidad * precio_unitario");
                table.HasCheckConstraint(
                    "CK_detalle_pedido_notas",
                    "char_length(notas) <= 150 AND notas = normalize(notas, NFC)");
            });
            entity.HasKey(detalle => detalle.IdDetalle);
            entity.Property(detalle => detalle.Cancelado).HasColumnName("cancelado").HasDefaultValue(false);
            entity.Property(detalle => detalle.IdDetalle)
                .HasColumnName("id_detalle")
                .ValueGeneratedOnAdd();
            entity.Property(detalle => detalle.IdComanda)
                .HasColumnName("id_comanda")
                .IsRequired();
            entity.Property(detalle => detalle.IdProducto)
                .HasColumnName("id_producto")
                .IsRequired();
            entity.Property(detalle => detalle.NombreProducto)
                .HasColumnName("nombre_producto")
                .HasMaxLength(120)
                .IsRequired();
            entity.Property(detalle => detalle.Cantidad)
                .HasColumnName("cantidad")
                .IsRequired();
            entity.Property(detalle => detalle.PrecioUnitario)
                .HasColumnName("precio_unitario")
                .HasPrecision(8, 2)
                .IsRequired();
            entity.Property(detalle => detalle.Subtotal)
                .HasColumnName("subtotal")
                .HasPrecision(12, 2)
                .IsRequired();
            entity.Property(detalle => detalle.Notas)
                .HasColumnName("notas")
                .HasMaxLength(150)
                .HasDefaultValue(string.Empty)
                .IsRequired();
            entity.HasIndex(detalle => new { detalle.IdComanda, detalle.IdDetalle })
                .HasDatabaseName("IX_detalle_pedido_id_comanda_id_detalle");
            entity.HasOne(detalle => detalle.Comanda)
                .WithMany(comanda => comanda.Detalles)
                .HasForeignKey(detalle => detalle.IdComanda)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(detalle => detalle.Producto)
                .WithMany(producto => producto.DetallesPedido)
                .HasForeignKey(detalle => detalle.IdProducto)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EnvioPedido>(entity =>
        {
            entity.ToTable("envio_pedido", table =>
            {
                table.HasCheckConstraint(
                    "CK_envio_pedido_hash_solicitud",
                    "hash_solicitud ~ '^[0-9a-f]{64}$'");
                table.HasCheckConstraint(
                    "CK_envio_pedido_status_code",
                    "status_code = 201");
                table.HasCheckConstraint(
                    "CK_envio_pedido_response_json",
                    "jsonb_typeof(response_json) = 'object'");
            });
            entity.HasKey(envio => envio.EnvioId);
            entity.Property(envio => envio.EnvioId)
                .HasColumnName("envio_id")
                .ValueGeneratedNever();
            entity.Property(envio => envio.IdUsuario)
                .HasColumnName("id_usuario")
                .IsRequired();
            entity.Property(envio => envio.HashSolicitud)
                .HasColumnName("hash_solicitud")
                .HasColumnType("character(64)")
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(envio => envio.IdComanda)
                .HasColumnName("id_comanda")
                .IsRequired();
            entity.Property(envio => envio.StatusCode)
                .HasColumnName("status_code")
                .IsRequired();
            entity.Property(envio => envio.Location)
                .HasColumnName("location")
                .HasMaxLength(200)
                .IsRequired();
            entity.Property(envio => envio.ResponseJson)
                .HasColumnName("response_json")
                .HasColumnType("jsonb")
                .IsRequired();
            entity.Property(envio => envio.FechaHora)
                .HasColumnName("fecha_hora")
                .IsRequired();
            entity.HasIndex(envio => envio.IdComanda)
                .IsUnique()
                .HasDatabaseName("IX_envio_pedido_id_comanda");
            entity.HasOne(envio => envio.Usuario)
                .WithMany()
                .HasForeignKey(envio => envio.IdUsuario)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(envio => envio.Comanda)
                .WithOne(comanda => comanda.Envio)
                .HasForeignKey<EnvioPedido>(envio => envio.IdComanda)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OperacionCaja>(entity =>
        {
            entity.ToTable("operacion_caja", table =>
            {
                table.HasCheckConstraint(
                    "CK_operacion_caja_estado",
                    "estado IN ('Preparada', 'Completada', 'Rechazada', 'Cancelada')");
                table.HasCheckConstraint(
                    "CK_operacion_caja_payload",
                    "((tipo IS NULL AND version_contrato IS NULL AND argumentos IS NULL AND hash_argumentos IS NULL AND estado = 'Cancelada') OR " +
                    "(tipo IS NOT NULL AND tipo IN ('AbrirTurno', 'CobrarCuenta', 'CerrarTurno') AND version_contrato IS NOT NULL AND version_contrato = 1 AND " +
                    "argumentos IS NOT NULL AND jsonb_typeof(argumentos) = 'object' AND " +
                    "hash_argumentos IS NOT NULL AND hash_argumentos ~ '^[0-9a-f]{64}$'))");
                table.HasCheckConstraint(
                    "CK_operacion_caja_resultado",
                    "((estado = 'Preparada' AND fecha_terminal IS NULL AND status_resultado IS NULL AND codigo_resultado IS NULL AND resultado_json IS NULL) OR " +
                    "(estado <> 'Preparada' AND fecha_terminal IS NOT NULL AND status_resultado IS NOT NULL AND status_resultado BETWEEN 200 AND 499 AND " +
                    "codigo_resultado IS NOT NULL AND resultado_json IS NOT NULL AND jsonb_typeof(resultado_json) = 'object'))");
                table.HasCheckConstraint(
                    "CK_operacion_caja_cancelacion",
                    "((estado = 'Cancelada' AND id_usuario_cancelacion IS NOT NULL AND fecha_cancelacion IS NOT NULL) OR " +
                    "(estado <> 'Cancelada' AND id_usuario_cancelacion IS NULL AND fecha_cancelacion IS NULL))");
            });
            entity.HasKey(operation => operation.IdOperacion);
            entity.Property(operation => operation.IdOperacion).HasColumnName("id_operacion").ValueGeneratedNever();
            entity.Property(operation => operation.IdUsuarioPropietario).HasColumnName("id_usuario_propietario").IsRequired();
            entity.Property(operation => operation.Tipo).HasColumnName("tipo").HasMaxLength(30).IsRequired(false);
            entity.Property(operation => operation.VersionContrato).HasColumnName("version_contrato").IsRequired(false);
            entity.Property(operation => operation.Argumentos).HasColumnName("argumentos").HasColumnType("jsonb").IsRequired(false);
            entity.Property(operation => operation.HashArgumentos).HasColumnName("hash_argumentos").HasColumnType("character(64)").HasMaxLength(64).IsRequired(false);
            entity.Property(operation => operation.Estado).HasColumnName("estado").HasMaxLength(20).IsRequired();
            entity.Property(operation => operation.FechaCreacion).HasColumnName("fecha_creacion").IsRequired();
            entity.Property(operation => operation.FechaTerminal).HasColumnName("fecha_terminal").IsRequired(false);
            entity.Property(operation => operation.StatusResultado).HasColumnName("status_resultado").IsRequired(false);
            entity.Property(operation => operation.CodigoResultado).HasColumnName("codigo_resultado").HasMaxLength(80).IsRequired(false);
            entity.Property(operation => operation.ResultadoJson).HasColumnName("resultado_json").HasColumnType("jsonb").IsRequired(false);
            entity.Property(operation => operation.IdUsuarioCancelacion).HasColumnName("id_usuario_cancelacion").IsRequired(false);
            entity.Property(operation => operation.FechaCancelacion).HasColumnName("fecha_cancelacion").IsRequired(false);
            entity.HasIndex(operation => new { operation.IdUsuarioPropietario, operation.Estado, operation.FechaCreacion })
                .HasDatabaseName("IX_operacion_caja_propietario_estado_fecha");
            entity.HasOne(operation => operation.UsuarioPropietario).WithMany()
                .HasForeignKey(operation => operation.IdUsuarioPropietario).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(operation => operation.UsuarioCancelacion).WithMany()
                .HasForeignKey(operation => operation.IdUsuarioCancelacion)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TurnoCaja>(entity =>
        {
            entity.ToTable("turno_caja", table =>
            {
                table.HasCheckConstraint("CK_turno_caja_estado", "estado IN ('Abierto', 'Cerrado')");
                table.HasCheckConstraint("CK_turno_caja_fondo", "fondo_inicial >= 0");
                table.HasCheckConstraint(
                    "CK_turno_caja_cierre",
                    "((estado = 'Abierto' AND fecha_cierre IS NULL AND id_usuario_cierre IS NULL AND id_operacion_cierre IS NULL AND " +
                    "efectivo_esperado IS NULL AND efectivo_contado IS NULL AND diferencia IS NULL AND total_efectivo IS NULL AND " +
                    "total_tarjeta IS NULL AND total_transferencia IS NULL AND cantidad_cuentas_pendientes IS NULL AND motivo_diferencia IS NULL) OR " +
                    "(estado = 'Cerrado' AND fecha_cierre IS NOT NULL AND id_usuario_cierre IS NOT NULL AND id_operacion_cierre IS NOT NULL AND " +
                    "efectivo_esperado IS NOT NULL AND efectivo_esperado >= 0 AND efectivo_contado IS NOT NULL AND efectivo_contado >= 0 AND " +
                    "diferencia IS NOT NULL AND diferencia = efectivo_contado - efectivo_esperado AND " +
                    "total_efectivo IS NOT NULL AND total_efectivo >= 0 AND total_tarjeta IS NOT NULL AND total_tarjeta >= 0 AND " +
                    "fecha_cierre >= fecha_apertura AND total_transferencia IS NOT NULL AND total_transferencia >= 0 AND cantidad_cuentas_pendientes IS NOT NULL AND " +
                    "cantidad_cuentas_pendientes >= 0 AND " +
                    "((diferencia = 0 AND motivo_diferencia IS NULL) OR (diferencia <> 0 AND motivo_diferencia IS NOT NULL AND length(btrim(motivo_diferencia)) > 0))))");
                table.HasCheckConstraint(
                    "CK_turno_caja_motivo_diferencia",
                    "motivo_diferencia IS NULL OR (char_length(motivo_diferencia) <= 500 AND motivo_diferencia = normalize(motivo_diferencia, NFC) AND motivo_diferencia !~ '[[:cntrl:]]')");
            });
            entity.HasKey(shift => shift.IdTurnoCaja);
            entity.Property(shift => shift.IdTurnoCaja).HasColumnName("id_turno_caja").ValueGeneratedOnAdd();
            entity.Property(shift => shift.Estado).HasColumnName("estado").HasMaxLength(20).IsRequired();
            entity.Property(shift => shift.FechaApertura).HasColumnName("fecha_apertura").IsRequired();
            entity.Property(shift => shift.IdUsuarioApertura).HasColumnName("id_usuario_apertura").IsRequired();
            entity.Property(shift => shift.FondoInicial).HasColumnName("fondo_inicial").HasPrecision(12, 2).IsRequired();
            entity.Property(shift => shift.IdOperacionApertura).HasColumnName("id_operacion_apertura").IsRequired();
            entity.Property(shift => shift.FechaCierre).HasColumnName("fecha_cierre").IsRequired(false);
            entity.Property(shift => shift.IdUsuarioCierre).HasColumnName("id_usuario_cierre").IsRequired(false);
            entity.Property(shift => shift.IdOperacionCierre).HasColumnName("id_operacion_cierre").IsRequired(false);
            entity.Property(shift => shift.EfectivoEsperado).HasColumnName("efectivo_esperado").HasPrecision(12, 2).IsRequired(false);
            entity.Property(shift => shift.EfectivoContado).HasColumnName("efectivo_contado").HasPrecision(12, 2).IsRequired(false);
            entity.Property(shift => shift.Diferencia).HasColumnName("diferencia").HasPrecision(12, 2).IsRequired(false);
            entity.Property(shift => shift.TotalEfectivo).HasColumnName("total_efectivo").HasPrecision(12, 2).IsRequired(false);
            entity.Property(shift => shift.TotalTarjeta).HasColumnName("total_tarjeta").HasPrecision(12, 2).IsRequired(false);
            entity.Property(shift => shift.TotalTransferencia).HasColumnName("total_transferencia").HasPrecision(12, 2).IsRequired(false);
            entity.Property(shift => shift.CantidadCuentasPendientes).HasColumnName("cantidad_cuentas_pendientes").IsRequired(false);
            entity.Property(shift => shift.MotivoDiferencia).HasColumnName("motivo_diferencia").HasMaxLength(500).IsRequired(false);
            entity.HasIndex(shift => shift.Estado).IsUnique().HasFilter("estado = 'Abierto'")
                .HasDatabaseName("IX_turno_caja_unico_abierto");
            entity.HasIndex(shift => shift.FechaApertura).HasDatabaseName("IX_turno_caja_fecha_apertura");
            entity.HasIndex(shift => shift.IdOperacionApertura).IsUnique()
                .HasDatabaseName("IX_turno_caja_operacion_apertura");
            entity.HasIndex(shift => shift.IdOperacionCierre).IsUnique()
                .HasDatabaseName("IX_turno_caja_operacion_cierre");
            entity.HasOne(shift => shift.UsuarioApertura).WithMany()
                .HasForeignKey(shift => shift.IdUsuarioApertura)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(shift => shift.UsuarioCierre).WithMany()
                .HasForeignKey(shift => shift.IdUsuarioCierre)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(shift => shift.OperacionApertura).WithMany(operation => operation.TurnosApertura)
                .HasForeignKey(shift => shift.IdOperacionApertura).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(shift => shift.OperacionCierre).WithMany(operation => operation.TurnosCierre)
                .HasForeignKey(shift => shift.IdOperacionCierre).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Cobro>(entity =>
        {
            entity.ToTable("cobro", table =>
            {
                table.HasCheckConstraint(
                    "CK_cobro_montos",
                    "subtotal > 0 AND impuestos = 0 AND total = subtotal AND total > 0");
                table.HasCheckConstraint(
                    "CK_cobro_metodo",
                    "((metodo_pago = 'Efectivo' AND efectivo_recibido IS NOT NULL AND vuelto IS NOT NULL AND " +
                    "efectivo_recibido >= total AND vuelto = efectivo_recibido - total AND " +
                    "referencia_externa IS NULL AND NOT verificacion_externa_confirmada AND NOT referencia_repetida_confirmada) OR " +
                    "(metodo_pago IN ('Tarjeta', 'Transferencia') AND efectivo_recibido IS NULL AND vuelto IS NULL AND " +
                    "referencia_externa IS NOT NULL AND length(btrim(referencia_externa)) > 0 AND verificacion_externa_confirmada))");
                table.HasCheckConstraint(
                    "CK_cobro_referencia",
                    "referencia_externa IS NULL OR (char_length(referencia_externa) BETWEEN 1 AND 100 AND " +
                    "referencia_externa = normalize(referencia_externa, NFC) AND referencia_externa = btrim(referencia_externa) AND " +
                    "referencia_externa !~ '[[:cntrl:]]')");
                table.HasCheckConstraint("CK_cobro_numero_comprobante", "numero_comprobante > 0");
            });
            entity.HasKey(payment => payment.IdCobro);
            entity.Property(payment => payment.IdCobro).HasColumnName("id_cobro").ValueGeneratedOnAdd();
            entity.Property(payment => payment.NumeroComprobante).HasColumnName("numero_comprobante").ValueGeneratedOnAdd();
            entity.Property(payment => payment.IdPedido).HasColumnName("id_pedido").IsRequired();
            entity.Property(payment => payment.IdTurnoCaja).HasColumnName("id_turno_caja").IsRequired();
            entity.Property(payment => payment.IdUsuario).HasColumnName("id_usuario").IsRequired();
            entity.Property(payment => payment.IdOperacion).HasColumnName("id_operacion").IsRequired();
            entity.Property(payment => payment.MetodoPago).HasColumnName("metodo_pago").HasMaxLength(20).IsRequired();
            entity.Property(payment => payment.Subtotal).HasColumnName("subtotal").HasPrecision(12, 2).IsRequired();
            entity.Property(payment => payment.Impuestos).HasColumnName("impuestos").HasPrecision(12, 2).IsRequired();
            entity.Property(payment => payment.Total).HasColumnName("total").HasPrecision(12, 2).IsRequired();
            entity.Property(payment => payment.EfectivoRecibido).HasColumnName("efectivo_recibido").HasPrecision(12, 2).IsRequired(false);
            entity.Property(payment => payment.Vuelto).HasColumnName("vuelto").HasPrecision(12, 2).IsRequired(false);
            entity.Property(payment => payment.ReferenciaExterna).HasColumnName("referencia_externa").HasMaxLength(100).IsRequired(false);
            entity.Property(payment => payment.VerificacionExternaConfirmada).HasColumnName("verificacion_externa_confirmada").IsRequired();
            entity.Property(payment => payment.ReferenciaRepetidaConfirmada).HasColumnName("referencia_repetida_confirmada").IsRequired();
            entity.Property(payment => payment.FechaHora).HasColumnName("fecha_hora").IsRequired();
            entity.HasIndex(payment => payment.NumeroComprobante).IsUnique()
                .HasDatabaseName("IX_cobro_numero_comprobante");
            entity.HasIndex(payment => payment.IdPedido).IsUnique()
                .HasDatabaseName("IX_cobro_id_pedido");
            entity.HasIndex(payment => new { payment.IdTurnoCaja, payment.FechaHora, payment.IdCobro })
                .HasDatabaseName("IX_cobro_turno_fecha_id");
            entity.HasIndex(payment => new { payment.MetodoPago, payment.ReferenciaExterna })
                .HasDatabaseName("IX_cobro_metodo_referencia");
            entity.HasIndex(payment => payment.IdOperacion).IsUnique()
                .HasDatabaseName("IX_cobro_id_operacion");
            entity.HasOne(payment => payment.Pedido).WithMany().HasForeignKey(payment => payment.IdPedido)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(payment => payment.TurnoCaja).WithMany(shift => shift.Cobros)
                .HasForeignKey(payment => payment.IdTurnoCaja).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(payment => payment.Usuario).WithMany().HasForeignKey(payment => payment.IdUsuario)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(payment => payment.Operacion).WithOne(operation => operation.Cobro)
                .HasForeignKey<Cobro>(payment => payment.IdOperacion).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ComprobanteCobroLinea>(entity =>
        {
            entity.ToTable("comprobante_cobro_linea", table =>
            {
                table.HasCheckConstraint("CK_comprobante_cobro_linea_orden", "orden > 0");
                table.HasCheckConstraint("CK_comprobante_cobro_linea_detalle", "id_detalle_origen > 0");
                table.HasCheckConstraint(
                    "CK_comprobante_cobro_linea_nombre",
                    "char_length(nombre_producto) BETWEEN 1 AND 120 AND nombre_producto = normalize(nombre_producto, NFC)");
                table.HasCheckConstraint("CK_comprobante_cobro_linea_cantidad", "cantidad BETWEEN 1 AND 99");
                table.HasCheckConstraint(
                    "CK_comprobante_cobro_linea_importes",
                    "precio_unitario BETWEEN 0.01 AND 999999.99 AND subtotal = cantidad * precio_unitario");
            });
            entity.HasKey(line => line.IdComprobanteCobroLinea);
            entity.Property(line => line.IdComprobanteCobroLinea).HasColumnName("id_comprobante_cobro_linea").ValueGeneratedOnAdd();
            entity.Property(line => line.IdCobro).HasColumnName("id_cobro").IsRequired();
            entity.Property(line => line.Orden).HasColumnName("orden").IsRequired();
            entity.Property(line => line.IdDetalleOrigen).HasColumnName("id_detalle_origen").IsRequired();
            entity.Property(line => line.NombreProducto).HasColumnName("nombre_producto").HasMaxLength(120).IsRequired();
            entity.Property(line => line.Cantidad).HasColumnName("cantidad").IsRequired();
            entity.Property(line => line.PrecioUnitario).HasColumnName("precio_unitario").HasPrecision(8, 2).IsRequired();
            entity.Property(line => line.Subtotal).HasColumnName("subtotal").HasPrecision(12, 2).IsRequired();
            entity.HasIndex(line => new { line.IdCobro, line.Orden }).IsUnique()
                .HasDatabaseName("IX_comprobante_cobro_linea_cobro_orden");
            entity.HasIndex(line => new { line.IdCobro, line.IdDetalleOrigen }).IsUnique()
                .HasDatabaseName("IX_comprobante_cobro_linea_cobro_detalle");
            entity.HasOne(line => line.Cobro).WithMany(payment => payment.Lineas).HasForeignKey(line => line.IdCobro)
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
