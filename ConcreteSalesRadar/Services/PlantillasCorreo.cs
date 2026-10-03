namespace ConcreteSalesRadar.Services;

/// <summary>Construye el HTML de los correos con el branding de la aplicación.</summary>
public static class PlantillasCorreo
{
    /// <summary>Envuelve el contenido interno en la plantilla con encabezado de marca.</summary>
    public static string Envolver(string contenido) => $@"
<div style='font-family:Arial,Helvetica,sans-serif;max-width:540px;margin:auto;border:1px solid #e2e8f0;border-radius:12px;overflow:hidden'>
  <div style='background:linear-gradient(135deg,#0f2744,#2563eb);color:#fff;padding:22px 26px'>
    <h2 style='margin:0'>Concrete Sales Radar</h2>
  </div>
  <div style='padding:26px;color:#1e293b;font-size:15px;line-height:1.5'>
    {contenido}
  </div>
</div>";

    /// <summary>Bloque grande para resaltar un código de verificación.</summary>
    public static string Codigo(string codigo) => $@"
<div style='font-size:34px;font-weight:bold;letter-spacing:8px;color:#1d4ed8;text-align:center;
            background:#eff4ff;border-radius:10px;padding:18px;margin:18px 0'>{codigo}</div>";
}
