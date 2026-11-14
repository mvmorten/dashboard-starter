using System;
using System.Globalization;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using DashboardStarter.Api.Auth;
using DashboardStarter.Api.Customers;
using DashboardStarter.Api.Database;
using DashboardStarter.Api.Demo;
using DashboardStarter.Api.Email;
using DashboardStarter.Api.Hosting;
using DashboardStarter.Api.Members;
using DashboardStarter.Db.Authorization;
using DashboardStarter.Db.Passwords;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DashboardStarter.Api;

public static class Program
{
  public static async Task<int> Main(string[] args)
  {
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    // Development: appsettings.Development.json. Anywhere else: the ConnectionStrings__Default
    // environment variable. appsettings.json deliberately has no connection string.
    string? connectionString = builder.Configuration.GetConnectionString("Default");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
      // Logging isn't set up until the app is built, so this one message goes to stderr.
      Console.Error.WriteLine(
        $"No connection string. Environment is '{builder.Environment.EnvironmentName}', so set ConnectionStrings__Default.");
      return 1;
    }

    // The connection factory reads the signed-in user's tenant from the current request.
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton(services =>
      new SqlConnectionFactory(connectionString, services.GetRequiredService<IHttpContextAccessor>()));
    builder.Services.AddSingleton<StartupSchemaCheck>();
    builder.Services.AddSingleton<DataProtectionCheck>();

    DemoOptions demo = AddDemoSandboxes(builder);
    AddAuthentication(builder, demo);
    DataProtectionSetup.AddDataProtectionKeys(builder.Services, builder.Configuration);
    EmailSetup.AddEmail(builder.Services, builder.Configuration, builder.Environment);

    // Every POST, PUT, PATCH, and DELETE to a controller needs a valid antiforgery token;
    // GET, HEAD, OPTIONS, and TRACE don't. One filter for all actions, so a new endpoint
    // can't forget it.
    builder.Services.AddControllers(options => options.Filters.Add<AntiforgeryFilter>());

    // HSTS: browsers that have seen the header use only HTTPS for this host for a year.
    // No includeSubDomains and no preload: a fork runs on someone else's domain, and those
    // commit every subdomain, for a long time, to something its owner never agreed to.
    builder.Services.AddHsts(options =>
    {
      options.MaxAge = TimeSpan.FromDays(365);
      options.IncludeSubDomains = false;
      options.Preload = false;
    });

    WebApplication app = builder.Build();

    // With sandboxes on, the cleanup must know every table a sandbox's rows can be in.
    bool canStart = await app.Services.GetRequiredService<StartupSchemaCheck>().RunAsync()
      && app.Services.GetRequiredService<DataProtectionCheck>().Run()
      && app.Services.GetRequiredService<EmailCheck>().Run()
      && (!demo.SandboxesEnabled || await app.Services.GetRequiredService<SandboxCleanup>().CheckCoverageAsync());
    if (!canStart)
    {
      return 1;
    }

    // Forwarded headers come first, before anything below reads the scheme or client address.
    // On App Service the deploy sets ASPNETCORE_FORWARDEDHEADERS_ENABLED=true, and ASP.NET Core
    // then adds the forwarded-headers middleware ahead of this pipeline by itself: Azure's
    // front end ends TLS and passes the original scheme and client IP in X-Forwarded-Proto and
    // X-Forwarded-For. Without it, Request.IsHttps is false (no HSTS header), and every
    // request seems to come from the front end (one sign-in rate limit for everyone).

    if (app.Environment.IsProduction())
    {
      app.UseHsts();
    }

    // No page of this app tells another site where the visitor came from. It matters most on
    // the reset page, whose address briefly holds a reset token: a link followed from there
    // must not hand the token to the next site in its Referer header. (index.html has the
    // same rule as a meta tag, for the Vite dev server.)
    app.Use((context, next) =>
    {
      context.Response.Headers["Referrer-Policy"] = "no-referrer";
      return next(context);
    });

    // Production: one host name for the whole site (see CanonicalHostMiddleware).
    // Empty in Development, so nothing is redirected locally.
    string? canonicalHost = app.Configuration["CanonicalHost"];
    if (!string.IsNullOrWhiteSpace(canonicalHost))
    {
      app.UseMiddleware<CanonicalHostMiddleware>(canonicalHost);
    }

