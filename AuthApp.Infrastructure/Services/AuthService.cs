using AuthApp.Application.DTOs;
using AuthApp.Application.Interfaces;
using AuthApp.Domain.Entities;
using AuthApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;
using Microsoft.Extensions.Configuration;
namespace AuthApp.Infrastructure.Services;
public class AuthService : IAuthService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;

    public AuthService(AppDbContext context, IConfiguration config)
    {
        _context = context;
        _config = config;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        if (await _context.Users.AnyAsync(u => u.UserName == request.Username))
            //TODO : Use a more specific exception type or create a custom exception
            throw new Exception("Username already exists");

        var user = new User
        {
            UserName = request.Username,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = request.Role
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return await GenerateTokensAsync(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == request.Username);
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new Exception("Invalid credentials");

        return await GenerateTokensAsync(user);
    }

    public async Task<AuthResponse> RefreshTokenAsync(string token)
    {
        var refreshToken = await _context.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == token && !r.IsRevoked && r.Expires > DateTime.UtcNow);

        if (refreshToken == null)
            throw new Exception("Invalid refresh token");

        refreshToken.IsRevoked = true;
        await _context.SaveChangesAsync();

        return await GenerateTokensAsync(refreshToken.User);
    }

    private async Task<AuthResponse> GenerateTokensAsync(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.UserName),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds
        );

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        var refreshToken = new RefreshToken
        {
            Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            Expires = DateTime.UtcNow.AddDays(7),
            UserId = user.Id
        };

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            Expiration = token.ValidTo
        };
    }

}
