using System.Security.Claims;
using ConcreteSalesRadar.Data;
using ConcreteSalesRadar.Models.Entities;
using ConcreteSalesRadar.Models.ViewModels;
using ConcreteSalesRadar.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ConcreteSalesRadar.Controllers;

public class CuentaController : Controller
{
    private readonly AppDbContext _db;
    private readonly IEmailService _email;
    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly PasswordHasher<Usuario> _hasher = new();

    public CuentaController(AppDbContext db, IEmailService email, IStringLocalizer<SharedResource> loc)
    {
        _db = db;
        _email = email;
        _loc = loc;
    }

    // ---------------- REGISTRO ----------------

    [HttpGet]
    public async Task<IActionResult> Registro(int? plan)
    {
        await CargarPlanesAsync();
        var vm = new RegistroViewModel();
        if (plan.HasValue) vm.MembresiaId = plan.Value;
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Registro(RegistroViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            await CargarPlanesAsync();
            return View(vm);
        }

        var correo = vm.Correo.Trim().ToLowerInvariant();
        if (await _db.Usuarios.AnyAsync(u => u.Correo == correo))
        {
            ModelState.AddModelError(nameof(vm.Correo), _loc["Msg_EmailExists"]);
            await CargarPlanesAsync();
            return View(vm);
        }

        var rolUsuario = await _db.Roles.FirstAsync(r => r.Nombre == Roles.Usuario);
        var usuario = new Usuario
        {
            Nombre = vm.Nombre.Trim(),
            Apellido = vm.Apellido.Trim(),
            Compania = vm.Compania.Trim(),
            Correo = correo,
            Telefono = vm.Telefono.Trim(),
            RolId = rolUsuario.Id,
            MembresiaId = vm.MembresiaId,
            MembresiaInicio = DateTime.UtcNow,
            MembresiaFin = DateTime.UtcNow.AddMonths(1),
            CorreoConfirmado = false
        };
        usuario.PasswordHash = _hasher.HashPassword(usuario, vm.Password);

        _db.Usuarios.Add(usuario);
        await _db.SaveChangesAsync();

        var codigo = await GenerarCodigoAsync(usuario, TipoCodigo.ConfirmacionCorreo);
        await EnviarCodigoAsync(usuario, codigo, _loc["Email_SubjectConfirm"]);

        TempData["Info"] = _loc["Msg_CodeSent"].Value;
        if (!_email.EstaConfigurado)
            TempData["CodigoDev"] = codigo; // Solo para pruebas sin SMTP.

        return RedirectToAction(nameof(Verificar), new { usuarioId = usuario.Id });
    }

    // ---------------- VERIFICACIÓN (doble autenticación) ----------------

