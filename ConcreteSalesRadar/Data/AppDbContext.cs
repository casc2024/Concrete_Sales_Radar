using ConcreteSalesRadar.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ConcreteSalesRadar.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Rol> Roles => Set<Rol>();
    public DbSet<Membresia> Membresias => Set<Membresia>();
    public DbSet<CodigoVerificacion> CodigosVerificacion => Set<CodigoVerificacion>();
    public DbSet<Proyecto> Proyectos => Set<Proyecto>();
    public DbSet<Pago> Pagos => Set<Pago>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Usuario>(e =>
        {
            e.ToTable("usuarios");
            e.HasIndex(u => u.Correo).IsUnique();
            e.HasOne(u => u.Rol)
                .WithMany(r => r.Usuarios)
                .HasForeignKey(u => u.RolId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(u => u.Membresia)
                .WithMany(m => m.Usuarios)
                .HasForeignKey(u => u.MembresiaId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Rol>(e =>
        {
            e.ToTable("roles");
            e.HasIndex(r => r.Nombre).IsUnique();
        });

        modelBuilder.Entity<Membresia>(e =>
        {
            e.ToTable("membresias");
            e.Property(m => m.PrecioMensual).HasColumnType("numeric(12,2)");
        });

        modelBuilder.Entity<CodigoVerificacion>(e =>
        {
            e.ToTable("codigos_verificacion");
            e.HasOne(c => c.Usuario)
                .WithMany(u => u.Codigos)
                .HasForeignKey(c => c.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => new { c.UsuarioId, c.Tipo });
        });

        modelBuilder.Entity<Pago>(e =>
        {
            e.ToTable("pagos");
            e.Property(p => p.Monto).HasColumnType("numeric(12,2)");
            e.HasIndex(p => p.StripeSessionId).IsUnique();
            e.HasOne(p => p.Usuario)
                .WithMany(u => u.Pagos)
                .HasForeignKey(p => p.UsuarioId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Membresia)
                .WithMany()
                .HasForeignKey(p => p.MembresiaId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Proyecto>(e =>
        {
            e.ToTable("proyectos");
            e.Property(p => p.Presupuesto).HasColumnType("numeric(14,2)");
            // FechaInicio es una fecha de calendario (sin zona horaria); el resto de
            // fechas son "momentos" en UTC y usan timestamptz por convención de Npgsql.
            e.Property(p => p.FechaInicio).HasColumnType("timestamp without time zone");
            e.HasOne(p => p.Usuario)
                .WithMany(u => u.Proyectos)
                .HasForeignKey(p => p.UsuarioId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
