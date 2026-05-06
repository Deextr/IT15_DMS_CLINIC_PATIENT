using System.ComponentModel.DataAnnotations;
using DMS_CPMS.Models.Patient;

namespace DMS_CPMS.Tests;

public class CreatePatientViewModelValidationTests
{
    private static IList<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        var ctx = new ValidationContext(model);
        Validator.TryValidateObject(model, ctx, results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Invalid_gender_fails()
    {
        var m = new CreatePatientViewModel
        {
            FirstName = "A",
            LastName = "B",
            BirthDate = DateTime.Today.AddYears(-30),
            Gender = "Unknown",
            VisitedAt = DateTime.UtcNow
        };

        var results = Validate(m);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CreatePatientViewModel.Gender)));
    }

    [Fact]
    public void Valid_gender_passes()
    {
        var m = new CreatePatientViewModel
        {
            FirstName = "A",
            LastName = "B",
            BirthDate = DateTime.Today.AddYears(-25),
            Gender = "Male",
            VisitedAt = DateTime.UtcNow
        };

        var results = Validate(m);

        Assert.DoesNotContain(results, r => r.MemberNames.Contains(nameof(CreatePatientViewModel.Gender)));
    }
}
