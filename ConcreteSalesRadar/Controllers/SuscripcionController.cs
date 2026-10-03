using System.Security.Claims;
using ConcreteSalesRadar.Data;
using ConcreteSalesRadar.Models.Entities;
using ConcreteSalesRadar.Models.ViewModels;
using ConcreteSalesRadar.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Stripe;
using Stripe.Checkout;

namespace ConcreteSalesRadar.Controllers;

[Authorize]
public class SuscripcionController : Controller
{
    private readonly AppDbContext _db;
    private readonly IStripeService _stripe;
    private readonly IEmailService _email;
    private readonly IStringLocalizer<SharedResource> _loc;
    private readonly ILogger<SuscripcionController> _logger;

    public SuscripcionController(AppDbContext db, IStripeService stripe, IEmailService email,
        IStringLocalizer<SharedResource> loc, ILogger<SuscripcionController> logger)
    {
        _db = db;
        _stripe = stripe;
        _email = email;
        _loc = loc;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var usuario = await _db.Usuarios.Include(u => u.Membresia)
            .FirstOrDefaultAsync(u => u.Id == UsuarioActualId());
        if (usuario is null) return RedirectToAction("Login", "Cuenta");

        var planes = await _db.Membresias.Where(m => m.Activa).OrderBy(m => m.Orden).ToListAsync();
        return View(new SuscripcionViewModel
        {
            Usuario = usuario,
            Planes = planes,
            StripeConfigurado = _stripe.EstaConfigurado
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(int membresiaId)
    {
        if (!_stripe.EstaConfigurado)
        {
            TempData["Error"] = _loc["Sub_NotConfigured"].Value;
            return RedirectToAction(nameof(Index));
        }

        var usuario = await _db.Usuarios.FindAsync(UsuarioActualId());
        var membresia = await _db.Membresias.FindAsync(membresiaId);
        if (usuario is null || membresia is null) return RedirectToAction(nameof(Index));

        if (membresia.PrecioMensual <= 0)
        {
            TempData["Info"] = _loc["Sub_ContactPlan"].Value;
            return RedirectToAction(nameof(Index));
        }

        var successUrl = Url.Action(nameof(Exito), "Suscripcion", null, Request.Scheme)
            + "?session_id={CHECKOUT_SESSION_ID}";
        var cancelUrl = Url.Action(nameof(Cancelado), "Suscripcion", null, Request.Scheme);

        var session = await _stripe.CrearCheckoutSessionAsync(usuario, membresia, successUrl!, cancelUrl!);
        return Redirect(session.Url);
    }

    [HttpGet]
    public async Task<IActionResult> Exito(string? session_id)
    {
        if (string.IsNullOrEmpty(session_id)) return RedirectToAction(nameof(Index));

        try
        {
            var session = await _stripe.ObtenerSesionAsync(session_id);
            if (session.PaymentStatus == "paid")
            {
                await ActivarDesdeSesionAsync(session);
                TempData["Exito"] = _loc["Sub_Success_Msg"].Value;
            }
            else
            {
                TempData["Info"] = _loc["Sub_Pending"].Value;
            }
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Error al recuperar la sesión de Stripe {Id}", session_id);
            TempData["Error"] = ex.Message;
        }

        return View();
    }

    [HttpGet]
    public IActionResult Cancelado() => View();

    // ---------------- Webhook de Stripe ----------------

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Webhook()
    {
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(_stripe.WebhookSecret))
            return Ok(); // Sin secreto configurado no se procesan webhooks.

        try
        {
            var stripeEvent = EventUtility.ConstructEvent(
                json, Request.Headers["Stripe-Signature"], _stripe.WebhookSecret);

            if (stripeEvent.Type == "checkout.session.completed" &&
                stripeEvent.Data.Object is Session session)
            {
                await ActivarDesdeSesionAsync(session);
            }
            return Ok();
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Webhook de Stripe inválido");
            return BadRequest();
        }
    }

    // ---------------- Activación (idempotente) ----------------

    private async Task ActivarDesdeSesionAsync(Session session)
    {
        if (!session.Metadata.TryGetValue("usuarioId", out var uid) ||
            !session.Metadata.TryGetValue("membresiaId", out var mid))
            return;

        // Idempotencia: si ya se registró este pago como Pagado, no hacer nada.
        var pagoExistente = await _db.Pagos.FirstOrDefaultAsync(p => p.StripeSessionId == session.Id);
        if (pagoExistente is { Estado: EstadoPago.Pagado }) return;

        var usuario = await _db.Usuarios.FindAsync(int.Parse(uid));
        var membresia = await _db.Membresias.FindAsync(int.Parse(mid));
        if (usuario is null || membresia is null) return;

        var ahora = DateTime.UtcNow;
        var fin = ahora.AddMonths(1);

        usuario.MembresiaId = membresia.Id;
        usuario.MembresiaPagada = true;
        usuario.MembresiaInicio = ahora;
        usuario.MembresiaFin = fin;
        if (!string.IsNullOrEmpty(session.CustomerId))
            usuario.StripeCustomerId = session.CustomerId;

        var pago = pagoExistente ?? new Pago { StripeSessionId = session.Id };
        pago.UsuarioId = usuario.Id;
        pago.MembresiaId = membresia.Id;
        pago.Monto = (session.AmountTotal ?? 0) / 100m;
        pago.Moneda = session.Currency ?? "usd";
        pago.Estado = EstadoPago.Pagado;
        pago.Fecha = ahora;
        pago.PeriodoInicio = ahora;
        pago.PeriodoFin = fin;
        if (pagoExistente is null) _db.Pagos.Add(pago);

        await _db.SaveChangesAsync();

        await EnviarConfirmacionPagoAsync(usuario, membresia, pago);
    }

    private async Task EnviarConfirmacionPagoAsync(Usuario usuario, Membresia membresia, Pago pago)
    {
        try
        {
            var vigencia = pago.PeriodoFin?.ToString("yyyy-MM-dd") ?? "-";
            var monto = $"${pago.Monto:0.00} {pago.Moneda.ToUpperInvariant()}";
            var contenido = $@"
<p>{_loc["Email_Hello"].Value} <b>{usuario.Nombre}</b>,</p>
<p>{_loc["Email_Pago_Msg"].Value}</p>
<table style='width:100%;border-collapse:collapse;margin:16px 0'>
  <tr><td style='padding:8px 0;color:#64748b'>{_loc["Email_Pago_Plan"].Value}</td><td style='padding:8px 0;text-align:right'><b>{membresia.Nombre}</b></td></tr>
  <tr><td style='padding:8px 0;color:#64748b'>{_loc["Email_Pago_Monto"].Value}</td><td style='padding:8px 0;text-align:right'><b>{monto}</b></td></tr>
  <tr><td style='padding:8px 0;color:#64748b'>{_loc["Email_Pago_Vigencia"].Value}</td><td style='padding:8px 0;text-align:right'><b>{vigencia}</b></td></tr>
</table>
<p style='color:#1d4ed8;font-weight:bold'>{_loc["Email_Pago_Gracias"].Value}</p>";
            await _email.EnviarAsync(usuario.Correo, _loc["Email_Pago_Subject"].Value,
                PlantillasCorreo.Envolver(contenido));
        }
        catch (Exception ex)
        {
            // No bloquear la activación si falla el envío del correo.
            _logger.LogError(ex, "Error al enviar el correo de confirmación de pago a {Correo}", usuario.Correo);
        }
    }

    private int UsuarioActualId()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(id, out var v) ? v : 0;
    }
}
