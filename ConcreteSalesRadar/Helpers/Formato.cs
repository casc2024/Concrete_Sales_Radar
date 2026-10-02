using System.Globalization;

namespace ConcreteSalesRadar.Helpers;

public static class Formato
{
    private static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Formatea un monto como $1.2B / $3.4M / $500K / $999.</summary>
    public static string Dinero(decimal n)
    {
        if (n >= 1_000_000_000m) return "$" + (n / 1_000_000_000m).ToString("0.0", En) + "B";
        if (n >= 1_000_000m) return "$" + (n / 1_000_000m).ToString("0.0", En) + "M";
        if (n >= 1_000m) return "$" + (n / 1_000m).ToString("0", En) + "K";
        return "$" + n.ToString("#,0", En);
    }

    public static string Numero(int n) => n.ToString("#,0", En);

    public static (string Label, string Clase) Antiguedad(DateTime? fecha)
    {
        if (fecha is null) return ("No disponible", "cold");
        var hoy = DateTime.UtcNow;
        var meses = (hoy.Year - fecha.Value.Year) * 12 + (hoy.Month - fecha.Value.Month);
        if (meses < 0) return ("Próxima / no iniciada", "new");
        if (meses <= 3) return ($"{meses} mes{(meses == 1 ? "" : "es")}", "new");
        if (meses <= 12) return ($"{meses} meses", "active");
        return ($"{meses} meses", "mature");
    }

    public static string Calor(int prioridad) =>
        prioridad >= 75 ? "hot" : prioridad >= 50 ? "warm" : "cold";
}
