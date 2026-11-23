namespace ITInventory.Web.Services;

public interface ILdapAuthenticationService
{
    /// <summary>
    /// Kullanici adi/parolayi LDAP bind ile dogrular.
    ///
    /// Birden fazla domain tanimliysa: kullanici adresini tam yazdiysa
    /// (ali@zfgistirak.local) yalnizca o domain denenir; domainsiz yazdiysa
    /// (ali) domainler yapilandirmadaki SIRAYLA denenir ve ilk kabul eden
    /// kazanir.
    ///
    /// Sadece kimlik dogrulama yapar; kullanicinin IT Envanter sistemine
    /// erisim izni olup olmadigini kontrol etmez.
    /// </summary>
    /// <param name="eslesenDomain">
    /// Dogrulama basarili olduysa kimligin DOGRULANDIGI domain. Cagiran bunu
    /// bilmek zorunda: iki domainde ayni kullanici adi olabilir ve envanter
    /// kaydi buna gore secilir.
    /// </param>
    bool ValidateCredentials(string username, string password,
        out string? errorMessage, out string? eslesenDomain);
}
