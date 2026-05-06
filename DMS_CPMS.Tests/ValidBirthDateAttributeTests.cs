using System.ComponentModel.DataAnnotations;
using DMS_CPMS.Validation;

namespace DMS_CPMS.Tests;

public class ValidBirthDateAttributeTests
{
    private readonly ValidBirthDateAttribute _attribute = new();

    [Fact]
    public void Future_date_is_invalid()
    {
        var ctx = new ValidationContext(new object());
        var result = _attribute.GetValidationResult(DateTime.Today.AddDays(1), ctx);

        Assert.NotEqual(ValidationResult.Success, result);
        Assert.Equal("Birth date cannot be in the future.", result!.ErrorMessage);
    }

    [Fact]
    public void Date_before_1900_is_invalid()
    {
        var ctx = new ValidationContext(new object());
        var result = _attribute.GetValidationResult(new DateTime(1899, 12, 31), ctx);

        Assert.NotEqual(ValidationResult.Success, result);
        Assert.Equal("Birth date must be after January 1, 1900.", result!.ErrorMessage);
    }

    [Fact]
    public void Today_is_valid()
    {
        var ctx = new ValidationContext(new object());
        var result = _attribute.GetValidationResult(DateTime.Today, ctx);

        Assert.Equal(ValidationResult.Success, result);
    }

    [Fact]
    public void Date_in_valid_range_is_valid()
    {
        var ctx = new ValidationContext(new object());
        var result = _attribute.GetValidationResult(new DateTime(2000, 6, 15), ctx);

        Assert.Equal(ValidationResult.Success, result);
    }
}
