using MediatorLite;

namespace MediatorLite.Sample.Requests;

public record GetUserQuery(int Id) : IRequest<User>;
