namespace TECNIHUB.Domain.Rules;

public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 16;

    public static IReadOnlyList<string> Validate(string? password)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(password))
        {
            errors.Add("La contraseña es obligatoria.");
            return errors;
        }

        if (password.Length < MinLength || password.Length > MaxLength)
        {
            errors.Add($"La contraseña debe tener entre {MinLength} y {MaxLength} caracteres.");
        }

        if (password.Any(char.IsWhiteSpace))
        {
            errors.Add("La contraseña no debe contener espacios en blanco.");
        }

        if (!password.Any(char.IsUpper))
        {
            errors.Add("La contraseña debe incluir al menos una letra mayúscula.");
        }

        if (!password.Any(char.IsLower))
        {
            errors.Add("La contraseña debe incluir al menos una letra minúscula.");
        }

        if (!password.Any(char.IsDigit))
        {
            errors.Add("La contraseña debe incluir al menos un número.");
        }

        if (!password.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c)))
        {
            errors.Add("La contraseña debe incluir al menos un carácter especial.");
        }

        return errors;
    }

    public static bool IsValid(string? password) => Validate(password).Count == 0;
}
