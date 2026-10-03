using ConcreteSalesRadar.Models.Entities;

namespace ConcreteSalesRadar.Models.ViewModels;

public class SuscripcionViewModel
{
    public Usuario Usuario { get; set; } = default!;
    public List<Membresia> Planes { get; set; } = new();
    public bool StripeConfigurado { get; set; }
}
