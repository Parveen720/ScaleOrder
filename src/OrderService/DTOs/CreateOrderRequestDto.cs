using System.ComponentModel.DataAnnotations;

namespace OrderService.DTOs;

public class CreateOrderRequestDto
{
    [Required]
    [MinLength(1)]
    public List<CreateOrderItemDto> Items { get; set; }
        = new();
}