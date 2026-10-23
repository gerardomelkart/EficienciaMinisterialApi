using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using EficienciaMinisterialAPI.Models;
using EficienciaMinisterialAPI.Services;

namespace EficienciaMinisterialAPI.Controllers;
[ApiController]
[Route("api/auth")]
public sealed class AuthController(UsuarioService usuarios, IConfiguration configuration) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        if(System.Text.Encoding.UTF8.GetByteCount(request.Password) > 72)
        {
            return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });
        }
        var usuario = await usuarios.AutenticarAsync(request);
        if(usuario == null)
        {
            return Unauthorized(new { mensaje = "Usuario o contraseña incorrectos." });
        }
        var vence = DateTime.UtcNow.AddHours(1);
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
