using ConcreteSalesRadar;
using ConcreteSalesRadar.Data;
using ConcreteSalesRadar.Services;
using ConcreteSalesRadar.Services.Config;
using ConcreteSalesRadar.Services.Leads;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Railway (y otros PaaS) definen el puerto en la variable de entorno PORT.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// --- Localización (Español / Inglés / Portugués) ---
// Sin ResourcesPath: los .resx se embeben con el nombre del namespace de la clase
// marcadora (ConcreteSalesRadar.SharedResource), que es lo que busca el localizador.
builder.Services.AddLocalization();

builder.Services.AddControllersWithViews()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization(options =>
        options.DataAnnotationLocalizerProvider = (_, factory) =>
            factory.Create(typeof(SharedResource)));

var culturasSoportadas = new[] { "es", "en", "pt" };
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.SetDefaultCulture("es")
           .AddSupportedCultures(culturasSoportadas)
           .AddSupportedUICultures(culturasSoportadas);
});

// --- Estrategia de configuración por entorno ---
// Local (Development): secretos desde appsettings.Development.json / user-secrets.
// Producción (Railway): secretos desde variables de entorno. El resto del arranque solo
// consulta la estrategia activa; no sabe de dónde salen los valores.
var cfgEntorno = SelectorConfiguracionEntorno.Crear(builder.Environment, builder.Configuration);
Console.WriteLine($"[Config] Estrategia de entorno activa: {cfgEntorno.Nombre}");

// --- Base de datos (PostgreSQL / Railway) ---
var connectionString = ConnectionStringHelper.Normalizar(cfgEntorno.ObtenerCadenaConexion());

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// --- Autenticación por cookies ---
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Cuenta/Login";
        options.LogoutPath = "/Cuenta/Logout";
        options.AccessDeniedPath = "/Cuenta/AccesoDenegado";
        options.ExpireTimeSpan = TimeSpan.FromDays(1);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

// --- Servicios ---
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IStripeService, StripeService>();

// --- Hangfire: jobs en segundo plano con historial en PostgreSQL (dashboard en /hangfire) ---
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));
builder.Services.AddHangfireServer();

