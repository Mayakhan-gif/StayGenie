using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using StayGenie.Data;
using StayGenie.Services;

namespace StayGenie.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")] // Ek IP se ek minute mein limited login/register attempts
public class AuthController(HotelDbContext db, JwtTokenService jwt) : ControllerBase
{
    // DTO = sirf wo data jo client se lena hai. Validation rules yahin likhte hain.
    public record RegisterRequest(
        [Required, StringLength(100, MinimumLength = 2)] string Name,
        [Required, EmailAddress, StringLength(256)] string Email,
        [Required, StringLength(100, MinimumLength = 8)] string Password);

    public record LoginRequest(
        [Required] string Email,
        [Required] string Password);

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();

        if (await db.Users.AnyAsync(u => u.Email == email))
            return Conflict(new { message = "Email already registered." });

        var user = new User
        {
            Name = req.Name.Trim(),
            Email = email,
            PasswordHash = PasswordHasher.Hash(req.Password),
            Role = "Customer" // naya user hamesha Customer, Admin nahi
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return Ok(new { token = jwt.GenerateToken(user), name = user.Name, role = user.Role });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        // Dono surtein (email ghalat / password ghalat) ka same jawab, taake hacker ko hint na mile
        if (user is null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { message = "Invalid email or password." });

        return Ok(new { token = jwt.GenerateToken(user), name = user.Name, role = user.Role });
    }
}