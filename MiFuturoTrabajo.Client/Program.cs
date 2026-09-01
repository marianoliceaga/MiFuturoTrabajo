using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.Authorization;
using MiFuturoTrabajo.Client;
using MiFuturoTrabajo.Client.Services;
using Blazored.LocalStorage;
using Blazored.Toast;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Get environment and log for debugging
var environment = builder.HostEnvironment.Environment;
Console.WriteLine($"Running in environment: {environment}");

// Configure API Base URL
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7071/api/";
Console.WriteLine($"API Base URL: {apiBaseUrl}");

// Add Blazored services
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddBlazoredToast();

// Resource strings live in Resources/Strings.resx (English, the neutral
// fallback) and Resources/Strings.es.resx (Spanish, what the app starts in).
// No ResourcesPath is configured: the marker type sits in the Resources
// namespace, so its full name already matches the embedded resource name.
builder.Services.AddLocalization();

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
builder.Services.AddHttpClient("MiFuturoTrabajo.API", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);

    // index.html started the runtime with an explicit applicationCulture, so
    // by the time this runs CurrentUICulture is already the language the user
    // chose. Sending it lets the API write verification and password-reset
    // emails in that language without every DTO having to carry a field for it.
    client.DefaultRequestHeaders.AcceptLanguage.Add(
        new System.Net.Http.Headers.StringWithQualityHeaderValue(
            System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName));
})
.AddHttpMessageHandler<AuthenticationHttpMessageHandler>();

// Register the authenticated HttpClient as the default
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>()
    .CreateClient("MiFuturoTrabajo.API"));

// Register services
builder.Services.AddScoped<IApplicantService, ApplicantService>();
builder.Services.AddScoped<IJobApplicationService, JobApplicationService>();
builder.Services.AddScoped<IMeetingService, MeetingService>();
builder.Services.AddScoped<IApplicantTaskService, ApplicantTaskService>();
builder.Services.AddScoped<ICompanyService, CompanyService>();
builder.Services.AddScoped<IRecruiterService, RecruiterService>();
builder.Services.AddScoped<ICVVersionService, CVVersionService>();
builder.Services.AddScoped<ICalendarService, CalendarService>();
builder.Services.AddScoped<IUserPreferenceService, UserPreferenceService>();
builder.Services.AddScoped<ICultureService, CultureService>();
builder.Services.AddScoped<IThemeService, ThemeService>();
builder.Services.AddScoped<StateContainer>();

// The culture is not set here. index.html starts Blazor with an explicit
// applicationCulture read from localStorage, which both sets the culture and
// tells the runtime which satellite assembly to download. Doing it from C#
// after the runtime has booted would set the former without the latter.
await builder.Build().RunAsync();
