using System;
using System.Threading.Tasks;
using MessaggingService.Services;
using Microsoft.AspNetCore.Mvc;

namespace MessaggingService.Models
{
    [Route("api/[controller]")]
    [ApiController]
    public class MailController : ControllerBase
    {
        private readonly IMailService mailService;
        public MailController(IMailService mailService)
        {
            this.mailService = mailService;
        }
        [HttpPost("send")]
        public async Task<IActionResult> SendMail([FromForm] MailRequest request)
        {
            try
            {
                await mailService.SendEmailAsync(request);
                return Ok();
            }
            catch (Exception ex)
            {
                return new JsonResult(ex.Message) { StatusCode = 500 };
            }

        }

        [HttpPost("welcome")]
        public async Task<IActionResult> SendWelcomeMail([FromForm] WelcomeRequest request)
        {
            try
            {
                await mailService.SendWelcomeEmailAsync(request);
                return Ok();
            }
            catch (Exception ex)
            {

                return new JsonResult(ex.Message) { StatusCode = 500 };
            }

        }
    }
}