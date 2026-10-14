namespace ConcreteSalesRadar.Models.Entities;

/// <summary>
/// Registro de que un usuario vio el detalle de un proyecto. Hay un índice único por
/// (ProyectoId, UsuarioId): un mismo usuario que abre el detalle N veces cuenta como 1 vista.
/// </summary>
public class VistaProyecto
{
    public int Id { get; set; }

    public int ProyectoId { get; set; }
    public Proyecto? Proyecto { get; set; }

    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }

    public DateTime FechaVista { get; set; } = DateTime.UtcNow;
}
