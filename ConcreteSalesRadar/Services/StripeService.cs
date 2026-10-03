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
            LineItems = new List<SessionLineItemOptions>
            {
                new()
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
                }
            },
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
}
