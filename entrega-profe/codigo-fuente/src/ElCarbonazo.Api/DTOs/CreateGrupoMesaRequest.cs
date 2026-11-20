using System.ComponentModel.DataAnnotations;

namespace ElCarbonazo.Api.DTOs;

public sealed record CreateGrupoMesaRequest(
    [Required, MinLength(2), MaxLength(15)] List<MesaVersionRequest>? Mesas);
