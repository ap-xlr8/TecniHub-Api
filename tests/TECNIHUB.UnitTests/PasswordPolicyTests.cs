using TECNIHUB.Domain.Rules;

namespace TECNIHUB.UnitTests;

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("Ab1!5678")]
    [InlineData("TecniHub#2026!")]
    [InlineData("Aa1$bcdefghijklm")]
    public void Valid_passwords_pass_policy(string password)
    {
        var errors = PasswordPolicy.Validate(password);

        Assert.Empty(errors);
        Assert.True(PasswordPolicy.IsValid(password));
    }

    [Theory]
    [InlineData("", "obligatoria")]
    [InlineData("Ab1!567", "entre 8 y 16")]
    [InlineData("Ab1!5678901234567", "entre 8 y 16")]
    [InlineData("ab1!5678", "mayúscula")]
    [InlineData("AB1!5678", "minúscula")]
    [InlineData("Abcd!efg", "número")]
    [InlineData("Abcd1234", "carácter especial")]
    [InlineData("Ab1! 678", "espacios")]
    public void Invalid_passwords_return_expected_error(string password, string expectedFragment)
    {
        var errors = PasswordPolicy.Validate(password);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
    }
}
