namespace ResumeAnalyzer.Web.Contracts;

public record RegisterRequest(string Email, string Password, string? DisplayName = null);
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record ConfirmEmailRequest(string UserId, string Code);
public record ResendConfirmationRequest(string Email);
public record UserDto(string Id, string? Email, string? DisplayName, string[] Roles);
