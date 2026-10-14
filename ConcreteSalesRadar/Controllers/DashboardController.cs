using System.Security.Claims;
using ConcreteSalesRadar.Data;
using ConcreteSalesRadar.Models.Entities;
using ConcreteSalesRadar.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ConcreteSalesRadar.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly AppDbContext _db;
    private readonly IStringLocalizer<SharedResource> _loc;

    public DashboardController(AppDbContext db, IStringLocalizer<SharedResource> loc)
    {
        _db = db;
        _loc = loc;
    }

    [HttpGet]
    public async Task<IActionResult> Index(DashboardViewModel filtro)
    {
        var proyectos = await _db.Proyectos.AsNoTracking().ToListAsync();

        IEnumerable<Proyecto> q = proyectos;

        var esAdmin = User.IsInRole(Roles.Administrador);

        // Regla de negocio (exhibición obligatoria): a un CLIENTE no se le muestra ningún
        // proyecto sin Fuente, Estado de verificación y Última verificación. El administrador
        // sí ve todo para poder completarlo/gestionarlo.
        if (!esAdmin)
            q = q.Where(p => !string.IsNullOrWhiteSpace(p.Fuente)
                && !string.IsNullOrWhiteSpace(p.EstadoVerificacion)
                && p.UltimaVerificacion.Year > 1);

        // Campos visibles según la membresía del usuario (el admin ve todos).
        if (esAdmin)
            filtro.CamposVisibles = new HashSet<string>(CamposProyecto.Claves, StringComparer.OrdinalIgnoreCase);
        else
        {
            var uid = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
            var csv = await _db.Usuarios.Where(u => u.Id == uid)
                .Select(u => u.Membresia != null ? u.Membresia.CamposVisibles : null)
                .FirstOrDefaultAsync();
            filtro.CamposVisibles = CamposProyecto.Conjunto(csv);
        }

        // Búsqueda por ubicación (área / ciudad / ZIP / sitio): empareja contra la dirección.
        // Se tokeniza para que "Farmers Branch", "75234" o "Dallas, TX" funcionen por separado
        // (deben aparecer TODOS los términos en la dirección).
        if (!string.IsNullOrWhiteSpace(filtro.Zona))
        {
            var tokens = filtro.Zona.ToLowerInvariant()
                .Split(new[] { ',', '/', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length >= 2)
                .ToArray();
            if (tokens.Length > 0)
                q = q.Where(p =>
                {
                    var dir = (p.Direccion ?? string.Empty).ToLowerInvariant();
                    return tokens.All(t => dir.Contains(t));
                });
        }

        if (!string.IsNullOrWhiteSpace(filtro.Busqueda))
        {
            var b = filtro.Busqueda.Trim().ToLowerInvariant();
            q = q.Where(p => string.Join(' ',
                p.Direccion, p.NombreProyecto, p.EmpresaContacto, p.NombreContacto,
                p.Email, p.Telefono, p.TelefonoEmpresa, p.Nota).ToLowerInvariant().Contains(b));
        }

        if (!string.IsNullOrWhiteSpace(filtro.Tipo))
            q = q.Where(p => p.Tipo == filtro.Tipo);

        if (filtro.PresupuestoMin > 0)
            q = q.Where(p => p.Presupuesto >= filtro.PresupuestoMin);

        if (!string.IsNullOrWhiteSpace(filtro.Antiguedad))
            q = q.Where(p => BucketAntiguedad(p.FechaInicio) == filtro.Antiguedad);

        if (!string.IsNullOrWhiteSpace(filtro.Contacto))
        {
            var quiereContacto = filtro.Contacto == "yes";
            q = q.Where(p => TieneContacto(p) == quiereContacto);
        }

        var lista = q.OrderByDescending(p => p.Prioridad).ToList();

        // KPIs y agregados sobre TODO el conjunto filtrado.
        filtro.TotalObras = lista.Count;
        filtro.PresupuestoTotal = lista.Sum(p => p.Presupuesto);
        filtro.YardasTotal = lista.Sum(p => p.Yardas);
        filtro.Calientes = lista.Count(p => p.Prioridad >= 75);
        filtro.ConContacto = lista.Count(TieneContacto);
        filtro.PorTipo = lista.GroupBy(p => p.Tipo ?? "-")
            .Select(g => (Tipo: g.Key, Cantidad: g.Count()))
            .OrderByDescending(x => x.Cantidad).ToList();
        filtro.PrioridadAlta = lista.Count(p => p.Prioridad >= 75);
        filtro.PrioridadMedia = lista.Count(p => p.Prioridad is >= 50 and < 75);
        filtro.PrioridadBaja = lista.Count(p => p.Prioridad < 50);

        // Paginación (10 por página) solo para la tabla.
        filtro.TotalRegistros = lista.Count;
        filtro.TotalPaginas = Math.Max(1, (int)Math.Ceiling(lista.Count / (double)DashboardViewModel.TamanoPagina));
        filtro.Pagina = Math.Clamp(filtro.Pagina <= 0 ? 1 : filtro.Pagina, 1, filtro.TotalPaginas);
        filtro.Proyectos = lista
            .Skip((filtro.Pagina - 1) * DashboardViewModel.TamanoPagina)
            .Take(DashboardViewModel.TamanoPagina)
            .ToList();

        return View(filtro);
    }

    // ---------------- Detalle (solo consulta) + conteo de vistas ----------------

    [HttpGet]
    public async Task<IActionResult> Detalle(int id)
    {
        var proyecto = await _db.Proyectos.FindAsync(id);
        if (proyecto is null) return NotFound();

        var esAdmin = User.IsInRole(Roles.Administrador);

        // Misma regla de exhibición: un cliente no ve proyectos incompletos.
        if (!esAdmin && (string.IsNullOrWhiteSpace(proyecto.Fuente)
            || string.IsNullOrWhiteSpace(proyecto.EstadoVerificacion)
            || proyecto.UltimaVerificacion.Year <= 1))
            return NotFound();

        // Registrar la vista: ÚNICA por usuario (abrir N veces = 1 vista). El índice único
        // (ProyectoId, UsuarioId) lo garantiza; el contador solo sube en la primera vez.
        if (UsuarioActualId() is int usuarioId)
        {
            var yaVista = await _db.VistasProyecto
                .AnyAsync(v => v.ProyectoId == id && v.UsuarioId == usuarioId);
            if (!yaVista)
            {
                _db.VistasProyecto.Add(new VistaProyecto
                {
                    ProyectoId = id,
                    UsuarioId = usuarioId,
                    FechaVista = DateTime.UtcNow
                });
                proyecto.NumeroVistas += 1;
                try { await _db.SaveChangesAsync(); }
                catch (DbUpdateException) { /* carrera: otra pestaña la registró primero */ }
            }
        }

        // Campos visibles según la membresía del usuario (el admin ve todos).
        HashSet<string> campos;
        if (esAdmin)
            campos = new HashSet<string>(CamposProyecto.Claves, StringComparer.OrdinalIgnoreCase);
        else
        {
            var uid = UsuarioActualId() ?? 0;
            var csv = await _db.Usuarios.Where(u => u.Id == uid)
                .Select(u => u.Membresia != null ? u.Membresia.CamposVisibles : null)
                .FirstOrDefaultAsync();
            campos = CamposProyecto.Conjunto(csv);
        }

        return View(new ProyectoDetalleViewModel { Proyecto = proyecto, CamposVisibles = campos });
    }

    // ---------------- Gestión (solo administrador) ----------------

    [HttpGet]
    [Authorize(Roles = Roles.Administrador)]
    public IActionResult Crear() => View("Editar", new Proyecto { Prioridad = 75 });

    [HttpGet]
    [Authorize(Roles = Roles.Administrador)]
    public async Task<IActionResult> Editar(int id)
    {
        var proyecto = await _db.Proyectos.FindAsync(id);
        if (proyecto is null) return NotFound();
        return View(proyecto);
    }

    [HttpPost]
    [Authorize(Roles = Roles.Administrador)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Guardar(Proyecto modelo)
    {
        if (!ModelState.IsValid)
            return View("Editar", modelo);

        if (modelo.Id == 0)
        {
            modelo.FechaCreacion = DateTime.UtcNow;
            modelo.UsuarioId = UsuarioActualId();
            _db.Proyectos.Add(modelo);
        }
        else
        {
            var existente = await _db.Proyectos.FindAsync(modelo.Id);
            if (existente is null) return NotFound();

            existente.Direccion = modelo.Direccion;
            existente.NombreProyecto = modelo.NombreProyecto;
            existente.Tipo = modelo.Tipo;
            existente.FechaInicio = modelo.FechaInicio;
            existente.Presupuesto = modelo.Presupuesto;
            existente.Yardas = modelo.Yardas;
            existente.EmpresaContacto = modelo.EmpresaContacto;
            existente.NombreContacto = modelo.NombreContacto;
            existente.Email = modelo.Email;
            existente.Telefono = modelo.Telefono;
            existente.TelefonoEmpresa = modelo.TelefonoEmpresa;
            existente.Fuente = modelo.Fuente;
            existente.Prioridad = modelo.Prioridad;
            existente.Nota = modelo.Nota;
        }

        await _db.SaveChangesAsync();
        TempData["Exito"] = _loc["Msg_ProjectSaved"].Value;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = Roles.Administrador)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Eliminar(int id)
    {
        var proyecto = await _db.Proyectos.FindAsync(id);
        if (proyecto is not null)
        {
            _db.Proyectos.Remove(proyecto);
            await _db.SaveChangesAsync();
            TempData["Exito"] = _loc["Msg_ProjectDeleted"].Value;
        }
        return RedirectToAction(nameof(Index));
    }

    // ---------------- Helpers ----------------

    private int? UsuarioActualId()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(id, out var v) ? v : null;
    }

    public static bool TieneContacto(Proyecto p) =>
        !string.IsNullOrWhiteSpace(p.Telefono) || !string.IsNullOrWhiteSpace(p.TelefonoEmpresa) ||
        !string.IsNullOrWhiteSpace(p.Email) || !string.IsNullOrWhiteSpace(p.NombreContacto) ||
        !string.IsNullOrWhiteSpace(p.EmpresaContacto);

    public static string BucketAntiguedad(DateTime? fecha)
    {
        if (fecha is null) return "";
        var meses = MesesDesde(fecha.Value);
        if (meses < 0) return "new";
        if (meses <= 3) return "new";
        if (meses <= 12) return "active";
        return "mature";
    }

    public static int MesesDesde(DateTime fecha)
    {
        var hoy = DateTime.UtcNow;
        return (hoy.Year - fecha.Year) * 12 + (hoy.Month - fecha.Month);
    }
}
