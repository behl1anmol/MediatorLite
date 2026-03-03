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
        public void ValidationResult_SuccessProperty_ReturnsValidResult()
        {
            var result = ValidationResult.Success;
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void ValidationResult_Failure_WithParams_CreatesFailedResult()
        {
            var errors = new[] { new ValidationError("a", "b", null) };
            var result = ValidationResult.Failure(errors[0]);
            Assert.False(result.IsValid);
            Assert.Single(result.Errors);
        }

        [Fact]
        public void ValidationResult_Failure_WithEnumerable_CreatesFailedResult()
        {
            var errors = new List<ValidationError> { new ValidationError("a", "b", null) };
            var result = ValidationResult.Failure(errors);
            Assert.False(result.IsValid);
            Assert.Single(result.Errors);
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
    }
}
