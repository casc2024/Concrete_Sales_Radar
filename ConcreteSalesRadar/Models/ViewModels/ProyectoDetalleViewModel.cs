using ConcreteSalesRadar.Models.Entities;

namespace ConcreteSalesRadar.Models.ViewModels;

/// <summary>Detalle de un proyecto (solo consulta) + los campos que la membresía permite ver.</summary>
public class ProyectoDetalleViewModel
{
    public Proyecto Proyecto { get; set; } = null!;

    public HashSet<string> CamposVisibles { get; set; } =
        new(CamposProyecto.Claves, StringComparer.OrdinalIgnoreCase);

    public bool Ver(string clave) => CamposVisibles.Contains(clave);
}
