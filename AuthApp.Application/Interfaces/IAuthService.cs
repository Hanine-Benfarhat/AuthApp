using AuthApp.Application.DTOs;
namespace AuthApp.Application.Interfaces;
public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<AuthResponse> RefreshTokenAsync(string token);
}