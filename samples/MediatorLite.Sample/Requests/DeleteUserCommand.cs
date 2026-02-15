using MediatorLite;

namespace MediatorLite.Sample.Requests;

public record DeleteUserCommand(int Id) : IRequest;
