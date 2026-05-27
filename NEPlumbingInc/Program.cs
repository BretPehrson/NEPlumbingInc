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
var (connectionString, connectionStringSource) = ResolveDefaultConnectionString(builder.Configuration);

builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(
        connectionString,
        sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);
            sqlOptions.CommandTimeout(30);
        }));

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
builder.Services.AddMemoryCache();

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
builder.Services.AddScoped<ISpamFilterService, SpamFilterService>();
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

app.Logger.LogInformation(
    "Resolved DefaultConnection from configuration key source: {ConnectionStringSource}.",
    connectionStringSource);

// Ensure database and apply migrations
await InitializeDatabaseWithRetryAsync(app);

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

static async Task InitializeDatabaseWithRetryAsync(WebApplication app)
{
    const int maxAttempts = 5;
    Exception? lastException = null;

    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var services = scope.ServiceProvider;
            var contextFactory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();
            using var context = await contextFactory.CreateDbContextAsync();

            await context.Database.MigrateAsync();
            await SeedData.Initialize(services, context);

            app.Logger.LogInformation(
                "Database initialization succeeded on attempt {Attempt}/{MaxAttempts}.",
                attempt,
                maxAttempts);
            return;
        }
        catch (Exception ex) when (attempt < maxAttempts)
        {
            lastException = ex;
            var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
            app.Logger.LogWarning(
                ex,
                "Database initialization failed on attempt {Attempt}/{MaxAttempts}. Retrying in {DelaySeconds} seconds.",
                attempt,
                maxAttempts,
                delay.TotalSeconds);
            await Task.Delay(delay);
        }
        catch (Exception ex)
        {
            lastException = ex;
        }
    }

    app.Logger.LogError(
        lastException,
        "Database initialization failed after {MaxAttempts} attempts.",
        maxAttempts);
    throw new InvalidOperationException(
        "Database initialization failed after maximum retry attempts.",
        lastException);
}

static (string ConnectionString, string Source) ResolveDefaultConnectionString(IConfiguration configuration)
{
    var candidates = new (string Key, string? Value)[]
    {
        ("ConnectionStrings:DefaultConnection", configuration.GetConnectionString("DefaultConnection")),
        ("ConnectionStrings:DefaultConnection", configuration["ConnectionStrings:DefaultConnection"]),
        ("ConnectionStrings__DefaultConnection", configuration["ConnectionStrings__DefaultConnection"]),
        ("DefaultConnection", configuration["DefaultConnection"]),
        ("SQLAZURECONNSTR_DefaultConnection", configuration["SQLAZURECONNSTR_DefaultConnection"]),
        ("SQLCONNSTR_DefaultConnection", configuration["SQLCONNSTR_DefaultConnection"]),
        ("CUSTOMCONNSTR_DefaultConnection", configuration["CUSTOMCONNSTR_DefaultConnection"])
    };

    foreach (var candidate in candidates)
    {
        if (string.IsNullOrWhiteSpace(candidate.Value))
        {
            continue;
        }

        var value = candidate.Value.Trim();

        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
        {
            value = value[1..^1].Trim();
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            return (value, candidate.Key);
        }
    }

    throw new InvalidOperationException(
        "DefaultConnection connection string not found. Checked keys: " +
        "ConnectionStrings:DefaultConnection, ConnectionStrings__DefaultConnection, DefaultConnection, " +
        "SQLAZURECONNSTR_DefaultConnection, SQLCONNSTR_DefaultConnection, CUSTOMCONNSTR_DefaultConnection.");
}
