using ConcreteSalesRadar.Models.Entities;
using Stripe;
using Stripe.Checkout;

namespace ConcreteSalesRadar.Services;

public class StripeSettings
{
    public string? SecretKey { get; set; }
    public string? PublishableKey { get; set; }
    public string? WebhookSecret { get; set; }
    public string Moneda { get; set; } = "usd";
}

public interface IStripeService
{
    /// <summary>True cuando hay claves de Stripe configuradas.</summary>
    bool EstaConfigurado { get; }

    string? PublishableKey { get; }
    string? WebhookSecret { get; }

    /// <summary>Crea una Checkout Session de suscripción mensual y devuelve la sesión.</summary>
    Task<Session> CrearCheckoutSessionAsync(Usuario usuario, Membresia membresia, string successUrl, string cancelUrl);

    /// <summary>Recupera una Checkout Session por su id.</summary>
    Task<Session> ObtenerSesionAsync(string sessionId);

    /// <summary>Programa la cancelación de la suscripción al final del período actual.</summary>
    Task CancelarSuscripcionAsync(string subscriptionId);

    /// <summary>Revierte una cancelación programada: la suscripción sigue renovándose.</summary>
    Task ReactivarSuscripcionAsync(string subscriptionId);

    /// <summary>
    /// Crea o actualiza el producto y el precio del plan en Stripe y devuelve sus ids
    /// (prod_xxx, price_xxx). Como el precio de Stripe es inmutable, si cambió el monto
    /// se crea un precio nuevo y se archiva el anterior. Para planes "de contacto"
    /// (precio ≤ 0) no se crea precio y se devuelve priceId nulo.
    /// </summary>
    Task<(string? productId, string? priceId)> SincronizarPlanAsync(Membresia membresia);
}

public class StripeService : IStripeService
{
    private readonly StripeSettings _settings;

    public StripeService(IConfiguration config)
    {
        _settings = config.GetSection("Stripe").Get<StripeSettings>() ?? new StripeSettings();
        if (EstaConfigurado)
            StripeConfiguration.ApiKey = _settings.SecretKey;
    }

    public bool EstaConfigurado => !string.IsNullOrWhiteSpace(_settings.SecretKey);
    public string? PublishableKey => _settings.PublishableKey;
    public string? WebhookSecret => _settings.WebhookSecret;

    public async Task<Session> CrearCheckoutSessionAsync(Usuario usuario, Membresia membresia, string successUrl, string cancelUrl)
    {
        var metadata = new Dictionary<string, string>
        {
            ["usuarioId"] = usuario.Id.ToString(),
            ["membresiaId"] = membresia.Id.ToString()
        };

        var opciones = new SessionCreateOptions
        {
            Mode = "subscription",
            CustomerEmail = usuario.StripeCustomerId is null ? usuario.Correo : null,
            Customer = usuario.StripeCustomerId,
            ClientReferenceId = usuario.Id.ToString(),
            LineItems = new List<SessionLineItemOptions> { ConstruirLineItem(membresia) },
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            Metadata = metadata,
            // La metadata también viaja a la suscripción, para identificarla en los webhooks de renovación.
            SubscriptionData = new SessionSubscriptionDataOptions { Metadata = metadata }
        };

        var service = new SessionService();
        return await service.CreateAsync(opciones);
    }

    public async Task<Session> ObtenerSesionAsync(string sessionId)
    {
        var service = new SessionService();
        return await service.GetAsync(sessionId);
    }

    public async Task CancelarSuscripcionAsync(string subscriptionId)
    {
        var service = new SubscriptionService();
        // Cancela al final del período: el usuario mantiene acceso hasta que termine.
        await service.UpdateAsync(subscriptionId, new SubscriptionUpdateOptions { CancelAtPeriodEnd = true });
    }

    public async Task ReactivarSuscripcionAsync(string subscriptionId)
    {
        var service = new SubscriptionService();
        await service.UpdateAsync(subscriptionId, new SubscriptionUpdateOptions { CancelAtPeriodEnd = false });
    }

