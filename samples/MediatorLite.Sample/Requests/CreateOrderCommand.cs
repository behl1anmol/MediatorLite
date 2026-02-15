using MediatorLite;
using System.ComponentModel.DataAnnotations;

namespace MediatorLite.Sample.Requests;

public record CreateOrderCommand(
    [property: Required] string ProductName,
    [property: Range(1, 100)] int Quantity,
    [property: Range(0.01, 10000)] decimal Price) : IRequest<int>;
