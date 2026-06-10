using MediatorLite;
using Xunit;

namespace MediatorLite.Tests.UnitTests
{
    public class AttributeTests
    {
        [Fact]
        public void NotificationHandlerOrderAttribute_SetsOrder()
        {
            var attr = new NotificationHandlerOrderAttribute(5);
            Assert.Equal(5, attr.Order);
        }

        [Fact]
        public void NotificationExecutionAttribute_SetsStrategy()
        {
            var attr = new NotificationExecutionAttribute(NotificationExecutionStrategy.Parallel);
            Assert.Equal(NotificationExecutionStrategy.Parallel, attr.Strategy);
        }

        [Fact]
        public void NotificationErrorAttribute_SetsStrategy()
        {
            var attr = new NotificationErrorAttribute(NotificationErrorStrategy.ContinueAndAggregate);
            Assert.Equal(NotificationErrorStrategy.ContinueAndAggregate, attr.Strategy);
        }

        [Fact]
        public void DefaultNotificationExecutionAttribute_SetsStrategy()
        {
            var attr = new DefaultNotificationExecutionAttribute(NotificationExecutionStrategy.StopOnFirst);
            Assert.Equal(NotificationExecutionStrategy.StopOnFirst, attr.Strategy);
        }

        [Fact]
        public void DefaultNotificationErrorAttribute_SetsStrategy()
        {
            var attr = new DefaultNotificationErrorAttribute(NotificationErrorStrategy.StopOnFirstError);
            Assert.Equal(NotificationErrorStrategy.StopOnFirstError, attr.Strategy);
        }

        [Fact]
        public void BehaviorOrderAttribute_SetsOrder()
        {
            var attr = new BehaviorOrderAttribute(3);
            Assert.Equal(3, attr.Order);
        }

        [Fact]
        public void DisableMediatorLoggingAttribute_CanBeInstantiated()
        {
            var attr = new DisableMediatorLoggingAttribute();
            Assert.NotNull(attr);
        }

        [Fact]
        public void DisableMediatorTracingAttribute_CanBeInstantiated()
        {
            var attr = new DisableMediatorTracingAttribute();
            Assert.NotNull(attr);
        }
    }
}
