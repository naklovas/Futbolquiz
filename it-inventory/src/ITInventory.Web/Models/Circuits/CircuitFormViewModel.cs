using System.ComponentModel.DataAnnotations;

namespace ITInventory.Web.Models.Circuits;

public class CircuitFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Country is required.")]
    [Display(Name = "Country")]
    public int CountryId { get; set; }

    [Required(ErrorMessage = "Circuit type is required.")]
    [StringLength(100)]
    [Display(Name = "Circuit Type")]
    public string CircuitType { get; set; } = string.Empty;

    [Required(ErrorMessage = "Capacity is required.")]
    [StringLength(50)]
    [Display(Name = "Capacity")]
    public string? CircuitCapacity { get; set; }

    [Required(ErrorMessage = "Provider is required.")]
    [StringLength(150)]
    [Display(Name = "Provider")]
    public string? Provider { get; set; }

    [Required(ErrorMessage = "Branch is required.")]
    [StringLength(150)]
    [Display(Name = "Branch")]
    public string? Branch { get; set; }

    [StringLength(255)]
    [Display(Name = "Location")]
    public string? Location { get; set; }

    [Required(ErrorMessage = "Start date is required.")]
    [DataType(DataType.Date)]
    [Display(Name = "Start Date")]
    public DateTime? StartDate { get; set; }

    [Required(ErrorMessage = "End date is required.")]
    [DataType(DataType.Date)]
    [Display(Name = "End Date")]
    public DateTime? EndDate { get; set; }

    [Display(Name = "Notes")]
    public string? Notes { get; set; }
}