    [HttpGet]
    public async Task<IActionResult> Verificar(int usuarioId)
    {
        var usuario = await _db.Usuarios.FindAsync(usuarioId);
        if (usuario is null) return RedirectToAction(nameof(Registro));

        return View(new VerificarCodigoViewModel { UsuarioId = usuarioId, Correo = usuario.Correo });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Verificar(VerificarCodigoViewModel vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var codigo = await _db.CodigosVerificacion
            .Where(c => c.UsuarioId == vm.UsuarioId
                        && c.Tipo == TipoCodigo.ConfirmacionCorreo
                        && c.Codigo == vm.Codigo
                        && !c.Usado)
            .OrderByDescending(c => c.FechaCreacion)
            .FirstOrDefaultAsync();

        if (codigo is null || !codigo.EsValido)
        {
            ModelState.AddModelError(nameof(vm.Codigo), _loc["Msg_CodeInvalid"]);
            return View(vm);
        }

        var usuario = await _db.Usuarios.FindAsync(vm.UsuarioId);
        if (usuario is null) return RedirectToAction(nameof(Registro));

        codigo.Usado = true;
        usuario.CorreoConfirmado = true;
        await _db.SaveChangesAsync();

        TempData["Exito"] = _loc["Msg_Verified"].Value;
        return RedirectToAction(nameof(Login));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReenviarCodigo(int usuarioId)
    {
        var usuario = await _db.Usuarios.FindAsync(usuarioId);
        if (usuario is null) return RedirectToAction(nameof(Registro));

        var codigo = await GenerarCodigoAsync(usuario, TipoCodigo.ConfirmacionCorreo);
        await EnviarCodigoAsync(usuario, codigo, _loc["Email_SubjectNewCode"]);

        TempData["Info"] = _loc["Msg_NewCodeSent"].Value;
        if (!_email.EstaConfigurado) TempData["CodigoDev"] = codigo;

        return RedirectToAction(nameof(Verificar), new { usuarioId });
    }

    // ---------------- LOGIN / LOGOUT ----------------

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel vm, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(vm);

        var correo = vm.Correo.Trim().ToLowerInvariant();
        var usuario = await _db.Usuarios.Include(u => u.Rol)
            .FirstOrDefaultAsync(u => u.Correo == correo);

        if (usuario is null ||
            _hasher.VerifyHashedPassword(usuario, usuario.PasswordHash, vm.Password) == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(string.Empty, _loc["Msg_BadCredentials"]);
            return View(vm);
        }

        if (!usuario.Activo)
        {
            ModelState.AddModelError(string.Empty, _loc["Msg_Disabled"]);
            return View(vm);
        }

        if (!usuario.CorreoConfirmado)
        {
            TempData["Info"] = _loc["Msg_MustVerify"].Value;
            var codigo = await GenerarCodigoAsync(usuario, TipoCodigo.ConfirmacionCorreo);
            await EnviarCodigoAsync(usuario, codigo, _loc["Email_SubjectConfirm"]);
            if (!_email.EstaConfigurado) TempData["CodigoDev"] = codigo;
            return RedirectToAction(nameof(Verificar), new { usuarioId = usuario.Id });
        }

        usuario.UltimoAcceso = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await IniciarSesionAsync(usuario, vm.Recordarme);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult AccesoDenegado() => View();

    // ---------------- Helpers ----------------

    private async Task CargarPlanesAsync()
    {
        var planes = await _db.Membresias.Where(m => m.Activa).OrderBy(m => m.Orden).ToListAsync();
        ViewBag.Planes = planes.Select(m => new SelectListItem
        {
            Value = m.Id.ToString(),
            Text = m.PrecioMensual > 0
                ? $"{m.Nombre} — ${m.PrecioMensual:0} {_loc["Pricing_PerMonth"].Value}"
                : $"{m.Nombre} — {_loc["Pricing_Contact"].Value}"
        }).ToList();
    }

    private async Task<string> GenerarCodigoAsync(Usuario usuario, TipoCodigo tipo)
    {
        var codigo = Random.Shared.Next(100000, 999999).ToString();
        _db.CodigosVerificacion.Add(new CodigoVerificacion
        {
            UsuarioId = usuario.Id,
            Codigo = codigo,
            Tipo = tipo,
            FechaExpiracion = DateTime.UtcNow.AddMinutes(15)
        });
        await _db.SaveChangesAsync();
        return codigo;
    }

    private Task EnviarCodigoAsync(Usuario usuario, string codigo, string asunto)
    {
        var html = $@"
<div style='font-family:Arial,Helvetica,sans-serif;max-width:520px;margin:auto;border:1px solid #e2e8f0;border-radius:12px;overflow:hidden'>
  <div style='background:linear-gradient(135deg,#153d2f,#2d7257);color:#fff;padding:22px 26px'>
    <h2 style='margin:0'>Concrete Sales Radar</h2>
  </div>
  <div style='padding:26px'>
    <p>{_loc["Email_Hello"].Value} <b>{usuario.Nombre}</b>,</p>
    <p>{_loc["Email_Intro"].Value}</p>
    <div style='font-size:34px;font-weight:bold;letter-spacing:8px;color:#153d2f;text-align:center;
                background:#e9f1ed;border-radius:10px;padding:18px;margin:18px 0'>{codigo}</div>
    <p style='color:#64748b;font-size:14px'>{_loc["Email_Expire"].Value}</p>
  </div>
</div>";
        return _email.EnviarAsync(usuario.Correo, asunto, html);
    }

    private async Task IniciarSesionAsync(Usuario usuario, bool recordarme)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new(ClaimTypes.Name, usuario.NombreCompleto),
            new(ClaimTypes.Email, usuario.Correo),
            new(ClaimTypes.Role, usuario.Rol?.Nombre ?? Roles.Usuario)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var props = new AuthenticationProperties
        {
            IsPersistent = recordarme,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(recordarme ? 14 : 1)
        };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity), props);
    }
}
