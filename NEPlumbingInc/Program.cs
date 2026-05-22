var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("FormSubmission", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ip,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
    });
});

// Add database context - use SQL Server for all environments
var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? builder.Configuration["ConnectionStrings__DefaultConnection"]
    ?? builder.Configuration["DefaultConnection"]
    ?? throw new InvalidOperationException(
        "DefaultConnection connection string not found.");

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddCascadingAuthenticationState();

// Add authentication services
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "AdminAuthCookie";
        options.LoginPath = "/auth/login";
        options.LogoutPath = "/auth/logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure Blazor Server circuit options
builder.Services.Configure<CircuitOptions>(options =>
{
    // Increase the circuit timeout to 45 minutes
    options.DisconnectedCircuitMaxRetained = 100;
    options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(5);
    options.MaxBufferedUnacknowledgedRenderBatches = 10;
});

builder.Services.AddScoped<IServiceManager, ServiceManager>();
builder.Services.AddScoped<ISpecialOfferService, SpecialOfferService>();
builder.Services.AddScoped<ISpecialOfferSettingsService, SpecialOfferSettingsService>();
builder.Services.AddScoped<ICareersPageSettingsService, CareersPageSettingsService>();
builder.Services.AddScoped<IMessageService, MessageService>();
builder.Services.AddScoped<IResumeStorageService, ResumeStorageService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IColorSettingsService, ColorSettingsService>();
builder.Services.AddScoped<IWebsiteMetricsService, WebsiteMetricsService>();
builder.Services.AddScoped<IMessageNotificationSettingsService, MessageNotificationSettingsService>();
builder.Services.AddScoped<DarkModeService>();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<HomePageContentService>();
builder.Services.Configure<EmailSettings>(
    builder.Configuration.GetSection("Email"));
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddHttpClient("LocalApi", client =>
{
    client.BaseAddress = new Uri("https://localhost:7162");
});

var app = builder.Build();

// Ensure database and apply migrations
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var contextFactory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var context = await contextFactory.CreateDbContextAsync();
    await context.Database.MigrateAsync();
    await SeedData.Initialize(services, context);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.MapControllers();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();
app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.UseMiddleware<PageVisitLoggingMiddleware>();


app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
