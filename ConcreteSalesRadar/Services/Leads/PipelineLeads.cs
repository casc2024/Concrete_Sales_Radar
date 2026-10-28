namespace ConcreteSalesRadar.Services.Leads;

/// <summary>
/// Un "pipeline" de leads = una fuente + su enriquecedor correspondiente. El job puede correr
/// VARIOS pipelines en una misma ejecución (ej. "Nacional" con paso directo + "BusquedaWeb" con
/// Gemini), juntar los leads y deduplicar/insertar una sola vez.
/// </summary>
public record PipelineLeads(string Nombre, IFuenteProyectos Fuente, IEnriquecedorProyectosIa Ia);
