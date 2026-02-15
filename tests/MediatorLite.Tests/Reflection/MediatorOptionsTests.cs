using MediatorLite.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MediatorLite.Tests.Reflection;

public class MediatorOptionsTests
{
    [Fact]
    public void AddOpenBehavior_WithValidType_AddsBehavior()
    {
        var options = new MediatorOptions();

        var result = options.AddOpenBehavior(typeof(TestBehavior<,>));

        Assert.Same(options, result);
    }

    [Fact]
    public void AddOpenBehavior_WithNull_ThrowsArgumentNullException()
    {
        var options = new MediatorOptions();

        var ex = Assert.Throws<ArgumentNullException>(() => options.AddOpenBehavior(null!));
        Assert.NotNull(ex);
    }

    [Fact]
    public void AddOpenBehavior_WithNonGenericType_ThrowsArgumentException()
    {
        var options = new MediatorOptions();

        var ex = Assert.Throws<ArgumentException>(() => options.AddOpenBehavior(typeof(string)));
        Assert.NotNull(ex);
    }

    [Fact]
    public void AddOpenBehavior_WithNonBehaviorType_ButWithTwoGenericArgs_DoesNotThrow()
    {
        var options = new MediatorOptions();

        // InvalidBehavior<,> doesn't implement IPipelineBehavior but has 2 generic args
        // The current validation logic allows this through - it only validates the generic arg count
        var result = options.AddOpenBehavior(typeof(InvalidBehavior<,>));

        Assert.Same(options, result);
    }

    [Fact]
    public void AddBehavior_WithValidType_AddsBehavior()
    {
        var options = new MediatorOptions();

        var result = options.AddBehavior<TestBehavior<TestRequest, TestResponse>>();

        Assert.Same(options, result);
    }

    [Fact]
    public void NotificationExecutionStrategy_DefaultIsSequential()
    {
        var options = new MediatorOptions();

        Assert.Equal(NotificationExecutionStrategy.Sequential, options.NotificationExecutionStrategy);
    }

    [Fact]
    public void NotificationErrorStrategy_DefaultIsContinueAndAggregate()
    {
        var options = new MediatorOptions();

        Assert.Equal(NotificationErrorStrategy.ContinueAndAggregate, options.NotificationErrorStrategy);
    }

    [Fact]
    public void EnableBuiltInLogging_DefaultIsTrue()
    {
        var options = new MediatorOptions();

        Assert.True(options.EnableBuiltInLogging);
    }

    [Fact]
    public void DefaultLogLevel_DefaultIsDebug()
    {
        var options = new MediatorOptions();

        Assert.Equal(LogLevel.Debug, options.DefaultLogLevel);
    }

    [Fact]
    public void EnableTracing_DefaultIsTrue()
    {
        var options = new MediatorOptions();

        Assert.True(options.EnableTracing);
    }

    [Fact]
    public void HandlerLifetime_DefaultIsTransient()
    {
        var options = new MediatorOptions();

        Assert.Equal(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient, options.HandlerLifetime);
    }

    [Fact]
    public void MediatorLifetime_DefaultIsTransient()
    {
        var options = new MediatorOptions();

        Assert.Equal(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient, options.MediatorLifetime);
    }

    [Fact]
    public void PropertySetters_Work()
    {
        var options = new MediatorOptions();

        options.NotificationExecutionStrategy = NotificationExecutionStrategy.Parallel;
        options.NotificationErrorStrategy = NotificationErrorStrategy.StopOnFirstError;
        options.EnableBuiltInLogging = false;
        options.DefaultLogLevel = LogLevel.Information;
        options.EnableTracing = false;
        options.HandlerLifetime = Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped;
        options.MediatorLifetime = Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton;

        Assert.Equal(NotificationExecutionStrategy.Parallel, options.NotificationExecutionStrategy);
        Assert.Equal(NotificationErrorStrategy.StopOnFirstError, options.NotificationErrorStrategy);
        Assert.False(options.EnableBuiltInLogging);
        Assert.Equal(LogLevel.Information, options.DefaultLogLevel);
        Assert.False(options.EnableTracing);
        Assert.Equal(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped, options.HandlerLifetime);
        Assert.Equal(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton, options.MediatorLifetime);
    }

    [Fact]
    public void AddMultipleBehaviors_ChainedCalls_Work()
    {
        var options = new MediatorOptions();

        var result = options
            .AddOpenBehavior(typeof(TestBehavior<,>))
            .AddBehavior<TestBehavior<TestRequest, TestResponse>>();

        Assert.Same(options, result);
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

    public class InvalidBehavior<TRequest, TResponse>
    {
        // Not implementing IPipelineBehavior
    }
}
