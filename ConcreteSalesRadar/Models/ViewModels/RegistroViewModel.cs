using System.ComponentModel.DataAnnotations;

namespace ConcreteSalesRadar.Models.ViewModels;

public class RegistroViewModel
{
    // Permite letras (con acentos), números, espacios y signos comunes.
    private const string TextoNumero = @"^[A-Za-zÀ-ÿ0-9\s.,#'/()\-+&]+$";
    private const string RegexCorreo = @"^[^@\s]+@[^@\s]+\.[A-Za-z]{2,}$";
    // Los textos de ErrorMessage son claves que se traducen con SharedResource.

    [Required(ErrorMessage = "Val_Nombre_Req")]
    [StringLength(50, ErrorMessage = "Val_Max50")]
    [RegularExpression(TextoNumero, ErrorMessage = "Val_TextoNumero")]
    public string Nombre { get; set; } = string.Empty;

    [Required(ErrorMessage = "Val_Apellido_Req")]
    [StringLength(50, ErrorMessage = "Val_Max50")]
    [RegularExpression(TextoNumero, ErrorMessage = "Val_TextoNumero")]
    public string Apellido { get; set; } = string.Empty;

    [Required(ErrorMessage = "Val_Compania_Req")]
    [StringLength(50, ErrorMessage = "Val_Max50")]
    [RegularExpression(TextoNumero, ErrorMessage = "Val_TextoNumero")]
    public string Compania { get; set; } = string.Empty;

    [Required(ErrorMessage = "Val_Correo_Req")]
    [StringLength(100, ErrorMessage = "Val_Max100")]
    [RegularExpression(RegexCorreo, ErrorMessage = "Val_Correo_Fmt")]
    public string Correo { get; set; } = string.Empty;

    [Required(ErrorMessage = "Val_Telefono_Req")]
    [StringLength(50, ErrorMessage = "Val_Max50")]
    [RegularExpression(TextoNumero, ErrorMessage = "Val_TextoNumero")]
    public string Telefono { get; set; } = string.Empty;

    [Required(ErrorMessage = "Val_Pass_Req")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Val_Pass_Len")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Val_Confirm_Req")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Val_Pass_Match")]
    public string ConfirmarPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Val_Plan_Req")]
    [Range(1, int.MaxValue, ErrorMessage = "Val_Plan_Req")]
    public int MembresiaId { get; set; }
}
