using System.ComponentModel.DataAnnotations;
using System.Net;

namespace ITInventory.Web.Common;

/// <summary>
/// Validates that a string is a real IP address (IPv4 or IPv6), using the same parser .NET
/// itself uses to construct an IPAddress. Presence is a separate concern -- [Required] handles
/// that, this only checks format when a value is actually given, so it composes with both
/// required and optional IP fields without duplicating the "is it missing" check.
/// </summary>
public class IpAddressAttribute : ValidationAttribute
{
    public IpAddressAttribute() : base("The {0} field must be a valid IP address.")
    {
    }

    public override bool IsValid(object? value)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
            return true;

        return IPAddress.TryParse(text.Trim(), out _);
    }
}
