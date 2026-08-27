namespace ITInventory.Web.Services;

public interface ILdapAuthenticationService
{
    /// <summary>
    /// fintek.local domaininde kullanıcı adı/şifreyi LDAP bind ile doğrular.
    /// Sadece kimlik doğrulama yapar; kullanıcının IT Envanter sistemine
    /// erişim izni olup olmadığını kontrol etmez (bkz. IUserContextService).
    /// </summary>
    bool ValidateCredentials(string username, string password, out string? errorMessage);
}
