using System.ComponentModel.DataAnnotations;
using DMS_CPMS.Validation;
using Microsoft.AspNetCore.Http;

namespace DMS_CPMS.Tests;

public class MaxFileSizeAttributeTests
{
    [Fact]
    public void File_within_limit_is_valid()
    {
        var attr = new MaxFileSizeAttribute(1024);
        using var stream = new MemoryStream(new byte[512]);
        var file = new FormFile(stream, 0, 512, "UploadedFile", "small.pdf");

        var result = attr.GetValidationResult(file, new ValidationContext(new object()));

        Assert.Equal(ValidationResult.Success, result);
    }

    [Fact]
    public void File_over_limit_is_invalid()
    {
        const int maxBytes = 10 * 1024 * 1024;
        var attr = new MaxFileSizeAttribute(maxBytes);
        var over = maxBytes + 1;
        using var stream = new MemoryStream(new byte[over]);
        var file = new FormFile(stream, 0, over, "UploadedFile", "big.pdf");

        var result = attr.GetValidationResult(file, new ValidationContext(new object()));

        Assert.NotEqual(ValidationResult.Success, result);
        Assert.NotNull(result!.ErrorMessage);
        Assert.Contains("big.pdf", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("10 MB", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