    public async Task<(string? productId, string? priceId)> SincronizarPlanAsync(Membresia membresia)
    {
        if (!EstaConfigurado) return (membresia.StripeProductId, membresia.StripePriceId);

        var productService = new ProductService();
        var priceService = new PriceService();

        // 1) Producto: se crea una vez y luego se actualiza (nombre/descripción).
        var nombreProducto = $"Concrete Sales Radar — {membresia.Nombre}";
        var productId = membresia.StripeProductId;
        if (string.IsNullOrWhiteSpace(productId))
        {
            var producto = await productService.CreateAsync(new ProductCreateOptions
            {
                Name = nombreProducto,
                Description = membresia.Descripcion
            });
            productId = producto.Id;
        }
        else
        {
            await productService.UpdateAsync(productId, new ProductUpdateOptions
            {
                Name = nombreProducto,
                Description = membresia.Descripcion
            });
        }

        // 2) Precio: un plan "de contacto" (≤ 0) no tiene precio recurrente en Stripe.
        if (membresia.PrecioMensual <= 0)
        {
            await ArchivarPrecioAsync(priceService, membresia.StripePriceId);
            return (productId, null);
        }

        var montoDeseado = (long)(membresia.PrecioMensual * 100m);

        // Si ya existe un precio y coincide con monto/moneda/intervalo, se reutiliza.
        if (!string.IsNullOrWhiteSpace(membresia.StripePriceId))
        {
            try
            {
                var actual = await priceService.GetAsync(membresia.StripePriceId);
                if (actual.Active
                    && actual.UnitAmount == montoDeseado
                    && string.Equals(actual.Currency, _settings.Moneda, StringComparison.OrdinalIgnoreCase)
                    && actual.Recurring?.Interval == "month")
                {
                    return (productId, actual.Id);
                }
            }
            catch (StripeException) { /* el precio ya no existe: se crea uno nuevo abajo */ }
        }

        // El monto cambió (o no había precio): como el precio de Stripe es inmutable,
        // se crea uno nuevo y se archiva el anterior.
        var nuevoPrecio = await priceService.CreateAsync(new PriceCreateOptions
        {
            Product = productId,
            Currency = _settings.Moneda,
            UnitAmount = montoDeseado,
            Recurring = new PriceRecurringOptions { Interval = "month" }
        });
        await ArchivarPrecioAsync(priceService, membresia.StripePriceId);
        return (productId, nuevoPrecio.Id);
    }

    /// <summary>Desactiva un precio anterior en Stripe (no se pueden borrar, solo archivar).</summary>
    private static async Task ArchivarPrecioAsync(PriceService priceService, string? priceId)
    {
        if (string.IsNullOrWhiteSpace(priceId)) return;
        try
        {
            await priceService.UpdateAsync(priceId, new PriceUpdateOptions { Active = false });
        }
        catch (StripeException) { /* ya no existe o ya estaba archivado */ }
    }

    /// <summary>
    /// Construye el ítem del checkout. Usa el precio guardado del plan (price_xxx) para
    /// hacer el match con Stripe; si por alguna razón no existe, cae a un precio inline.
    /// </summary>
    private SessionLineItemOptions ConstruirLineItem(Membresia membresia)
    {
        if (!string.IsNullOrWhiteSpace(membresia.StripePriceId))
            return new SessionLineItemOptions { Quantity = 1, Price = membresia.StripePriceId };

        return new SessionLineItemOptions
        {
            Quantity = 1,
            PriceData = new SessionLineItemPriceDataOptions
            {
                Currency = _settings.Moneda,
                UnitAmount = (long)(membresia.PrecioMensual * 100m),
                Recurring = new SessionLineItemPriceDataRecurringOptions { Interval = "month" },
                ProductData = new SessionLineItemPriceDataProductDataOptions
                {
                    Name = $"Concrete Sales Radar — {membresia.Nombre}",
                    Description = membresia.Descripcion
                }
            }
        };
    }
}
