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

    [Display(Name = "Send Expiration Notifications")]
    public bool ReceiveExpirationNotifications { get; set; } = true;

    // Role assignment is deliberately NOT a property here. Binding it as part of the model
    // would make role membership just another field that arrives with the rest of the form;
    // it is taken as an explicit parameter on the Create/Edit actions instead, and every id
    // is checked against dbo.YDRoles before it is used (see AdminUsersController).
}
