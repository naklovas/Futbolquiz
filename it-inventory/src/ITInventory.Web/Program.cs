using ITInventory.Data;
using ITInventory.Web.Configuration;
using ITInventory.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
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

builder.Services.Configure<LdapSettings>(builder.Configuration.GetSection(LdapSettings.SectionName));
builder.Services.AddSingleton<ILdapAuthenticationService, LdapAuthenticationService>();

builder.Services.Configure<TestLoginSettings>(builder.Configuration.GetSection(TestLoginSettings.SectionName));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IDevicePoolService, DevicePoolService>();
builder.Services.AddScoped<IActivityLogger, ActivityLogger>();

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

app.Run();
