using System.ComponentModel.DataAnnotations;
using DMS_CPMS.Models.SuperAdmin;

namespace DMS_CPMS.Tests;

public class CreateAccountViewModelValidationTests
{
    private static IList<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        var ctx = new ValidationContext(model);
        Validator.TryValidateObject(model, ctx, results, validateAllProperties: true);
        return results;
    }

    private static CreateAccountViewModel ValidModel() => new()
    {
        FirstName = "Jane",
        LastName = "Doe",
        RoleType = "Staff",
        Username = "jdoe",
        Password = "Abcd1234!",
        ConfirmPassword = "Abcd1234!"
    };

    [Fact]
    public void Valid_model_passes_all_rules()
    {
        var results = Validate(ValidModel());
        Assert.Empty(results);
    }

    [Fact]
    public void Weak_password_fails_complexity()
    {
        var m = ValidModel();
        m.Password = m.ConfirmPassword = "password";

        var results = Validate(m);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CreateAccountViewModel.Password)));
    }

    [Fact]
    public void Mismatched_confirm_password_fails()
    {
        var m = ValidModel();
        m.ConfirmPassword = "Other123!";

        var results = Validate(m);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CreateAccountViewModel.ConfirmPassword)));
    }

    [Fact]
    public void Invalid_role_fails()
    {
        var m = ValidModel();
        m.RoleType = "SuperUser";

        var results = Validate(m);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CreateAccountViewModel.RoleType)));
    }

    [Fact]
    public void Username_too_short_fails()
    {
        var m = ValidModel();
        m.Username = "ab";

        var results = Validate(m);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CreateAccountViewModel.Username)));
    }
}
