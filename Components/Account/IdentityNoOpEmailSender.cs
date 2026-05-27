using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using MiniFinance.Data;

namespace MiniFinance.Components.Account;

// Remove the "else if (EmailSender is IdentityNoOpEmailSender)" block from RegisterConfirmation.razor after updating with a real implementation.
internal sealed class IdentityNoOpEmailSender : IEmailSender<ApplicationUser>
{
    private readonly IEmailSender emailSender = new NoOpEmailSender();

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        emailSender.SendEmailAsync(email, "Подтверждение email — MiniFinance",
            $"Подтвердите учётную запись, перейдя по <a href='{confirmationLink}'>ссылке</a>.");

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        emailSender.SendEmailAsync(email, "Сброс пароля — MiniFinance",
            $"Сбросьте пароль, перейдя по <a href='{resetLink}'>ссылке</a>.");

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        emailSender.SendEmailAsync(email, "Код сброса пароля — MiniFinance",
            $"Код для сброса пароля: {resetCode}");
}
