using System.ComponentModel.DataAnnotations;

namespace OrderService.DTOs;

public class CreateOrderItemDto : IValidatableObject
{
    [Required]
    public Guid ProductId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }

    [Range(typeof(decimal), "0.01", "9999999999999999.99")]
    public decimal UnitPrice { get; set; }

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        if (ProductId == Guid.Empty)
        {
            yield return new ValidationResult(
                "ProductId cannot be empty.",
                new[] { nameof(ProductId) });
        }
    }
}