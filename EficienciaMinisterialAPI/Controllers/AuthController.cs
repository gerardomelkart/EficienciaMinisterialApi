using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using EficienciaMinisterialAPI.Models;
using EficienciaMinisterialAPI.Services;
using EficienciaMinisterialAPI.Repositories;

namespace EficienciaMinisterialAPI.Controllers;
[ApiController]
[Route("api/auth")]
public sealed class AuthController(UsuarioService usuarios, IConfiguration configuration, ILogger<AuthController> logger, ConfiguracionRepository configuracion) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        if(System.Text.Encoding.UTF8.GetByteCount(request.Password) > 72)
        {
            logger.LogWarning("Intento de acceso rechazado. TraceId: {TraceId}", HttpContext.TraceIdentifier);
            return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });
        }
        var usuario = await usuarios.AutenticarAsync(request);
        if(usuario == null)
        {
            logger.LogWarning("Intento de acceso rechazado. TraceId: {TraceId}", HttpContext.TraceIdentifier);
            return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });
        }
        logger.LogInformation("Inicio de sesión correcto. UsuarioId: {UsuarioId}. TraceId: {TraceId}", usuario.Id, HttpContext.TraceIdentifier);
        var vence = DateTime.UtcNow.AddMinutes((await configuracion.ObtenerAsync()).Valor<int>("Seguridad.TokenMinutos"));
        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"], audience: configuration["Jwt:Audience"],
            claims: [new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString())],
            expires: vence,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!)), SecurityAlgorithms.HmacSha256));
        Response.Headers.CacheControl = "no-store";
        return Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token), venceUtc = vence, usuario });
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me() => Ok(HttpContext.Items["Usuario"]);
}
