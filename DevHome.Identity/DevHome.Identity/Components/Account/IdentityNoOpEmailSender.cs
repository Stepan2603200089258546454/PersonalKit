using DevHome.Identity.DataContext.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace DevHome.Identity.Components.Account
{
    // После появления реальной отправки писем уберите ветку "else if (EmailSender is IdentityNoOpEmailSender)" из RegisterConfirmation.razor.
    internal sealed class IdentityNoOpEmailSender : IEmailSender<ApplicationUser>
    {
        private readonly IEmailSender emailSender = new NoOpEmailSender();

        public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
            emailSender.SendEmailAsync(email, "Подтвердите адрес электронной почты", $"Подтвердите учётную запись, перейдя по <a href='{confirmationLink}'>этой ссылке</a>. Если вы не запрашивали подтверждение, просто проигнорируйте это письмо.");

        public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
            emailSender.SendEmailAsync(email, "Сброс пароля", $"Чтобы сбросить пароль, перейдите по <a href='{resetLink}'>этой ссылке</a>. Если вы не запрашивали сброс пароля, просто проигнорируйте это письмо.");

        public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
            emailSender.SendEmailAsync(email, "Сброс пароля", $"Чтобы сбросить пароль, используйте следующий код: {resetCode}. Если вы не запрашивали сброс пароля, просто проигнорируйте это письмо.");
    }
}
