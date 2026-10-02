using System.Diagnostics;
using ConcreteSalesRadar.Data;
using ConcreteSalesRadar.Models;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ConcreteSalesRadar.Controllers;

public class HomeController : Controller
{
    private readonly AppDbContext _db;

    public HomeController(AppDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var planes = await _db.Membresias.AsNoTracking()
            .Where(m => m.Activa).OrderBy(m => m.Orden).ToListAsync();
        return View(planes);
    }

    public IActionResult Privacy() => View();

    /// <summary>Cambia el idioma de la interfaz guardando una cookie de cultura.</summary>
    [HttpPost]
    public IActionResult CambiarIdioma(string cultura, string? returnUrl)
    {
        var permitidas = new[] { "es", "en", "pt" };
        if (!permitidas.Contains(cultura)) cultura = "es";

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(cultura)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);
        return RedirectToAction(nameof(Index));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
