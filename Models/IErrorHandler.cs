using Microsoft.Extensions.Logging;

namespace CountryTelegramBot.Models
{
    public interface IErrorHandler
    {
        void HandleError(Exception ex, string context = "");
    }

    public class DefaultErrorHandler : IErrorHandler
    {
        private readonly ILogger<DefaultErrorHandler> _logger;
        
        public DefaultErrorHandler(ILogger<DefaultErrorHandler> logger)
        {
            _logger = logger;
        }
        
        public void HandleError(Exception ex, string context = "")
        {
            _logger.LogError(ex, $"{context}");
        }
    }
}