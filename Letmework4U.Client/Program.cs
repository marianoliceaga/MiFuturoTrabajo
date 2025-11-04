using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Authorization;
using Letmework4U.Client;
using Letmework4U.Client.Services;
using Blazored.LocalStorage;
using Blazored.Toast;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Get environment and log for debugging
var environment = builder.HostEnvironment.Environment;
Console.WriteLine($"Running in environment: {environment}");

// Configure API Base URL
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:7070/api/";
Console.WriteLine($"API Base URL: {apiBaseUrl}");

// Add Blazored services
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddBlazoredToast();

// Add Custom Authorization
builder.Services.AddAuthorizationCore();

// Register Custom Authentication State Provider
builder.Services.AddScoped<CustomAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(provider =>
    provider.GetRequiredService<CustomAuthStateProvider>());

// Register Authentication Service
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();

// Register Authentication HTTP Message Handler
builder.Services.AddTransient<AuthenticationHttpMessageHandler>();

// Configure HttpClient with custom authentication handler
builder.Services.AddHttpClient("Letmework4U.API", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<AuthenticationHttpMessageHandler>();

// Register the authenticated HttpClient as the default
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>()
    .CreateClient("Letmework4U.API"));

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
