using ConcreteSalesRadar.Data;
using ConcreteSalesRadar.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ConcreteSalesRadar.Controllers;

[Authorize(Roles = Roles.Administrador)]
public class AdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly PasswordHasher<Usuario> _hasher = new();

    public AdminController(AppDbContext db) => _db = db;

    public IActionResult Index() => View();

    // ================= USUARIOS =================

    [HttpGet]
    public async Task<IActionResult> Usuarios()
    {
        var usuarios = await _db.Usuarios
            .Include(u => u.Rol)
            .Include(u => u.Membresia)
            .OrderByDescending(u => u.FechaRegistro)
            .ToListAsync();
        return View(usuarios);
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

        if (!string.IsNullOrWhiteSpace(nuevaPassword))
            usuario.PasswordHash = _hasher.HashPassword(usuario, nuevaPassword);

        await _db.SaveChangesAsync();
        TempData["Exito"] = "Usuario actualizado.";
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
            TempData["Exito"] = "Usuario eliminado.";
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
        TempData["Exito"] = "Membresía guardada.";
        return RedirectToAction(nameof(Membresias));
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
                TempData["Error"] = "No se puede eliminar: hay usuarios con este plan. Desactívalo en su lugar.";
            else
            {
                _db.Membresias.Remove(plan);
                await _db.SaveChangesAsync();
                TempData["Exito"] = "Membresía eliminada.";
            }
        }
        return RedirectToAction(nameof(Membresias));
    }

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
