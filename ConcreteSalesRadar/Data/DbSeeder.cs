using ConcreteSalesRadar.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ConcreteSalesRadar.Data;

/// <summary>
/// Aplica migraciones pendientes y carga datos iniciales: roles, planes de
/// membresía, usuario administrador y obras de ejemplo.
/// </summary>
public static class DbSeeder
{
    public static async Task InicializarAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.MigrateAsync();

        await SeedRolesAsync(db);
        await SeedMembresiasAsync(db);
        await SeedAdminAsync(db, config);
        await SeedUsuarioCascAsync(db);
        await SeedProyectosAsync(db);
    }

    /// <summary>
    /// Garantiza que casc2024@gmail.com sea Administrador. Si ya existe (p. ej. registrado
    /// desde la página) lo promueve a administrador sin tocar su contraseña ni sus datos;
    /// si no existe, lo crea con la contraseña por defecto "Casc2024!".
    /// </summary>
    private static async Task SeedUsuarioCascAsync(AppDbContext db)
    {
        const string correo = "casc2024@gmail.com";
        var rolAdmin = await db.Roles.FirstAsync(r => r.Nombre == Roles.Administrador);

        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Correo == correo);
        if (usuario is not null)
        {
            var cambio = false;
            if (usuario.RolId != rolAdmin.Id) { usuario.RolId = rolAdmin.Id; cambio = true; }
            if (!usuario.CorreoConfirmado) { usuario.CorreoConfirmado = true; cambio = true; }
            if (!usuario.Activo) { usuario.Activo = true; cambio = true; }
            if (usuario.MembresiaFin is null)
            {
                usuario.MembresiaInicio ??= DateTime.UtcNow;
                usuario.MembresiaFin = usuario.MembresiaInicio.Value.AddMonths(1);
                cambio = true;
            }
            if (cambio) await db.SaveChangesAsync();
            return;
        }

        var plan = await db.Membresias.FirstOrDefaultAsync(m => m.Nombre == "Professional");
        var hasher = new PasswordHasher<Usuario>();
        var nuevo = new Usuario
        {
            Nombre = "Christian",
            Apellido = "Schereiber",
            Compania = "Concrete Sales Radar",
            Correo = correo,
            Telefono = "000-000-0000",
            CorreoConfirmado = true,
            Activo = true,
            RolId = rolAdmin.Id,
            MembresiaId = plan?.Id,
            MembresiaInicio = DateTime.UtcNow,
            MembresiaFin = DateTime.UtcNow.AddMonths(1)
        };
        nuevo.PasswordHash = hasher.HashPassword(nuevo, "Casc2024!");

        db.Usuarios.Add(nuevo);
        await db.SaveChangesAsync();
    }

    private static async Task SeedRolesAsync(AppDbContext db)
    {
        if (await db.Roles.AnyAsync()) return;

        db.Roles.AddRange(
            new Rol { Nombre = Roles.Administrador, Descripcion = "Acceso total: mantenimiento de usuarios y membresías." },
            new Rol { Nombre = Roles.Usuario, Descripcion = "Acceso al dashboard de prospección de obras." }
        );
        await db.SaveChangesAsync();
    }

    private static async Task SeedMembresiasAsync(AppDbContext db)
    {
        if (await db.Membresias.AnyAsync()) return;

        db.Membresias.AddRange(
            new Membresia
            {
                Nombre = "Starter", PrecioMensual = 99m, MaxUsuarios = 1, Orden = 1,
                Descripcion = "Para empezar a prospectar obras.",
                Caracteristicas = "1 usuario\n1 territorio\nBúsqueda de proyectos\nFiltros básicos\nExportar 100 leads/mes"
            },
            new Membresia
            {
                Nombre = "Professional", PrecioMensual = 249m, MaxUsuarios = 3, Orden = 2, EsPopular = true,
                Descripcion = "El plan más popular para equipos pequeños.",
                Caracteristicas = "3 usuarios\nBúsquedas ilimitadas\nInformación de contacto completa\nPresupuestos y yardas\nExportar leads\nAlertas por correo\nPipeline de ventas (CRM)"
            },
            new Membresia
            {
                Nombre = "Business", PrecioMensual = 499m, MaxUsuarios = 10, Orden = 3,
                Descripcion = "Para equipos y múltiples plantas.",
                Caracteristicas = "10 usuarios\nMúltiples territorios/plantas\nFiltros avanzados\nExportar leads ilimitados\nPipeline de ventas (CRM)\nReportes personalizados\nSoporte prioritario"
            },
            new Membresia
            {
                Nombre = "Enterprise", PrecioMensual = 0m, MaxUsuarios = 9999, Orden = 4,
                Descripcion = "Solución a la medida. Contacta a ventas.",
                Caracteristicas = "Usuarios ilimitados\nTerritorios personalizados\nAcceso a API\nOpciones white label\nSoporte dedicado\nIntegración de datos a medida\nOnboarding y capacitación"
            }
        );
        await db.SaveChangesAsync();
    }

    private static async Task SeedAdminAsync(AppDbContext db, IConfiguration config)
    {
        var correoAdmin = config["Admin:Correo"] ?? "admin@concretesalesradar.com";
        if (await db.Usuarios.AnyAsync(u => u.Correo == correoAdmin)) return;

        var rolAdmin = await db.Roles.FirstAsync(r => r.Nombre == Roles.Administrador);
        var planBusiness = await db.Membresias.FirstOrDefaultAsync(m => m.Nombre == "Business");
        var hasher = new PasswordHasher<Usuario>();

        var admin = new Usuario
        {
            Nombre = "Administrador",
            Apellido = "Sistema",
            Compania = "Concrete Sales Radar",
            Correo = correoAdmin,
            Telefono = "000-000-0000",
            CorreoConfirmado = true,
            Activo = true,
            RolId = rolAdmin.Id,
            MembresiaId = planBusiness?.Id,
            MembresiaInicio = DateTime.UtcNow,
            MembresiaFin = DateTime.UtcNow.AddMonths(1)
        };
        admin.PasswordHash = hasher.HashPassword(admin, config["Admin:Password"] ?? "Admin123!");

        db.Usuarios.Add(admin);
        await db.SaveChangesAsync();
    }

    private static async Task SeedProyectosAsync(AppDbContext db)
    {
        if (await db.Proyectos.AnyAsync()) return;

        db.Proyectos.AddRange(
            new Proyecto { Direccion = "14700 Heritage Parkway, Fort Worth, TX 76177", NombreProyecto = "Colovore Alliance Data Center", Tipo = "Data Center", FechaInicio = new DateTime(2026, 12, 1), Presupuesto = 430000000m, Yardas = 10000, EmpresaContacto = "AIL Investment LP / Hillwood", NombreContacto = "Doug Johnson", Telefono = "817-224-6000", TelefonoEmpresa = "214-303-5535", Fuente = "TDLR TABS2027001701", Prioridad = 95, Nota = "156,500 SF data center + 42,000 SF equipment yard." },
            new Proyecto { Direccion = "Northlake, TX 76247", NombreProyecto = "Northlake 35 Logistics Park III - Building 7", Tipo = "Warehouse", FechaInicio = new DateTime(2026, 6, 22), Presupuesto = 46770000m, Yardas = 27000, EmpresaContacto = "Northlake 35 Logistics Park III Phase I, LLC", NombreContacto = "Ben Newell", Telefono = "713-855-0091", Fuente = "TDLR TABS2026020281", Prioridad = 96, Nota = "1,223,144 SF industrial building con grading, utilities y paving." },
            new Proyecto { Direccion = "Alliance Center North Block 3 Lot 2, Fort Worth, TX 76177", NombreProyecto = "ACN 6 - Alliance Center North", Tipo = "Warehouse", FechaInicio = new DateTime(2026, 4, 20), Presupuesto = 47000000m, Yardas = 23000, EmpresaContacto = "Alliance Center North No. 6 Ltd / Hillwood", NombreContacto = "Jesse Carrasco", Telefono = "817-224-6082", TelefonoEmpresa = "214-303-5535", Fuente = "TDLR TABS2026017749", Prioridad = 94, Nota = "1,009,280 SF warehouse shell." },
            new Proyecto { Direccion = "220 Eagle Parkway, Fort Worth, TX 76177", NombreProyecto = "Alliance Embraer MRO", Tipo = "Industrial", FechaInicio = new DateTime(2026, 3, 15), Presupuesto = 55300000m, Yardas = 8000, EmpresaContacto = "Alliance Center No. 16 Ltd / Hillwood", NombreContacto = "Doug Johnson", Telefono = "817-224-6000", TelefonoEmpresa = "214-303-5535", Fuente = "TDLR TABS2026013049", Prioridad = 86, Nota = "156,641 SF concrete tilt-up hangar/MRO facility." },
            new Proyecto { Direccion = "McNabb Drive, Celina, TX 75009", NombreProyecto = "Sage Homes Celina", Tipo = "Multifamily", FechaInicio = new DateTime(2026, 7, 13), Presupuesto = 47672154m, Yardas = 7500, EmpresaContacto = "Sage Apartment Development", NombreContacto = "Justin Nowell", Telefono = "817-475-8328", TelefonoEmpresa = "817-475-8328", Fuente = "TDLR TABS2026020282", Prioridad = 90, Nota = "295,971 SF apartment development." },
            new Proyecto { Direccion = "6150 Allred Road, Denton, TX 76226", NombreProyecto = "Bridle Ridge", Tipo = "Residential", FechaInicio = new DateTime(2026, 9, 1), Presupuesto = 16500000m, Yardas = 12000, EmpresaContacto = "Denton 65 Land LP", NombreContacto = "Justin Bono", Telefono = "214-519-1901", TelefonoEmpresa = "214-519-1901", Fuente = "TDLR TABS2026021603", Prioridad = 93, Nota = "~70 acres, 210 lotes residenciales." },
            new Proyecto { Direccion = "5116 Triadic Lane, Celina, TX 75078", NombreProyecto = "Mosaic Phase 4A", Tipo = "Residential", FechaInicio = new DateTime(2026, 10, 5), Presupuesto = 5867225m, Yardas = 10000, EmpresaContacto = "Tellus Texas I LLC / Tellus Group", NombreContacto = "Justin Craig", Email = "jcraig@tellusgroupllc.com", Telefono = "469-532-0689", TelefonoEmpresa = "469-532-0689", Fuente = "TDLR TABS2026027753 + public municipal filing", Prioridad = 95, Nota = "1,010,374 SF residential improvements." },
            new Proyecto { Direccion = "5116 Triadic Lane, Celina, TX 75078", NombreProyecto = "Mosaic Phase 5", Tipo = "Residential", FechaInicio = new DateTime(2026, 8, 20), Presupuesto = 7200000m, Yardas = 10000, EmpresaContacto = "Tellus Texas I LLC / Tellus Group", NombreContacto = "Justin Craig", Email = "jcraig@tellusgroupllc.com", Telefono = "469-532-0689", TelefonoEmpresa = "469-532-0689", Fuente = "TDLR TABS2026027746 + public municipal filing", Prioridad = 94, Nota = "1,053,455 SF residential construction." }
        );
        await db.SaveChangesAsync();
    }
}
