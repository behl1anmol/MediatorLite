using MediatorLite.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Xunit;

namespace MediatorLite.Tests.Reflection;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMediatorLite_WithoutConfigure_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatorLite();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        Assert.NotNull(mediator);
    }

    [Fact]
    public void AddMediatorLite_WithConfigure_RegistersServicesWithOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatorLite(options =>
        {
            options.NotificationExecutionStrategy = NotificationExecutionStrategy.Parallel;
            options.NotificationErrorStrategy = NotificationErrorStrategy.ContinueAndAggregate;
            options.EnableBuiltInLogging = false;
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<MediatorOptions>();
        Assert.Equal(NotificationExecutionStrategy.Parallel, options.NotificationExecutionStrategy);
        Assert.Equal(NotificationErrorStrategy.ContinueAndAggregate, options.NotificationErrorStrategy);
        Assert.False(options.EnableBuiltInLogging);
    }

    [Fact]
    public void AddMediatorLite_WithBehavior_RegistersBehavior()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatorLite(options =>
        {
            options.AddOpenBehavior(typeof(TestBehavior<,>));
        });

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        Assert.NotNull(mediator);
    }

    [Fact]
    public void AddMediatorBehavior_WithClosedBehavior_RegistersSuccessfully()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatorLite();
        services.AddMediatorBehavior<TestBehavior<TestRequest, TestResponse>>(ServiceLifetime.Transient);

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        Assert.NotNull(mediator);
    }

    [Fact]
    public void AddMediatorLite_WithMultiInterfaceClosedBehavior_RegistersAllBehaviorInterfaces()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatorLite(options =>
        {
            options.AddBehavior<MultiInterfaceBehavior>();
        });

        var provider = services.BuildServiceProvider();

        var firstBehaviors = provider.GetServices<IPipelineBehavior<FirstRequest, FirstResponse>>().ToArray();
        var secondBehaviors = provider.GetServices<IPipelineBehavior<SecondRequest, SecondResponse>>().ToArray();

        Assert.Single(firstBehaviors);
        Assert.IsType<MultiInterfaceBehavior>(firstBehaviors[0]);

        Assert.Single(secondBehaviors);
        Assert.IsType<MultiInterfaceBehavior>(secondBehaviors[0]);
    }

    [Fact]
    public void AddMediatorBehavior_WithMultiInterfaceClosedBehavior_RegistersAllBehaviorInterfaces()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatorLite();
        services.AddMediatorBehavior<MultiInterfaceBehavior>();

        var provider = services.BuildServiceProvider();

        var firstBehaviors = provider.GetServices<IPipelineBehavior<FirstRequest, FirstResponse>>().ToArray();
        var secondBehaviors = provider.GetServices<IPipelineBehavior<SecondRequest, SecondResponse>>().ToArray();

        Assert.Single(firstBehaviors);
        Assert.IsType<MultiInterfaceBehavior>(firstBehaviors[0]);

        Assert.Single(secondBehaviors);
        Assert.IsType<MultiInterfaceBehavior>(secondBehaviors[0]);
    }

    [Fact]
    public void AddMediatorLite_And_AddMediatorBehavior_InvalidBehavior_UseConsistentArgumentExceptionContract()
    {
        var servicesUsingOptions = new ServiceCollection();
        servicesUsingOptions.AddLogging();

        var addMediatorLiteException = Assert.Throws<ArgumentException>(() =>
            servicesUsingOptions.AddMediatorLite(options => AddInvalidBehaviorType(options, typeof(InvalidClosedBehavior))));

        var servicesUsingDirectRegistration = new ServiceCollection();
        servicesUsingDirectRegistration.AddLogging();

        var addBehaviorException = Assert.Throws<ArgumentException>(() =>
            servicesUsingDirectRegistration.AddMediatorBehavior<InvalidClosedBehavior>());

        Assert.Equal("behaviorType", addMediatorLiteException.ParamName);
        Assert.Equal(addMediatorLiteException.ParamName, addBehaviorException.ParamName);
        Assert.Contains("IPipelineBehavior", addMediatorLiteException.Message);
        Assert.Contains("IPipelineBehavior", addBehaviorException.Message);
    }

    [Fact]
    public void AddMediatorLite_MediatorLifetime_Transient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatorLite(options =>
        {
            options.MediatorLifetime = ServiceLifetime.Transient;
        });

        var provider = services.BuildServiceProvider();
        var mediator1 = provider.GetRequiredService<IMediator>();
        var mediator2 = provider.GetRequiredService<IMediator>();
        Assert.NotSame(mediator1, mediator2);
    }

    [Fact]
    public void AddMediatorLite_MediatorLifetime_Singleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatorLite(options =>
        {
            options.MediatorLifetime = ServiceLifetime.Singleton;
        });

        var provider = services.BuildServiceProvider();
        var mediator1 = provider.GetRequiredService<IMediator>();
        var mediator2 = provider.GetRequiredService<IMediator>();
        Assert.Same(mediator1, mediator2);
    }

    [Fact]
    public void AddMediatorLite_HandlerLifetime_Scoped()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatorLite(options =>
        {
            options.HandlerLifetime = ServiceLifetime.Scoped;
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<MediatorOptions>();
        Assert.Equal(ServiceLifetime.Scoped, options.HandlerLifetime);
    }

    [Fact]
    public void AddMediatorLite_WithMultipleBehaviors_RegistersAll()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatorLite(options =>
        {
            options.AddOpenBehavior(typeof(TestBehavior<,>));
            options.AddBehavior<TestBehavior<TestRequest, TestResponse>>();
        });

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        Assert.NotNull(mediator);
    }

    // Test types
    public class TestRequest : IRequest<TestResponse> { }
    public class TestResponse { }

    public class FirstRequest : IRequest<FirstResponse> { }
    public class FirstResponse { }

    public class SecondRequest : IRequest<SecondResponse> { }
    public class SecondResponse { }

    [MediatorGeneration(Skip = true)]
    public class TestBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public ValueTask<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default)
        {
            return next();
        }
    }

    [MediatorGeneration(Skip = true)]
    public class MultiInterfaceBehavior :
        IPipelineBehavior<FirstRequest, FirstResponse>,
        IPipelineBehavior<SecondRequest, SecondResponse>
    {
        public ValueTask<FirstResponse> HandleAsync(FirstRequest request, RequestHandlerDelegate<FirstResponse> next, CancellationToken cancellationToken = default)
        {
            return next();
        }

        public ValueTask<SecondResponse> HandleAsync(SecondRequest request, RequestHandlerDelegate<SecondResponse> next, CancellationToken cancellationToken = default)
        {
            return next();
        }
    }

    public class InvalidClosedBehavior
    {
        // Not implementing IPipelineBehavior
    }

    private static void AddInvalidBehaviorType(MediatorOptions options, Type behaviorType)
    {
        var behaviorField = typeof(MediatorOptions).GetField("_behaviorTypes", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(behaviorField);

        var behaviorTypes = behaviorField.GetValue(options) as List<Type>;
        Assert.NotNull(behaviorTypes);

        behaviorTypes.Add(behaviorType);
    }
}
