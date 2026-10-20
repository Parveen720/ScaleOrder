
using System.ComponentModel.DataAnnotations;

namespace PaymentService.DTOs;

public class CreatePaymentRequest
{
    [Required]
    public Guid OrderId { get; set; }
    public bool SimulateFailure { get; set; } = false;
}
