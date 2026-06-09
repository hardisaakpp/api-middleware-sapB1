namespace Middleware.Application.Abstractions;

public interface IUseCase<TRequest, TResponse>
{
    public Task<TResponse> ExecuteAsync(TRequest request, CancellationToken cancellationToken = default);
}
