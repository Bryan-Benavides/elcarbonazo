using Microsoft.EntityFrameworkCore;
using ElCarbonazo.Api.Entities;

namespace ElCarbonazo.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Mesa> Mesas => Set<Mesa>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tabla usuario
        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.ToTable("usuario");
            entity.HasKey(e => e.IdUsuario);
            entity.Property(e => e.IdUsuario).HasColumnName("id_usuario").ValueGeneratedOnAdd();
            entity.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(100).IsRequired();
            entity.Property(e => e.ContrasenaHash).HasColumnName("contrasena_hash").HasMaxLength(255).IsRequired();
            entity.Property(e => e.Rol).HasColumnName("rol").HasMaxLength(50).IsRequired();
            entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
            entity.Property(e => e.Activo).HasColumnName("activo").HasDefaultValue(true).IsRequired();
        });

        // Tabla mesa
        modelBuilder.Entity<Mesa>(entity =>
        {
            entity.ToTable("mesa");
            entity.HasKey(e => e.IdMesa);
            entity.Property(e => e.IdMesa).HasColumnName("id_mesa").ValueGeneratedOnAdd();
            entity.Property(e => e.IdGrupo).HasColumnName("id_grupo").IsRequired(false);
            entity.Property(e => e.Numero).HasColumnName("numero").IsRequired();
            entity.Property(e => e.Capacidad).HasColumnName("capacidad").IsRequired();
            entity.Property(e => e.Estado).HasColumnName("estado").HasMaxLength(20).IsRequired();

            // Índice único para numero
            entity.HasIndex(e => e.Numero).IsUnique();
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

        // Seed data: 1 usuario administrador
        // Contraseña: "admin123" - Hash BCrypt generado con work factor 12
        var adminHash = "$2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/LewdBPj/RK.PZvO.S";
        
        modelBuilder.Entity<Usuario>().HasData(new Usuario
        {
            IdUsuario = 1,
            Nombre = "admin",
            Email = "admin@elcarbonazo.local",
            Rol = "Jefa",
            Activo = true,
            ContrasenaHash = adminHash
        });
    }
}