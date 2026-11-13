using DevHome.Identity.Client.Pages;
using DevHome.Identity.Components;
using DevHome.Identity.Components.Account;
using DevHome.Identity.DataContext;
using DevHome.Identity.DataContext.Context;
using DevHome.Identity.DataContext.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Регистрируем сервисы приложения.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

RegistrationDataContext.AddIdentity(builder.Services, builder.Configuration);

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

var app = builder.Build();

// Настраиваем конвейер обработки HTTP-запросов.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // Значение HSTS по умолчанию — 30 дней; для промышленной эксплуатации при необходимости увеличьте его: https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(DevHome.Identity.Client._Imports).Assembly);

// Добавляем конечные точки, необходимые компонентам Identity из папки /Account.
app.MapAdditionalIdentityEndpoints();

await MigrationDataContext.AutoMigrateAsync(app);

app.Run();
