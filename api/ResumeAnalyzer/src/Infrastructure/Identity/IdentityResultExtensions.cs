using ResumeAnalyzer.Application.Common.Models;
using Microsoft.AspNetCore.Identity;

namespace ResumeAnalyzer.Infrastructure.Identity;

public static class IdentityResultExtensions
{
    public static Result ToApplicationResult(this IdentityResult result)
    {
        return result.Succeeded
            ? Result.Success()
            : Result.Failure(result.Errors.Select(e => e.Description));
    }

    public static IDictionary<string, string[]> ToValidationErrors(this IdentityResult result) => 
        result.Errors
            .GroupBy(e => e.Code, e => e.Description)
            .ToDictionary(g => g.Key, g => g.ToArray());
}
