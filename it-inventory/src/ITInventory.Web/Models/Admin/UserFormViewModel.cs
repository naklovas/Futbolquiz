using System.ComponentModel.DataAnnotations;

namespace ITInventory.Web.Models.Admin;

public class UserFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Username is required.")]
    [StringLength(100)]
    [Display(Name = "Username (AD sAMAccountName)")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Full name is required.")]
    [StringLength(100)]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [StringLength(100)]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [Display(Name = "Country/Branch")]
    public int? CountryId { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Roles")]
    public List<int> SelectedRoleIds { get; set; } = new();
}
