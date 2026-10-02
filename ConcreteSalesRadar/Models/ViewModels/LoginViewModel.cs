using System.ComponentModel.DataAnnotations;

namespace ConcreteSalesRadar.Models.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "Val_Correo_Req")]
    [RegularExpression(@"^[^@\s]+@[^@\s]+\.[A-Za-z]{2,}$", ErrorMessage = "Val_Correo_Fmt")]
    public string Correo { get; set; } = string.Empty;

    [Required(ErrorMessage = "Val_Pass_Req")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool Recordarme { get; set; }
}

public class VerificarCodigoViewModel
{
    [Required]
    public int UsuarioId { get; set; }

    public string? Correo { get; set; }

    [Required(ErrorMessage = "Val_Codigo_Req")]
    [RegularExpression(@"^\d{6}$", ErrorMessage = "Val_Codigo_Fmt")]
    public string Codigo { get; set; } = string.Empty;
}
