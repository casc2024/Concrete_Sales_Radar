using ConcreteSalesRadar.Models.Entities;

namespace ConcreteSalesRadar.Services.Leads;

/// <summary>
/// Reglas de negocio de calidad del dato. Centraliza el cálculo de:
///  - Confidence Score (0-100): fiabilidad de la INFORMACIÓN del proyecto. NO es la probabilidad
///    de ganar la venta.
///  - Sales Opportunity Score (0-100): qué tan buena oportunidad comercial es (métrica distinta).
/// </summary>
public static class ReglasCalidadDatos
{
    /// <summary>
    /// Confianza en la fiabilidad del dato según su estado de verificación y qué contacto trae.
    /// 95-100 permiso/licitación oficial + empresa + contacto; 80-94 fuente confiable + empresa +
    /// un contacto; 60-79 planificación/pre-permiso o info incompleta; 0-59 preliminar/estimado.
    /// </summary>
    public static int Confianza(string estado, bool tieneEmpresa, bool tieneContacto)
    {
        if (EstadosVerificacion.EsVerificadoOficial(estado))
        {
            if (tieneEmpresa && tieneContacto) return 97; // oficial + owner/GC + teléfono
            if (tieneEmpresa || tieneContacto) return 88;
            return 80;
        }

        return estado switch
        {
            EstadosVerificacion.Comercial when tieneEmpresa && tieneContacto => 86,
            EstadosVerificacion.Comercial when tieneEmpresa || tieneContacto => 81,
            EstadosVerificacion.Comercial => 72,
            EstadosVerificacion.Planificacion => 68,
            _ => tieneContacto ? 58 : 45 // Estimado / incompleto
        };
    }

    /// <summary>
    /// Puntuación de oportunidad de venta (0-100): combina presupuesto, disponibilidad de contacto,
    /// cercanía de la fecha de inicio y etapa del proyecto. (La distancia a la planta se sumará
    /// cuando se configure la ubicación de la planta.)
    /// </summary>
    public static int Oportunidad(decimal presupuesto, bool tieneContacto, DateTime? inicio, string estado)
    {
        // Base por tamaño de obra (mayor presupuesto = más concreto potencial).
        int baseScore = presupuesto switch
        {
            >= 20_000_000m => 70,
            >= 10_000_000m => 62,
            >= 5_000_000m => 55,
            >= 1_000_000m => 45,
            >= 100_000m => 32,
            > 0 => 20,
            _ => 12
        };

        if (tieneContacto) baseScore += 12; // poder contactar sube la oportunidad

        // Proyectos por iniciar pronto (0-6 meses) valen más; muy lejanos o ya pasados, menos.
        if (inicio is DateTime i)
        {
            var dias = (i.Date - DateTime.UtcNow.Date).TotalDays;
            baseScore += dias switch
            {
                >= 0 and <= 180 => 12,
                > 180 and <= 365 => 6,
                < 0 => -6,
                _ => 0
            };
        }

        // Etapa: un permiso/licitación emitido es más accionable que planificación.
        if (EstadosVerificacion.EsVerificadoOficial(estado)) baseScore += 6;
        else if (estado == EstadosVerificacion.Planificacion) baseScore -= 4;

        return Math.Clamp(baseScore, 0, 100);
    }
}
