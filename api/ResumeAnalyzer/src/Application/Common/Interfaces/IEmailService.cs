using System;
using System.Collections.Generic;
using System.Text;

namespace ResumeAnalyzer.Application.Common.Interfaces;

public interface IEmailService
{
    Task SendEmailConfirmationAsync(string toEmail, string confirmationLink, CancellationToken cancellationToken = default);
    Task SendPasswordResetAsync(string toEmail, string resetLink, CancellationToken cancellationToken = default);

}
