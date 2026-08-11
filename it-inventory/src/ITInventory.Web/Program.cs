using ITInventory.Data;
using ITInventory.Web.Configuration;
using ITInventory.Web.HealthChecks;
using ITInventory.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// PropertyNamingPolicy = null keeps @Json.Serialize(...) output in the exact C# PascalCase
// property names (CountryId, Branch, ...) instead of the framework's camelCase default --
// the inline <script> blocks that read this JSON (branch/vendor comboboxes, company contacts)
// all reference the PascalCase names.
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options => options.JsonSerializerOptions.PropertyNamingPolicy = null);

builder.Services.AddDbContext<ITInventoryDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("ITInventory")));

// OpenShift/Kubernetes pods get a fresh, ephemeral filesystem by default and the container
// runs as an arbitrary non-root UID with no home directory, so the framework's default
// Data Protection key location isn't writable -- keys would silently regenerate on every
// pod restart and log everyone out. Persisting them to a mounted, group-writable directory
// (see openshift/deployment.yaml's volumeMount) keeps auth cookies valid across restarts.
// Same directory works unchanged for a normal Windows/IIS deployment.
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysDirectory"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
        .SetApplicationName("ITInventory");
}

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

builder.Services.Configure<LdapSettings>(builder.Configuration.GetSection(LdapSettings.SectionName));
builder.Services.AddSingleton<ILdapAuthenticationService, LdapAuthenticationService>();

builder.Services.Configure<TestLoginSettings>(builder.Configuration.GetSection(TestLoginSettings.SectionName));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IDevicePoolService, DevicePoolService>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// OpenShift/Kubernetes liveness+readiness probes hit these anonymously (no session cookie);
// AllowAnonymous is required or the app's global RequireAuthenticatedUser fallback policy
// would redirect them to /Account/Login instead of returning a health status.
// /healthz/live: process is up, no dependency checks (never fails while the app is running).
// /healthz/ready: also confirms the database is reachable, for "can this pod take traffic".
app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = check => !check.Tags.Contains("ready")
}).AllowAnonymous();

app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

app.Run();
