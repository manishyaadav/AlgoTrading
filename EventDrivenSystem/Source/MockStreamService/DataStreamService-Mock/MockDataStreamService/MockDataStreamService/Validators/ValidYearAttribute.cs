using System.ComponentModel.DataAnnotations;

namespace MockDataStreamService.Validators
{
    public class ValidYearAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            int year = (int)value;
            int currentYear = DateTime.Now.Year;

            if (year < 2000 || year > currentYear)
            {
                return new ValidationResult($"Year must be between 2000 and the current year ({currentYear})");
            }

            return ValidationResult.Success;
        }
    }
}
