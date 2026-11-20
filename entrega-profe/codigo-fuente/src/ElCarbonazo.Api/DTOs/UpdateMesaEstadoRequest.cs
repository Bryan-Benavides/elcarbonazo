using System.ComponentModel.DataAnnotations;

namespace ElCarbonazo.Api.DTOs;

public sealed record UpdateMesaEstadoRequest(string? Estado, [Required] uint? Version);
