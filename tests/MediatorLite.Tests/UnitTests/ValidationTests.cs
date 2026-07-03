using System.Collections.Generic;
using MediatorLite.Validation;
using MediatorLite.Validation.Models;
using Xunit;

namespace MediatorLite.Tests.UnitTests
{
    public class ValidationTests
    {
        [Fact]
        public void ValidationError_Record_CanBeCreated()
        {
            var error = new ValidationError("prop", "msg", 123);
            Assert.Equal("prop", error.PropertyName);
            Assert.Equal("msg", error.ErrorMessage);
            Assert.Equal(123, error.AttemptedValue);
        }

        [Fact]
        public void ValidationError_WithExpression_CreatesNewInstance()
        {
            var error = new ValidationError("prop", "msg", 123);
            var newError = error with { PropertyName = "newProp" };
            Assert.Equal("newProp", newError.PropertyName);
            Assert.Equal("msg", newError.ErrorMessage);
            Assert.Equal(123, newError.AttemptedValue);
        }

        [Fact]
        public void ValidationException_Constructor_WithErrors_Works()
        {
            var errors = new List<ValidationError> { new ValidationError("a", "b", null) };
            var ex = new ValidationException(errors);
            Assert.Equal(errors, ex.Errors);
        }

        [Fact]
        public void ValidationException_Message_ContainsErrorInfo()
        {
            var errors = new List<ValidationError> { new ValidationError("testProp", "testMsg", null) };
            var ex = new ValidationException(errors);
            Assert.NotEmpty(ex.Message);
            // The message only contains the error message, not the property name
            Assert.Contains("testMsg", ex.Message);
        }

        [Fact]
        public void ValidationException_Constructor_WithMessage_UsesMessageVerbatim()
        {
            var errors = new List<ValidationError> { new ValidationError("a", "b", null) };
            var ex = new ValidationException("custom message", errors);

            Assert.Equal("custom message", ex.Message);
            Assert.Equal(errors, ex.Errors);
        }

        [Fact]
        public void ValidationException_Message_MultipleErrors_SummarizesCount()
        {
            var errors = new List<ValidationError>
            {
                new ValidationError("p1", "first", null),
                new ValidationError("p2", "second", null),
            };
            var ex = new ValidationException(errors);

            Assert.Contains("2 errors", ex.Message);
            Assert.Contains("first", ex.Message);
            Assert.Contains("second", ex.Message);
        }

        [Fact]
        public void ValidationException_Errors_AreFrozen_NotAffectedByLaterMutation()
        {
            var errors = new List<ValidationError> { new ValidationError("a", "b", null) };
            var ex = new ValidationException(errors);

            errors.Add(new ValidationError("c", "d", null));

            // The exception captured a snapshot; mutating the source list does not leak in.
            Assert.Single(ex.Errors);
        }

        [Fact]
        public void ValidationException_NullErrors_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ValidationException((IEnumerable<ValidationError>)null!));
            Assert.Throws<ArgumentNullException>(() => new ValidationException("msg", null!));
        }
    }
}
