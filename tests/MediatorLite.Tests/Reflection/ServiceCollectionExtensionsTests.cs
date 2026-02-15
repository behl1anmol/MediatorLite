using MediatorLite.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    [MediatorGeneration(Skip = true)]
    public class TestBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public ValueTask<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default)
        {
            return next();
        }
    }
}
