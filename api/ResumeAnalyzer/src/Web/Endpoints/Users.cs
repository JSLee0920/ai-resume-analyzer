using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using ResumeAnalyzer.Web.Contracts;
using ResumeAnalyzer.Infrastructure.Identity;
using ResumeAnalyzer.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.BearerToken;

namespace ResumeAnalyzer.Web.Endpoints;

public class Users : IEndpointGroup
{
    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.MapPost(Register, "register")
            .AddEndpointFilter<ValidationFilter<RegisterRequest>>()
            .RequireRateLimiting("auth");

        groupBuilder.MapPost(Login, "login")
            .AddEndpointFilter<ValidationFilter<LoginRequest>>()
            .RequireRateLimiting("auth");

        groupBuilder.MapPost(Refresh, "refresh");

        groupBuilder.MapPost(ConfirmEmail, "confirm-email")
            .AddEndpointFilter<ValidationFilter<ConfirmEmailRequest>>()
            .RequireRateLimiting("auth");

        groupBuilder.MapPost(ResendConfirmation, "resend-confirmation")
            .AddEndpointFilter<ValidationFilter<ResendConfirmationRequest>>()
            .RequireRateLimiting("auth");

        groupBuilder.MapGet(Me, "me").RequireAuthorization();
    }

    [EndpointSummary("Register")]
    [EndpointDescription("Create a new user account and send a confirmation email")]
    public static async Task<Results<Ok, ValidationProblem>> Register(
        UserManager<ApplicationUser> userManager,
        IEmailService emailService,
        IConfiguration configuration,
        ILogger<Users> logger,
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return TypedResults.ValidationProblem(result.ToValidationErrors());

        await SendConfirmationEmailAsync(
            userManager, emailService, configuration, logger, user, cancellationToken);

        return TypedResults.Ok();
    }

    [EndpointSummary("Log In")]
    [EndpointDescription("Returns an access token and refresh token")]
    public static async Task<Results<EmptyHttpResult, ProblemHttpResult>> Login(
        SignInManager<ApplicationUser> signInManager,
        LoginRequest request)
    {
        signInManager.AuthenticationScheme = IdentityConstants.BearerScheme;

        var result = await signInManager.PasswordSignInAsync(
            request.Email,
            request.Password,
            isPersistent: false,
            lockoutOnFailure: true);

        if (result.IsNotAllowed)
            return TypedResults.Problem(
                "Confirm your email address before signing in.",
                statusCode: StatusCodes.Status403Forbidden,
                title: "Email not confirmed");

        if (result.IsLockedOut)
            return TypedResults.Problem(
                "Too many failed attempts. This account is temporarily locked.",
                statusCode: StatusCodes.Status423Locked,
                title: "Account locked");

        if (!result.Succeeded)
            return TypedResults.Problem("Invalid Credentials", statusCode: StatusCodes.Status401Unauthorized);

        // the bearer handler already wrote {accessToken, refreshToken, expiresIn}
        return TypedResults.Empty;
    }

    [EndpointSummary("Refresh Token")]
    [EndpointDescription("Returns an access token and refresh token")]
    public static async Task<Results<SignInHttpResult, UnauthorizedHttpResult>> Refresh(
       SignInManager<ApplicationUser> signInManager,
       IOptionsMonitor<BearerTokenOptions> bearerOptions,
       TimeProvider timeProvider,
       RefreshRequest request)
    {
        var protector = bearerOptions.Get(IdentityConstants.BearerScheme).RefreshTokenProtector;
        var ticket = protector.Unprotect(request.RefreshToken);

        if (ticket?.Properties.ExpiresUtc is not { } expiresUtc || timeProvider.GetUtcNow() >= expiresUtc || ticket.Principal is null)
        {
            return TypedResults.Unauthorized();
        }

        var user = await signInManager.ValidateSecurityStampAsync(ticket.Principal);
        if (user is null) return TypedResults.Unauthorized();

        var principal = await signInManager.CreateUserPrincipalAsync(user);

        return TypedResults.SignIn(principal, authenticationScheme: IdentityConstants.BearerScheme);
    }

    [EndpointSummary("Confirm email")]
    [EndpointDescription("Completes registration using the link emailed to the user")]
    public static async Task<Results<Ok, ValidationProblem>> ConfirmEmail(
        UserManager<ApplicationUser> userManager,
        ConfirmEmailRequest request)
    {
        var user = await userManager.FindByIdAsync(request.UserId);
        if (user is null)
            return TypedResults.ValidationProblem(InvalidConfirmationLink);

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Code));
        }
        catch (FormatException)
        {
            return TypedResults.ValidationProblem(InvalidConfirmationLink);
        }

        var result = await userManager.ConfirmEmailAsync(user, token);

        return result.Succeeded
            ? TypedResults.Ok()
            : TypedResults.ValidationProblem(InvalidConfirmationLink);
    }

    [EndpointSummary("Resend confirmation email")]
    [EndpointDescription("Always returns 200 so it cannot be used to enumerate accounts")]
    public static async Task<Ok> ResendConfirmation(
        UserManager<ApplicationUser> userManager,
        IEmailService emailService,
        IConfiguration configuration,
        ILogger<Users> logger,
        ResendConfirmationRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);

        if (user is not null && !await userManager.IsEmailConfirmedAsync(user))
        {
            await SendConfirmationEmailAsync(
                userManager, emailService, configuration, logger, user, cancellationToken);
        }

        return TypedResults.Ok();
    }

    [EndpointSummary("Current user")]
    public static Ok<UserDto> Me(ClaimsPrincipal claims) =>
        TypedResults.Ok(new UserDto(
            claims.FindFirstValue(ClaimTypes.NameIdentifier)!,
            claims.FindFirstValue(ClaimTypes.Email)!,
            claims.FindFirstValue(ApplicationUserClaimsPrincipalFactory.DisplayNameClaimType),
            claims.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray()));

    private static readonly Dictionary<string, string[]> InvalidConfirmationLink =
        new() { ["InvalidToken"] = ["The confirmation link is invalid or has expired."] };

    private static async Task SendConfirmationEmailAsync(
        UserManager<ApplicationUser> userManager,
        IEmailService emailService,
        IConfiguration configuration,
        ILogger logger,
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);

        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        var baseUrl = configuration["App:ClientBaseUrl"]!.TrimEnd('/');
        var link = $"{baseUrl}/confirm-email?userId={Uri.EscapeDataString(user.Id)}&code={code}";

        try
        {
            await emailService.SendEmailConfirmationAsync(user.Email!, link, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send confirmation email for user {UserId}", user.Id);
        }
    }
}
