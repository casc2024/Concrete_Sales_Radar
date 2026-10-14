using ConcreteSalesRadar.Models.Entities;

namespace ConcreteSalesRadar.Models.ViewModels;

public class DashboardViewModel
{
    // Filtros
    public string? Zona { get; set; }
    public int Radio { get; set; } = 50;
    public decimal PresupuestoMin { get; set; }
    public string? Tipo { get; set; }
    public string? Busqueda { get; set; }
    public string? Antiguedad { get; set; }   // new | active | mature
    public string? Contacto { get; set; }       // yes | no

    public List<Proyecto> Proyectos { get; set; } = new();

    /// <summary>Campos de la grilla que este usuario puede ver, según su membresía.</summary>
    public HashSet<string> CamposVisibles { get; set; } =
        new(CamposProyecto.Claves, StringComparer.OrdinalIgnoreCase);

    /// <summary>True si el campo debe mostrarse para el usuario actual.</summary>
    public bool Ver(string clave) => CamposVisibles.Contains(clave);

    // Paginación (10 registros por página)
    public int Pagina { get; set; } = 1;
    public const int TamanoPagina = 10;
    public int TotalRegistros { get; set; }
    public int TotalPaginas { get; set; }

    // KPIs
    public int TotalObras { get; set; }
    public decimal PresupuestoTotal { get; set; }
    public int YardasTotal { get; set; }
    public int Calientes { get; set; }
    public int ConContacto { get; set; }

    // Agregados
    public List<(string Tipo, int Cantidad)> PorTipo { get; set; } = new();
    public int PrioridadAlta { get; set; }
    public int PrioridadMedia { get; set; }
    public int PrioridadBaja { get; set; }

    public static readonly string[] TiposObra =
    {
        "Residential", "Multifamily", "Industrial", "Warehouse",
        "Data Center", "Road / Paving", "Commercial", "Municipal"
    };
}
