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
            if (session.Status == "complete" || session.PaymentStatus == "paid")
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancelar()
    {
        var usuario = await _db.Usuarios.FindAsync(UsuarioActualId());
        if (usuario?.StripeSubscriptionId is { } subId && _stripe.EstaConfigurado)
        {
            try
            {
                await _stripe.CancelarSuscripcionAsync(subId);
                usuario.CancelacionProgramada = true;
                await _db.SaveChangesAsync();
                TempData["Info"] = _loc["Sub_CancelScheduled"].Value;
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Error al cancelar la suscripción {Id}", subId);
                TempData["Error"] = ex.Message;
            }
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reactivar()
    {
        var usuario = await _db.Usuarios.FindAsync(UsuarioActualId());
        if (usuario?.StripeSubscriptionId is { } subId && usuario.CancelacionProgramada && _stripe.EstaConfigurado)
        {
            try
            {
                await _stripe.ReactivarSuscripcionAsync(subId);
                usuario.CancelacionProgramada = false;
                await _db.SaveChangesAsync();
                TempData["Exito"] = _loc["Sub_Reactivated"].Value;
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Error al reactivar la suscripción {Id}", subId);
                TempData["Error"] = ex.Message;
            }
        }
        return RedirectToAction(nameof(Index));
    }

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
            // throwOnApiVersionMismatch: false evita fallar si la versión de API de la
            // cuenta difiere de la que trae Stripe.net; los campos que usamos son estables.
            var stripeEvent = EventUtility.ConstructEvent(
                json, Request.Headers["Stripe-Signature"], _stripe.WebhookSecret,
                tolerance: 300, throwOnApiVersionMismatch: false);

            switch (stripeEvent.Type)
            {
                // Alta inicial de la suscripción.
                case "checkout.session.completed" when stripeEvent.Data.Object is Session session:
                    await ActivarDesdeSesionAsync(session);
                    break;

                // Renovación mensual: extiende la vigencia y registra el cobro.
                case "invoice.paid" when stripeEvent.Data.Object is Invoice invoice:
                    await RenovarDesdeFacturaAsync(invoice);
                    break;

                // La suscripción terminó (cancelada o impago): se marca como no pagada.
                case "customer.subscription.deleted" when stripeEvent.Data.Object is Subscription sub:
                    await FinalizarSuscripcionAsync(sub);
                    break;
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
        usuario.CancelacionProgramada = false;
        if (!string.IsNullOrEmpty(session.CustomerId))
            usuario.StripeCustomerId = session.CustomerId;
        if (!string.IsNullOrEmpty(session.SubscriptionId))
            usuario.StripeSubscriptionId = session.SubscriptionId;

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

    /// <summary>Renovación mensual: extiende la vigencia y registra el cobro (idempotente).</summary>
    private async Task RenovarDesdeFacturaAsync(Invoice invoice)
    {
        if (invoice.BillingReason != "subscription_cycle") return; // el alta inicial la maneja checkout.session.completed

        // La API reciente de Stripe ya no expone invoice.subscription directamente; se
        // identifica al usuario por el cliente de Stripe (invoice.customer).
        if (string.IsNullOrEmpty(invoice.CustomerId)) return;
        var usuario = await _db.Usuarios.Include(u => u.Membresia)
            .FirstOrDefaultAsync(u => u.StripeCustomerId == invoice.CustomerId);
        if (usuario?.MembresiaId is null || usuario.Membresia is null) return;

        if (await _db.Pagos.AnyAsync(p => p.StripeSessionId == invoice.Id)) return; // idempotencia

        var ahora = DateTime.UtcNow;
        var fin = ahora.AddMonths(1);
        usuario.MembresiaPagada = true;
        usuario.MembresiaFin = fin;

        var pago = new Pago
        {
            StripeSessionId = invoice.Id!,
            UsuarioId = usuario.Id,
            MembresiaId = usuario.MembresiaId.Value,
            Monto = invoice.AmountPaid / 100m,
            Moneda = invoice.Currency ?? "usd",
            Estado = EstadoPago.Pagado,
            Fecha = ahora,
            PeriodoInicio = ahora,
            PeriodoFin = fin
        };
        _db.Pagos.Add(pago);
        await _db.SaveChangesAsync();

        await EnviarConfirmacionPagoAsync(usuario, usuario.Membresia, pago);
    }

    /// <summary>La suscripción terminó (cancelación efectiva o impago): la membresía deja de estar pagada.</summary>
    private async Task FinalizarSuscripcionAsync(Subscription sub)
    {
        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.StripeSubscriptionId == sub.Id);
        if (usuario is null) return;

        usuario.MembresiaPagada = false;
        usuario.CancelacionProgramada = false;
        usuario.StripeSubscriptionId = null;
        usuario.MembresiaFin = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    private int UsuarioActualId()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(id, out var v) ? v : 0;
    }
}
