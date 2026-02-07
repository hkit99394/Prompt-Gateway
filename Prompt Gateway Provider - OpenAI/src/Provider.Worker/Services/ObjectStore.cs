namespace Provider.Worker.Services;

public interface IObjectStore
{
    Task<string> GetObjectTextAsync(string bucket, string key, CancellationToken cancellationToken);

    Task<string> PutObjectTextAsync(
        string bucket,
        string key,
        string content,
        string contentType,
        CancellationToken cancellationToken);
}
