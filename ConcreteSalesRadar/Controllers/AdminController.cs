using ConcreteSalesRadar.Data;
using ConcreteSalesRadar.Models.Entities;
using ConcreteSalesRadar.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ConcreteSalesRadar.Controllers;

[Authorize(Roles = Roles.Administrador)]
public class AdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly PasswordHasher<Usuario> _hasher = new();

    public AdminController(AppDbContext db, IStringLocalizer<SharedResource> loc)
    {
        _db = db;
        _loc = loc;
    }

    public IActionResult Index() => View();

    // ================= USUARIOS =================

    [HttpGet]
    public async Task<IActionResult> Usuarios(AdminUsuariosViewModel filtro)
    {
        var q = _db.Usuarios
            .Include(u => u.Rol)
            .Include(u => u.Membresia)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filtro.Nombre))
            q = q.Where(u => EF.Functions.ILike(u.Nombre, $"%{filtro.Nombre.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(filtro.Apellido))
            q = q.Where(u => EF.Functions.ILike(u.Apellido, $"%{filtro.Apellido.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(filtro.Compania))
            q = q.Where(u => EF.Functions.ILike(u.Compania, $"%{filtro.Compania.Trim()}%"));

        // El rango de fechas se compara en UTC (la columna es timestamptz).
        if (filtro.FechaDesde.HasValue)
        {
            var desde = DateTime.SpecifyKind(filtro.FechaDesde.Value.Date, DateTimeKind.Utc);
            q = q.Where(u => u.FechaRegistro >= desde);
        }
        if (filtro.FechaHasta.HasValue)
        {
            var hasta = DateTime.SpecifyKind(filtro.FechaHasta.Value.Date.AddDays(1), DateTimeKind.Utc);
            q = q.Where(u => u.FechaRegistro < hasta);
        }

        filtro.Usuarios = await q.OrderByDescending(u => u.FechaRegistro).ToListAsync();
        return View(filtro);
    }

    [HttpGet]
    public async Task<IActionResult> EditarUsuario(int id)
    {
        var usuario = await _db.Usuarios.FindAsync(id);
        if (usuario is null) return NotFound();
        await CargarCombosAsync();
        return View(usuario);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditarUsuario(Usuario modelo, string? nuevaPassword)
    {
        var usuario = await _db.Usuarios.FindAsync(modelo.Id);
        if (usuario is null) return NotFound();

        usuario.Nombre = modelo.Nombre;
        usuario.Apellido = modelo.Apellido;
        usuario.Compania = modelo.Compania;
        usuario.Telefono = modelo.Telefono;
        usuario.RolId = modelo.RolId;
        usuario.MembresiaId = modelo.MembresiaId;
        usuario.Activo = modelo.Activo;
        usuario.CorreoConfirmado = modelo.CorreoConfirmado;
        usuario.MembresiaPagada = modelo.MembresiaPagada;

        // Fechas de membresía: las columnas son timestamptz, así que se guardan en UTC.
        usuario.MembresiaInicio = ComoUtc(modelo.MembresiaInicio);
        usuario.MembresiaFin = ComoUtc(modelo.MembresiaFin);

        if (!string.IsNullOrWhiteSpace(nuevaPassword))
            usuario.PasswordHash = _hasher.HashPassword(usuario, nuevaPassword);

        await _db.SaveChangesAsync();
        TempData["Exito"] = _loc["Msg_UserUpdated"].Value;
        return RedirectToAction(nameof(Usuarios));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarUsuario(int id)
    {
        var usuario = await _db.Usuarios.FindAsync(id);
        if (usuario is not null)
        {
            _db.Usuarios.Remove(usuario);
            await _db.SaveChangesAsync();
            TempData["Exito"] = _loc["Msg_UserDeleted"].Value;
        }
        return RedirectToAction(nameof(Usuarios));
    }

    // ================= MEMBRESÍAS =================

    [HttpGet]
    public async Task<IActionResult> Membresias()
    {
        var planes = await _db.Membresias.OrderBy(m => m.Orden).ToListAsync();
        return View(planes);
    }

    [HttpGet]
    public IActionResult CrearMembresia() => View("EditarMembresia", new Membresia());

    [HttpGet]
    public async Task<IActionResult> EditarMembresia(int id)
    {
        var plan = await _db.Membresias.FindAsync(id);
        if (plan is null) return NotFound();
        return View(plan);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarMembresia(Membresia modelo)
    {
        if (!ModelState.IsValid)
            return View("EditarMembresia", modelo);

        if (modelo.Id == 0)
        {
            _db.Membresias.Add(modelo);
        }
        else
        {
            var plan = await _db.Membresias.FindAsync(modelo.Id);
            if (plan is null) return NotFound();
            plan.Nombre = modelo.Nombre;
            plan.Descripcion = modelo.Descripcion;
            plan.PrecioMensual = modelo.PrecioMensual;
            plan.MaxUsuarios = modelo.MaxUsuarios;
            plan.Caracteristicas = modelo.Caracteristicas;
            plan.EsPopular = modelo.EsPopular;
            plan.Activa = modelo.Activa;
            plan.Orden = modelo.Orden;
        }

        await _db.SaveChangesAsync();
        TempData["Exito"] = _loc["Msg_PlanSaved"].Value;
        return RedirectToAction(nameof(Membresias));
    }

    // ================= PAGOS =================

    [HttpGet]
    public async Task<IActionResult> Pagos()
    {
        var pagos = await _db.Pagos
            .Include(p => p.Usuario)
            .Include(p => p.Membresia)
            .OrderByDescending(p => p.Fecha)
            .Take(200)
            .ToListAsync();
        return View(pagos);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarMembresia(int id)
    {
        var plan = await _db.Membresias.FindAsync(id);
        if (plan is not null)
        {
            var enUso = await _db.Usuarios.AnyAsync(u => u.MembresiaId == id);
            if (enUso)
                TempData["Error"] = _loc["Msg_PlanInUse"].Value;
            else
            {
                _db.Membresias.Remove(plan);
                await _db.SaveChangesAsync();
                TempData["Exito"] = _loc["Msg_PlanDeleted"].Value;
            }
        }
        return RedirectToAction(nameof(Membresias));
    }

    /// <summary>Normaliza una fecha (de un input date) a UTC para columnas timestamptz.</summary>
    private static DateTime? ComoUtc(DateTime? fecha) =>
        fecha.HasValue ? DateTime.SpecifyKind(fecha.Value, DateTimeKind.Utc) : null;

    private async Task CargarCombosAsync()
    {
        ViewBag.Roles = await _db.Roles.OrderBy(r => r.Nombre)
            .Select(r => new SelectListItem { Value = r.Id.ToString(), Text = r.Nombre })
            .ToListAsync();
        ViewBag.Membresias = await _db.Membresias.OrderBy(m => m.Orden)
            .Select(m => new SelectListItem { Value = m.Id.ToString(), Text = m.Nombre })
            .ToListAsync();
    }
}