    // Outside Development the API also serves the React app: the deploy pipeline copies
    // the Vite build (src/Web/dist) into wwwroot, so the browser gets the page and the
    // API from one origin, just like the Vite proxy gives it locally.
    // In Development the Vite dev server serves the front end, so there's no wwwroot.
    // Static files are answered here, before authentication: the sign-in page has to load.
    if (!app.Environment.IsDevelopment())
    {
      app.UseDefaultFiles();
      app.UseStaticFiles();
    }

    app.UseRouting();

    // Reads the auth cookie and runs the session check (AuthCookieEvents.ValidatePrincipal).
    app.UseAuthentication();
    app.UseMiddleware<RequestLogScopeMiddleware>();
    app.UseRateLimiter();

    // Enforces the fallback policy: signed in, unless the endpoint says [AllowAnonymous].
    app.UseAuthorization();

    app.MapControllers();

    // Deep links and refresh: /health is a React route, not a file, so the server answers it
    // with index.html and the router takes over. {*path:nonfile} skips anything that looks like
    // a file (MissingFileController answers /missing.js with a real 404), and unknown /api/*
    // paths never get here because ApiNotFoundController answers them first. This is endpoint routing plumbing, not a
    // minimal API endpoint. Public: the page itself holds no data, and it's what shows the
    // sign-in screen.
    if (!app.Environment.IsDevelopment())
    {
      app.MapFallbackToFile("{*path:nonfile}", "index.html").AllowAnonymous();
    }

