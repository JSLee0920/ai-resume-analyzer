using System;
using System.Collections.Generic;
using System.Text;

namespace ResumeAnalyzer.Infrastructure.Email;

public class ResendOptions
{
    public const string SectionName = "Resend";
    public string ApiKey { get; set; } = string.Empty;
    public string FromEmail { get; set; } = "onboarding@resend.dev";
    public string FromName {  get; set; } = "Resume Analyzer";
}
