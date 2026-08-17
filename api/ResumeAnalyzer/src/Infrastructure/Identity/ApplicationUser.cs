using Microsoft.AspNetCore.Identity;

namespace ResumeAnalyzer.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
