using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Authentication.WebAssembly.Msal;
using Microsoft.Extensions.DependencyInjection;
using Letmework4U.Client;
using Letmework4U.Client.Services;
using Blazored.LocalStorage;
using Blazored.Toast;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Add MSAL Authentication
builder.Services.AddMsalAuthentication(options =>
{
    builder.Configuration.Bind("AzureAdB2C", options.ProviderOptions.Authentication);

    // Add API scopes for access tokens
    var apiScopes = builder.Configuration.GetSection("ApiScopes").Get<string[]>();
    if (apiScopes != null)
    {
        foreach (var scope in apiScopes)
        {
            options.ProviderOptions.DefaultAccessTokenScopes.Add(scope);
        }
    }
});

// Configure HttpClient with authentication
builder.Services.AddHttpClient("Letmework4U.API", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"] ?? "http://localhost:7071/api/");
})
.AddHttpMessageHandler(sp =>
{
    var handler = sp.GetRequiredService<AuthorizationMessageHandler>()
        .ConfigureHandler(
            authorizedUrls: new[] { builder.Configuration["ApiBaseUrl"] ?? "http://localhost:7071/api/" },
            scopes: builder.Configuration.GetSection("ApiScopes").Get<string[]>() ?? Array.Empty<string>()
        );
    return handler;
});

// Register the authenticated HttpClient as the default
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>()
    .CreateClient("Letmework4U.API"));

// Add Blazored services
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddBlazoredToast();

// Register services
builder.Services.AddScoped<IApplicantService, ApplicantService>();
builder.Services.AddScoped<IJobApplicationService, JobApplicationService>();
builder.Services.AddScoped<IMeetingService, MeetingService>();
builder.Services.AddScoped<IApplicantTaskService, ApplicantTaskService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<IRecruiterService, RecruiterService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<StateContainer>();

await builder.Build().RunAsync();
