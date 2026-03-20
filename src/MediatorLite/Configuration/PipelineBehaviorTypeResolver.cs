namespace MediatorLite.Configuration;

internal static class PipelineBehaviorTypeResolver
{
    private static readonly Type OpenPipelineBehaviorType = typeof(IPipelineBehavior<,>);

    internal static IReadOnlyList<Type> GetServiceTypesForRegistration(Type behaviorType)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);

        if (behaviorType.IsGenericTypeDefinition)
        {
            ValidateOpenBehaviorType(behaviorType);
            return [OpenPipelineBehaviorType];
        }

        return GetClosedBehaviorInterfacesOrThrow(behaviorType);
    }

    internal static IReadOnlyList<Type> GetClosedBehaviorInterfacesOrThrow(Type behaviorType)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);

        if (behaviorType.IsGenericTypeDefinition)
        {
            throw new ArgumentException(
                $"Type '{behaviorType.FullName}' must be a closed behavior type. " +
                "Use AddOpenBehavior for open generic behavior types.",
                "behaviorType");
        }

        var closedInterfaces = GetClosedBehaviorInterfaces(behaviorType);
        if (closedInterfaces.Count == 0)
        {
            throw CreateInvalidBehaviorTypeException(behaviorType);
        }

        return closedInterfaces;
    }

    internal static Type GetClosedBehaviorInterfaceForInvocation(
        Type behaviorType,
        Type requestType,
        Type responseType)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(responseType);

        var matchingInterface = GetClosedBehaviorInterfaces(behaviorType)
            .FirstOrDefault(i =>
            {
                var arguments = i.GetGenericArguments();
                return arguments[0] == requestType && arguments[1] == responseType;
            });

        if (matchingInterface != null)
        {
            return matchingInterface;
        }

        throw new InvalidOperationException(
            $"Cannot invoke behavior {behaviorType.Name} for request type {requestType.Name}. " +
            "Ensure the behavior implements IPipelineBehavior<,> for the resolved request/response pair.");
    }

    internal static void ValidateOpenBehaviorType(Type behaviorType)
    {
        ArgumentNullException.ThrowIfNull(behaviorType);

        if (!behaviorType.IsGenericTypeDefinition)
        {
            throw new ArgumentException(
                $"Type {behaviorType.Name} must be an open generic type definition.",
                "behaviorType");
        }

        var hasCorrectArity = behaviorType.GetGenericArguments().Length == 2;
        if (!hasCorrectArity)
        {
            throw new ArgumentException(
                $"Type {behaviorType.Name} must have exactly 2 generic type parameters.",
                "behaviorType");
        }

        var implementsPipelineBehavior = behaviorType.GetInterfaces()
            .Any(IsPipelineBehaviorInterface);

        if (!implementsPipelineBehavior)
        {
            throw CreateInvalidBehaviorTypeException(behaviorType);
        }
    }

    private static List<Type> GetClosedBehaviorInterfaces(Type behaviorType)
    {
        return behaviorType.GetInterfaces()
            .Where(IsPipelineBehaviorInterface)
            .Where(i => !i.ContainsGenericParameters)
            .Distinct()
            .ToList();
    }

    private static bool IsPipelineBehaviorInterface(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == OpenPipelineBehaviorType;
    }

    private static ArgumentException CreateInvalidBehaviorTypeException(Type behaviorType)
    {
        return new ArgumentException(
            $"Type '{behaviorType.FullName}' does not implement IPipelineBehavior<TRequest, TResponse>. " +
            "Ensure the behavior type implements the IPipelineBehavior<,> interface.",
            "behaviorType");
    }
}