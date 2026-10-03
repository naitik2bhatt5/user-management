using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace UserManagement.Models;

public class User : IValidatableObject
{
    // Letters (incl. accented), spaces, hyphens, apostrophes and periods, e.g. "Mary-Jane", "O'Neil", "Jr."
    public const string NamePattern = @"^[\p{L}][\p{L} .'\-]*$";

    public int UserId { get; set; }

    [Required(ErrorMessage = "First name is required.")]
    [StringLength(50, ErrorMessage = "First name cannot exceed 50 characters.")]
    [RegularExpression(NamePattern, ErrorMessage = "First name may contain letters, spaces, hyphens, apostrophes and periods only.")]
    [Display(Name = "First Name")]
    public string? FirstName { get; set; }

    [Required(ErrorMessage = "Last name is required.")]
    [StringLength(50, ErrorMessage = "Last name cannot exceed 50 characters.")]
    [RegularExpression(NamePattern, ErrorMessage = "Last name may contain letters, spaces, hyphens, apostrophes and periods only.")]
    [Display(Name = "Last Name")]
    public string? LastName { get; set; }

    [Required(ErrorMessage = "Date of birth is required.")]
    [DataType(DataType.Date)]
    [Display(Name = "Date of Birth")]
    public DateTime? DateOfBirth { get; set; }

    // Accepts "(212) 555-0143", "212-555-0143", "2125550143"; stored as 10 digits.
    [Required(ErrorMessage = "Phone is required.")]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    [Required(ErrorMessage = "Address line 1 is required.")]
    [StringLength(100, ErrorMessage = "Address line 1 cannot exceed 100 characters.")]
    [Display(Name = "Address Line 1")]
    public string? AddressLine1 { get; set; }

    [StringLength(100, ErrorMessage = "Address line 2 cannot exceed 100 characters.")]
    [Display(Name = "Address Line 2")]
    public string? AddressLine2 { get; set; }

    [Required(ErrorMessage = "City is required.")]
    [StringLength(50, ErrorMessage = "City cannot exceed 50 characters.")]
    [Display(Name = "City")]
    public string? City { get; set; }

    [Required(ErrorMessage = "State is required.")]
    [RegularExpression("^[A-Z]{2}$", ErrorMessage = "Select a valid state.")]
    [Display(Name = "State")]
    public string? StateCode { get; set; }

    [Required(ErrorMessage = "Zip code is required.")]
    [RegularExpression(@"^\d{5}$", ErrorMessage = "Zip must be exactly 5 digits.")]
    [Display(Name = "Zip")]
    public string? Zip5 { get; set; }

    [RegularExpression(@"^\d{4}$", ErrorMessage = "Zip+4 must be exactly 4 digits.")]
    [Display(Name = "Zip+4")]
    public string? Zip4 { get; set; }

    // ---------- Display helpers ----------

    public string FullName => $"{FirstName} {LastName}";

    public string PhoneFormatted =>
        Phone is { Length: 10 } p ? $"({p[..3]}) {p[3..6]}-{p[6..]}" : Phone ?? "";

    public string ZipFormatted => string.IsNullOrEmpty(Zip4) ? Zip5 ?? "" : $"{Zip5}-{Zip4}";

    /// <summary>Trims text, strips phone formatting and blanks optional fields so validation and storage see clean values.</summary>
    public void Normalize()
    {
        FirstName = Clean(FirstName);
        LastName = Clean(LastName);
        AddressLine1 = Clean(AddressLine1);
        AddressLine2 = Clean(AddressLine2);
        City = Clean(City);
        StateCode = Clean(StateCode)?.ToUpperInvariant();
        Zip5 = Clean(Zip5);
        Zip4 = Clean(Zip4);
        Phone = Phone is null ? null : Regex.Replace(Phone, @"\D", "");
        // Allow a leading US country code: 1 (212) 555-0143
        if (Phone is { Length: 11 } && Phone[0] == '1') Phone = Phone[1..];
        if (Phone == "") Phone = null;
    }

    private static string? Clean(string? value)
    {
        if (value is null) return null;
        var collapsed = Regex.Replace(value.Trim(), @"\s{2,}", " ");
        return collapsed.Length == 0 ? null : collapsed;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DateOfBirth is { } dob)
        {
            if (dob.Date > DateTime.Today)
                yield return new ValidationResult("Date of birth cannot be in the future.", new[] { nameof(DateOfBirth) });
            else if (dob.Date < new DateTime(1900, 1, 1))
                yield return new ValidationResult("Date of birth must be on or after 01/01/1900.", new[] { nameof(DateOfBirth) });
        }

        // North American Numbering Plan: area code and exchange cannot start with 0 or 1.
        if (Phone is not null && !Regex.IsMatch(Phone, @"^[2-9]\d{2}[2-9]\d{6}$"))
        {
            yield return new ValidationResult(
                Phone.Length != 10
                    ? "Phone must be a 10-digit U.S. number."
                    : "Enter a valid U.S. phone number (area code and exchange cannot start with 0 or 1).",
                new[] { nameof(Phone) });
        }

        if (Zip5 == "00000")
            yield return new ValidationResult("00000 is not a valid zip code.", new[] { nameof(Zip5) });
    }
}
