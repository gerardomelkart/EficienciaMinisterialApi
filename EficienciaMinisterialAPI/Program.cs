using Serilog;
using Serilog.Events;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using EficienciaMinisterialAPI.Models;
using EficienciaMinisterialAPI.Data;
using EficienciaMinisterialAPI.Services;
using EficienciaMinisterialAPI.Repositories;

var argumentos = args.Where(argumento => argumento != "--crear-superusuario").ToArray();
var builder = WebApplication.CreateBuilder(argumentos);
var directorioLogs = Path.GetFullPath(builder.Configuration["Logging:Directorio"] ?? "logs", builder.Environment.ContentRootPath);
Directory.CreateDirectory(directorioLogs);
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(directorioLogs, "eficiencia-ministerial-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 31,
                    fileSizeLimitBytes: 10_000_000,
                    rollOnFileSizeLimit: true,
                    shared: true,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{TraceId}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();
builder.Host.UseSerilog();
try
{
    builder.Services.AddControllers();
    builder.Services.AddProblemDetails();
    builder.Services.AddOpenApi();
    builder.Services.AddScoped<SqlConnectionFactory>();
    builder.Services.AddScoped<UsuarioService>();
    builder.Services.AddScoped<UsuarioRepository>();
    builder.Services.AddScoped<CatalogoRepository>();
    builder.Services.AddScoped<ExcelCargaService>();
    builder.Services.AddScoped<CargaRepository>();
    if(args.Contains("--crear-superusuario"))
    {
        await using var provisionamiento = builder.Build();
        using var scope = provisionamiento.Services.CreateScope();
        var nombre = Environment.GetEnvironmentVariable("EM_ADMIN_USUARIO") ?? throw new InvalidOperationException("Falta EM_ADMIN_USUARIO.");
        var password = Environment.GetEnvironmentVariable("EM_ADMIN_PASSWORD") ?? throw new InvalidOperationException("Falta EM_ADMIN_PASSWORD.");
        var request = new CrearUsuarioRequest(nombre, nombre, password, "SUPER_USUARIO", null, "NACIONAL", null);
        System.ComponentModel.DataAnnotations.Validator.ValidateObject(request, new System.ComponentModel.DataAnnotations.ValidationContext(request), true);
        await scope.ServiceProvider.GetRequiredService<UsuarioService>().CrearAsync(request, null);
        Console.WriteLine("Superusuario creado.");
        return;
    }
    var clave = builder.Configuration["Jwt:Key"] ?? "";
    if(Encoding.UTF8.GetByteCount(clave) < 32)
    {
        throw new InvalidOperationException("Configure Jwt__Key con al menos 32 bytes aleatorios fuera del repositorio.");
    }
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"], ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(clave)), ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if(!int.TryParse(id, out var usuarioId))
                {
                    context.Fail("Sesión inválida.");
                    return;
                }
                var usuario = await context.HttpContext.RequestServices.GetRequiredService<UsuarioService>().ObtenerAsync(usuarioId);
                if(usuario == null)
                {
                    context.Fail("Usuario inactivo.");
                    return;
                }
                ((ClaimsIdentity)context.Principal!.Identity!).AddClaim(new Claim(ClaimTypes.Role, usuario.Rol));
                context.HttpContext.Items["Usuario"] = usuario;
            }
        };
    });
    builder.Services.AddAuthorization();
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    });
    var app = builder.Build();
    // Solo se aceptan encabezados de proxies de loopback (lista predeterminada del middleware).
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    });
    app.UseSerilogRequestLogging(options =>
    {
        options.EnrichDiagnosticContext = (diagnostico, contexto) =>
        {
            diagnostico.Set("TraceId", contexto.TraceIdentifier);
        };
    });
    app.UseExceptionHandler(handler => handler.Run(async context =>
    {
        var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var status = error is ArgumentException ? 400 : error is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 } ? 409 : 500;
        context.Response.StatusCode = status;
        if(status == 500)
        {
            app.Logger.LogError(error, "Error de solicitud {TraceId}", context.TraceIdentifier);
        }
        await context.Response.WriteAsJsonAsync(new { mensaje = status == 400 ? error!.Message : status == 409 ? "El registro ya existe." : "No fue posible completar la operación.", traceId = context.TraceIdentifier });
    }));
    app.Use(async (context, next) =>
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        await next();
    });
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    if(app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }
    app.MapGet("/api/estado", () => Results.Ok(new { estado = "OK", sistema = "Eficiencia Ministerial" })).AllowAnonymous();
    app.MapControllers();
    app.Run();
}
catch(Exception ex)
{
    Log.Fatal(ex, "La API terminó por un error no controlado.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
