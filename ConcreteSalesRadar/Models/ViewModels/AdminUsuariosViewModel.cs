using ConcreteSalesRadar.Models.Entities;

namespace ConcreteSalesRadar.Models.ViewModels;

/// <summary>
/// Filtros y resultados del mantenimiento de usuarios del panel de administración.
/// Permite buscar por nombre, apellido o compañía y por rango de fecha de registro.
/// </summary>
public class AdminUsuariosViewModel
{
    public string? Nombre { get; set; }
    public string? Apellido { get; set; }
    public string? Compania { get; set; }
    public DateTime? FechaDesde { get; set; }
    public DateTime? FechaHasta { get; set; }

    public List<Usuario> Usuarios { get; set; } = new();
}