// --- Job automático de leads (poblar proyectos con IA desde una API real) ---
builder.Services.Configure<OpcionesLeads>(builder.Configuration.GetSection("Leads"));
// La estrategia de entorno rellena las API keys (user-secrets en local, variables en Railway)
// sin pisar un valor ya presente en la sección "Leads".
builder.Services.PostConfigure<OpcionesLeads>(opts =>
{
    if (string.IsNullOrWhiteSpace(opts.Fuente.ApiKey))
        opts.Fuente.ApiKey = cfgEntorno.ObtenerShovelsApiKey();
    if (string.IsNullOrWhiteSpace(opts.Ia.ApiKey))
        opts.Ia.ApiKey = cfgEntorno.ObtenerIaApiKey();
});
builder.Services.AddHttpClient("leads", c =>
{
    c.Timeout = TimeSpan.FromSeconds(60);
    // Algunos portales públicos (TDLR, datos abiertos) rechazan peticiones sin User-Agent.
    c.DefaultRequestHeaders.UserAgent.ParseAdd("ConcreteSalesRadar/1.0 (+leads job)");
    c.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/html");
});
builder.Services.AddScoped<IFuenteProyectos>(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpcionesLeads>>();
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("leads");
    var proveedor = opts.Value.Fuente.Proveedor ?? "Shovels";
    if (proveedor.Equals("Nacional", StringComparison.OrdinalIgnoreCase))
    {
        // Todas las fuentes gratis juntas → cobertura nacional en una sola pasada.
        var sub = new List<(string, IFuenteProyectos)>
        {
            ("TDLR TABS", new FuenteTdlrTabs(http, opts.Value, sp.GetRequiredService<ILogger<FuenteTdlrTabs>>())),
            ("Datos Abiertos (Socrata)", new FuenteDatosAbiertos(http, opts.Value, sp.GetRequiredService<ILogger<FuenteDatosAbiertos>>())),
            ("ArcGIS", new FuenteArcGis(http, opts.Value, sp.GetRequiredService<ILogger<FuenteArcGis>>())),
            ("SAM.gov", new FuenteSamGov(http, opts.Value, sp.GetRequiredService<ILogger<FuenteSamGov>>())),
            ("USASpending (adjudicados)", new FuenteUsaSpending(http, opts.Value, sp.GetRequiredService<ILogger<FuenteUsaSpending>>())),
            ("TxDOT", new FuenteTxDot(http, opts.Value, sp.GetRequiredService<ILogger<FuenteTxDot>>())),
            ("Red amplia (auto)", new FuenteSocrataAuto(http, opts.Value, sp.GetRequiredService<ILogger<FuenteSocrataAuto>>())),
        };
        return new FuenteCompuesta(sub, sp.GetRequiredService<ILogger<FuenteCompuesta>>());
    }
    if (proveedor.Equals("TdlrTabs", StringComparison.OrdinalIgnoreCase))
        return new FuenteTdlrTabs(http, opts.Value, sp.GetRequiredService<ILogger<FuenteTdlrTabs>>());
    if (proveedor.Equals("SamGov", StringComparison.OrdinalIgnoreCase))
        return new FuenteSamGov(http, opts.Value, sp.GetRequiredService<ILogger<FuenteSamGov>>());
    if (proveedor.Equals("UsaSpendingAwards", StringComparison.OrdinalIgnoreCase))
        return new FuenteUsaSpending(http, opts.Value, sp.GetRequiredService<ILogger<FuenteUsaSpending>>());
    if (proveedor.Equals("TxDot", StringComparison.OrdinalIgnoreCase))
        return new FuenteTxDot(http, opts.Value, sp.GetRequiredService<ILogger<FuenteTxDot>>());
    if (proveedor.Equals("DatosAbiertos", StringComparison.OrdinalIgnoreCase))
        return new FuenteDatosAbiertos(http, opts.Value, sp.GetRequiredService<ILogger<FuenteDatosAbiertos>>());
    if (proveedor.Equals("ArcGis", StringComparison.OrdinalIgnoreCase))
        return new FuenteArcGis(http, opts.Value, sp.GetRequiredService<ILogger<FuenteArcGis>>());
    return proveedor.Equals("Http", StringComparison.OrdinalIgnoreCase)
        ? new FuenteProyectosHttp(http, opts, sp.GetRequiredService<ILogger<FuenteProyectosHttp>>())
        : new FuenteProyectosShovels(http, opts, sp.GetRequiredService<ILogger<FuenteProyectosShovels>>());
});
builder.Services.AddScoped<IEnriquecedorProyectosIa>(sp =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpcionesLeads>>();
    // Las fuentes que ya entregan leads normalizados usan el paso directo (sin IA, sin costo).
    var prov = opts.Value.Fuente.Proveedor ?? "";
    if (prov.Equals("Nacional", StringComparison.OrdinalIgnoreCase) ||
        prov.Equals("DatosAbiertos", StringComparison.OrdinalIgnoreCase) ||
        prov.Equals("ArcGis", StringComparison.OrdinalIgnoreCase) ||
        prov.Equals("TdlrTabs", StringComparison.OrdinalIgnoreCase) ||
        prov.Equals("SamGov", StringComparison.OrdinalIgnoreCase) ||
        prov.Equals("UsaSpendingAwards", StringComparison.OrdinalIgnoreCase) ||
        prov.Equals("TxDot", StringComparison.OrdinalIgnoreCase))
        return new EnriquecedorDirecto(sp.GetRequiredService<ILogger<EnriquecedorDirecto>>());

    var modo = opts.Value.Ia.Modo ?? "Claude";
    return modo.Equals("Local", StringComparison.OrdinalIgnoreCase)
        ? new EnriquecedorProyectosLocal(sp.GetRequiredService<ILogger<EnriquecedorProyectosLocal>>())
        : new EnriquecedorProyectosClaude(opts, sp.GetRequiredService<ILogger<EnriquecedorProyectosClaude>>());
});
builder.Services.AddScoped<PoblarProyectosJob>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Railway termina TLS en su proxy; no forzar redirección HTTPS dentro del contenedor.
if (app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseStaticFiles();

var locOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RequestLocalizationOptions>>();
app.UseRequestLocalization(locOptions.Value);

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Dashboard de Hangfire: solo administradores (rol de la cookie de sesión).
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new SoloAdministradorDashboardFilter()]
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// --- Migraciones + datos iniciales ---
await DbSeeder.InicializarAsync(app.Services, app.Configuration);

// --- Programación del job de leads (Hangfire) ---
var opcionesLeads = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<OpcionesLeads>>().Value;
var recurrentes = app.Services.GetRequiredService<IRecurringJobManager>();
if (opcionesLeads.Activo)
{
    var horas = Math.Max(1, opcionesLeads.IntervaloHoras);
    var cron = horas == 1 ? Cron.Hourly() : $"0 */{horas} * * *";
    recurrentes.AddOrUpdate<PoblarProyectosJob>(
        PoblarProyectosJob.IdRecurrente, job => job.EjecutarAsync(CancellationToken.None), cron);
}
else
{
    recurrentes.RemoveIfExists(PoblarProyectosJob.IdRecurrente);
}

app.Run();
