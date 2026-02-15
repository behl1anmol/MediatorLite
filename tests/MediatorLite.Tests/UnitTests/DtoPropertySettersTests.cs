using MediatorLite.Tests.SourceGeneration;
using Xunit;

namespace MediatorLite.Tests.UnitTests
{
    public class DtoPropertySettersTests
    {
        [Fact]
        public void UserDto_Record_CanBeDeconstructed()
        {
            var userDto = new UserDto(1, "John Doe", "john@example.com");

            var (id, name, email) = userDto;

            Assert.Equal(1, id);
            Assert.Equal("John Doe", name);
            Assert.Equal("john@example.com", email);
        }

        [Fact]
        public void UserDto_Record_CanBeCreatedWithExpression()
        {
            var userDto = new UserDto(1, "John Doe", "john@example.com");
            var newUserDto = userDto with { Name = "Jane Doe" };

            Assert.Equal(1, newUserDto.Id);
            Assert.Equal("Jane Doe", newUserDto.Name);
            Assert.Equal("john@example.com", newUserDto.Email);
        }

        [Fact]
        public void GetUserByIdQuery_Record_CanBeCreatedAndAccessed()
        {
            var query = new GetUserByIdQuery(5);

            Assert.Equal(5, query.Id);
        }

        [Fact]
        public void CreateUserCommand_Record_CanBeDeconstructed()
        {
            var command = new CreateUserCommand("John Doe", "john@example.com");

            var (name, email) = command;

            Assert.Equal("John Doe", name);
            Assert.Equal("john@example.com", email);
        }

        [Fact]
        public void CreateUserCommand_Record_CanBeCreatedWithExpression()
        {
            var command = new CreateUserCommand("John Doe", "john@example.com");
            var newCommand = command with { Name = "Jane Doe" };

            Assert.Equal("Jane Doe", newCommand.Name);
            Assert.Equal("john@example.com", newCommand.Email);
        }

        [Fact]
        public void DeleteUserByIdCommand_Record_CanBeCreatedAndAccessed()
        {
            var command = new DeleteUserByIdCommand(10);

            Assert.Equal(10, command.Id);
        }

        [Fact]
        public void FailingRequest_Record_CanBeCreated()
        {
            var request = new FailingRequest();

            Assert.NotNull(request);
            Assert.IsType<FailingRequest>(request);
        }

        [Fact]
        public void ComputeValueQuery_Record_CanBeCreatedAndAccessed()
        {
            var query = new ComputeValueQuery(5);

            Assert.Equal(5, query.Value);
        }

        [Fact]
        public void DelayedRequest_Record_CanBeCreated()
        {
            var request = new DelayedRequest();

            Assert.NotNull(request);
            Assert.IsType<DelayedRequest>(request);
        }

        [Fact]
        public void UserCreatedEvent_Record_CanBeDeconstructed()
        {
            var notification = new UserCreatedEvent(1, "john@example.com");

            var (userId, email) = notification;

            Assert.Equal(1, userId);
            Assert.Equal("john@example.com", email);
        }

        [Fact]
        public void UserCreatedEvent_Record_CanBeCreatedWithExpression()
        {
            var notification = new UserCreatedEvent(1, "john@example.com");
            var newNotification = notification with { Email = "newemail@example.com" };

            Assert.Equal(1, newNotification.UserId);
            Assert.Equal("newemail@example.com", newNotification.Email);
        }

        [Fact]
        public void ParallelEvent_Record_CanBeCreatedAndAccessed()
        {
            var notification = new ParallelEvent("Test message");

            Assert.Equal("Test message", notification.Message);
        }

        [Fact]
        public void StopOnFirstEvent_Record_CanBeCreatedAndAccessed()
        {
            var notification = new StopOnFirstEvent("Test message");

            Assert.Equal("Test message", notification.Message);
        }

        [Fact]
        public void StopOnFirstFallbackEvent_Record_CanBeCreatedAndAccessed()
        {
            var notification = new StopOnFirstFallbackEvent("Test message");

            Assert.Equal("Test message", notification.Message);
        }

        [Fact]
        public void Records_EqualityWorks_SameValues()
        {
            var dto1 = new UserDto(1, "John Doe", "john@example.com");
            var dto2 = new UserDto(1, "John Doe", "john@example.com");

            Assert.Equal(dto1, dto2);
        }

        [Fact]
        public void Records_EqualityWorks_DifferentValues()
        {
            var dto1 = new UserDto(1, "John Doe", "john@example.com");
            var dto2 = new UserDto(2, "John Doe", "john@example.com");

            Assert.NotEqual(dto1, dto2);
        }

        [Fact]
        public void Records_GetHashCode_Works()
        {
            var dto1 = new UserDto(1, "John Doe", "john@example.com");
            var dto2 = new UserDto(1, "John Doe", "john@example.com");

            Assert.Equal(dto1.GetHashCode(), dto2.GetHashCode());
        }

        [Fact]
        public void Records_ToString_Works()
        {
            var dto = new UserDto(1, "John Doe", "john@example.com");
            var str = dto.ToString();

            Assert.NotEmpty(str);
            Assert.Contains("UserDto", str);
        }
    }
}
