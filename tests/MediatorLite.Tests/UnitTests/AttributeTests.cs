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
        public void NotificationOptionsAttribute_SetsProperties()
        {
            var attr = new NotificationOptionsAttribute
            {
                ExecutionStrategy = NotificationExecutionStrategy.Parallel,
                ErrorStrategy = NotificationErrorStrategy.ContinueAndAggregate,
                OverrideGlobal = true
            };
            Assert.Equal(NotificationExecutionStrategy.Parallel, attr.ExecutionStrategy);
            Assert.Equal(NotificationErrorStrategy.ContinueAndAggregate, attr.ErrorStrategy);
            Assert.True(attr.OverrideGlobal);
        }

        [Fact]
        public void BehaviorOrderAttribute_SetsOrder()
        {
            var attr = new BehaviorOrderAttribute(3);
            Assert.Equal(3, attr.Order);
        }

        [Fact]
        public void MediatorLoggingAttribute_SetsProperties()
        {
            var attr = new MediatorLoggingAttribute
            {
                Enabled = true,
                IncludePayload = true,
                LogLevel = 2
            };
            Assert.True(attr.Enabled);
            Assert.True(attr.IncludePayload);
            Assert.Equal(2, attr.LogLevel);
        }
    }
}