    await app.RunAsync();
    return 0;
  }

  /// <summary>
  /// Demo sandboxes: a private user and tenants per visitor, from "Try the demo". Off by default.
  /// The store is always there (the session check asks it to keep a sandbox alive); the cleanup
  /// service only runs when sandboxes are on.
  /// </summary>
  private static DemoOptions AddDemoSandboxes(WebApplicationBuilder builder)
  {
    IConfigurationSection section = builder.Configuration.GetSection(DemoOptions.SectionName);
    DemoOptions demo = section.Get<DemoOptions>() ?? new DemoOptions();
    builder.Services.Configure<DemoOptions>(section);
    builder.Services.AddSingleton<SandboxStore>();
    builder.Services.AddSingleton<SandboxCleanup>();
    if (demo.SandboxesEnabled)
    {
      builder.Services.AddHostedService<SandboxCleanupService>();
    }

    return demo;
  }

  /// <summary>
  /// Cookie sign-in with our own user tables (no ASP.NET Core Identity), the session check,
  /// password hashing, antiforgery, password reset, and the rate limits.
  /// </summary>
  private static void AddAuthentication(WebApplicationBuilder builder, DemoOptions demo)
  {
    IConfigurationSection authSection = builder.Configuration.GetSection(AuthOptions.SectionName);
    AuthOptions authOptions = authSection.Get<AuthOptions>() ?? new AuthOptions();
    builder.Services.Configure<AuthOptions>(authSection);

    PasswordHashingOptions hashing = builder.Configuration.GetSection(PasswordHashingOptions.SectionName).Get<PasswordHashingOptions>()
      ?? new PasswordHashingOptions();
    builder.Services.AddSingleton<IPasswordHasher>(new Argon2idPasswordHasher(hashing));
    builder.Services.AddSingleton<UnknownUserHash>();
    builder.Services.AddSingleton<UserStore>();
    builder.Services.AddSingleton<PasswordResetStore>();
    builder.Services.AddSingleton<MemberStore>();
    builder.Services.AddSingleton<CustomerStore>();

    IConfigurationSection resetSection = builder.Configuration.GetSection(PasswordResetOptions.SectionName);
    PasswordResetOptions resetOptions = resetSection.Get<PasswordResetOptions>() ?? new PasswordResetOptions();
    builder.Services.Configure<PasswordResetOptions>(resetSection);

    // The session check caches what it reads for SessionCheckSeconds.
    builder.Services.AddMemoryCache();
    builder.Services.AddScoped<AuthCookieEvents>();

    // __Host- names and Secure always, except in Development (see AuthCookies for why).
    AuthCookies cookies = AuthCookies.For(builder.Environment.IsDevelopment());
    builder.Services.AddSingleton(cookies);

    builder.Services
      .AddAuthentication(AuthClaims.Scheme)
      .AddCookie(AuthClaims.Scheme, options =>
      {
        options.Cookie.Name = cookies.AuthName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = cookies.SecurePolicy;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Path = "/";
        options.ExpireTimeSpan = TimeSpan.FromHours(authOptions.SessionHours);
        options.SlidingExpiration = true;
        options.EventsType = typeof(AuthCookieEvents);
      });

    builder.Services.AddAuthorization(options =>
    {
      // Everything needs a signed-in user unless it opts out with [AllowAnonymous]. Forgetting
      // an attribute then fails closed (a 401), not open.
      options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

      // One policy per permission, named after it: [Authorize(Policy = Permissions.MembersManage)].
      // Signed out is a 401; signed in without the permission is a 403 (PermissionHandler).
      foreach (string permission in Permissions.All.Keys)
      {
        options.AddPolicy(permission, policy => policy
          .RequireAuthenticatedUser()
          .AddRequirements(new PermissionRequirement(permission)));
      }
    });
    builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();

    builder.Services.AddAntiforgery(options =>
    {
      options.HeaderName = AuthCookies.HeaderName;
      options.Cookie.Name = cookies.AntiforgeryName;
      options.Cookie.HttpOnly = true;
      options.Cookie.SecurePolicy = cookies.SecurePolicy;
      options.Cookie.SameSite = SameSiteMode.Strict;
      options.Cookie.Path = "/";
    });

    builder.Services.AddRateLimiter(options =>
    {
      options.OnRejected = (context, cancellationToken) =>
      {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
          context.HttpContext.Response.Headers.RetryAfter =
            ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        // Say which limit was hit: the password pages shouldn't talk about sign-in attempts.
        string? policy = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        (string title, string detail) = policy switch
        {
          PasswordResetOptions.RateLimitPolicyName => ("Too many password requests", "Wait a minute, then try again."),
          DemoController.RateLimitPolicyName => ("Too many demos started from here", "Wait a while, then try again."),
          _ => ("Too many sign-in attempts", "Wait a minute, then try again."),
        };

        return new ValueTask(ProblemResponses.WriteAsync(
          context.HttpContext,
          StatusCodes.Status429TooManyRequests,
          title,
          detail));
      };

      options.AddPolicy(SignInRateLimit.PolicyName, httpContext => RateLimitPartition.GetFixedWindowLimiter(
        SignInRateLimit.PartitionKey(httpContext),
        _ => new FixedWindowRateLimiterOptions
        {
          PermitLimit = authOptions.SignInPermitLimit,
          Window = TimeSpan.FromSeconds(authOptions.SignInWindowSeconds),
          QueueLimit = 0,
        }));

      // Forgot, reset, and change password: per client address, like sign-in. The per-address
      // email cap (PasswordReset:MaxEmailsPerHour) protects inboxes; this protects the server.
      options.AddPolicy(PasswordResetOptions.RateLimitPolicyName, httpContext => RateLimitPartition.GetFixedWindowLimiter(
        SignInRateLimit.PartitionKey(httpContext),
        _ => new FixedWindowRateLimiterOptions
        {
          PermitLimit = resetOptions.PermitLimit,
          Window = TimeSpan.FromSeconds(resetOptions.WindowSeconds),
          QueueLimit = 0,
        }));

      // "Try the demo": per client address, sliding, so the limit can't be doubled by
      // clicking just before and just after a fixed window turns over.
      options.AddPolicy(DemoController.RateLimitPolicyName, httpContext => RateLimitPartition.GetSlidingWindowLimiter(
        SignInRateLimit.PartitionKey(httpContext),
        _ => new SlidingWindowRateLimiterOptions
        {
          PermitLimit = demo.SandboxPermitLimit,
          Window = TimeSpan.FromMinutes(demo.SandboxWindowMinutes),
          SegmentsPerWindow = 6,
          QueueLimit = 0,
        }));
    });
  }
}
