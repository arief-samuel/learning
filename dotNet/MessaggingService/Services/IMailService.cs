using System.Threading.Tasks;
using MessaggingService.Models;

namespace MessaggingService.Services
{
    public interface IMailService
    {
        Task SendEmailAsync(MailRequest mailRequest);
        Task SendWelcomeEmailAsync(WelcomeRequest request);
    }
}