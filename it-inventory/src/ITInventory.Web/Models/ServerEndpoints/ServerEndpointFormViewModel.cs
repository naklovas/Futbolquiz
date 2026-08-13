using System.ComponentModel.DataAnnotations;

namespace ITInventory.Web.Models.ServerEndpoints;

public class ServerEndpointFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Server is required.")]
    [Display(Name = "Server")]
    public int ServerId { get; set; }

    [Required(ErrorMessage = "IP address is required.")]
    [StringLength(50)]
    [Display(Name = "IP Address")]
    public string? IpAddress { get; set; }

    [Required(ErrorMessage = "Port is required.")]
    [Range(1, 65535)]
    [Display(Name = "Port")]
    public int? Port { get; set; }

    [Required(ErrorMessage = "Application is required.")]
    [Display(Name = "Application")]
    public int? ApplicationId { get; set; }

    [Display(Name = "Notes")]
    public string? Notes { get; set; }
}
