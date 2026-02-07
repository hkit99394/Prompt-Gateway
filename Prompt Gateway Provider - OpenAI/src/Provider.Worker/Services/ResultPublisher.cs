using Provider.Worker.Models;

namespace Provider.Worker.Services;

public interface IResultPublisher
{
    Task PublishAsync(ResultEvent resultEvent, CancellationToken cancellationToken);
}
