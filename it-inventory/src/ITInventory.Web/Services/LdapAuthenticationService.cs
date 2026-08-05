using System.DirectoryServices.Protocols;
using System.Net;
using ITInventory.Web.Configuration;
using Microsoft.Extensions.Options;

namespace ITInventory.Web.Services;

public class LdapAuthenticationService : ILdapAuthenticationService
{
    private readonly LdapSettings _settings;
    private readonly ILogger<LdapAuthenticationService> _logger;

    public LdapAuthenticationService(IOptions<LdapSettings> settings, ILogger<LdapAuthenticationService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public bool ValidateCredentials(string username, string password, out string? errorMessage)
    {
        errorMessage = null;

        var server = _settings.Servers.Length > 0 ? _settings.Servers[0] : _settings.Domain;
        var userPrincipal = username.Contains('@') ? username : $"{username}@{_settings.Domain}";

        var identifier = new LdapDirectoryIdentifier(server, _settings.Port);
        var credential = new NetworkCredential(userPrincipal, password);

        using var connection = new LdapConnection(identifier, credential)
        {
            AuthType = AuthType.Negotiate,
            Timeout = TimeSpan.FromSeconds(10)
        };

        if (_settings.UseSsl)
        {
            connection.SessionOptions.SecureSocketLayer = true;
            connection.SessionOptions.ProtocolVersion = 3;
        }

        try
        {
            connection.Bind();
            return true;
        }
        catch (LdapException ex)
        {
            _logger.LogWarning("LDAP bind failed. User: {Username}, Error: {Message}", username, ex.Message);
            errorMessage = "Invalid username or password.";
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not connect to the LDAP server ({Server}).", server);
            errorMessage = "Unable to reach the authentication server right now. Please try again later.";
            return false;
        }
    }
}
