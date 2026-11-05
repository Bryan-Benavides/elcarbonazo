using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ElCarbonazo.Desktop.Models;
using ElCarbonazo.Desktop.Services;

namespace ElCarbonazo.Desktop.ViewModels;

public partial class MesasViewModel
{
    private readonly IGruposMesaApiService _gruposService;
    private readonly HashSet<int> _idsGrupo = [];

    [ObservableProperty] private bool _seleccionandoGrupo;
    [ObservableProperty] private bool _confirmarCierreGrupo;
    [ObservableProperty] private int[] _mesasSeleccionadasGrupo = [];

    private bool RolPuedeModificar => Rol is "Jefa" or "Encargado de Caja" or "Mesero" or "Supervisor";
    public bool PuedeIniciarGrupo => !_disposed && RolPuedeModificar && _coordinator.CanMutate && !Mutando && !SeleccionandoGrupo;
    public bool PuedeConfirmarGrupo => !_disposed && RolPuedeModificar && _coordinator.CanMutate && !Mutando && SeleccionandoGrupo && _idsGrupo.Count is >= 2 and <= 15;
    public bool PuedeCerrarGrupo => !_disposed && RolPuedeModificar && _coordinator.CanMutate && !Mutando && !SeleccionandoGrupo && !ConfirmarCierreGrupo && MesaSeleccionada is { Estado: "Agrupada", IdGrupo: not null };
    public string ContadorGrupo => $"Mesas seleccionadas: {_idsGrupo.Count} (mínimo 2)";
    public string SeleccionGrupoTexto => _idsGrupo.Count == 0 ? "Seleccione mesas libres en el mapa." :
        $"Selección: {string.Join(", ", Mesas.Where(m => _idsGrupo.Contains(m.IdMesa)).Select(m => m.Numero).Order().Select(n => $"{n:00}"))}";

    private void ActualizarAccionesGrupo()
    {
        OnPropertyChanged(nameof(PuedeIniciarGrupo));
        OnPropertyChanged(nameof(PuedeConfirmarGrupo));
        OnPropertyChanged(nameof(PuedeCerrarGrupo));
        OnPropertyChanged(nameof(ContadorGrupo));
        OnPropertyChanged(nameof(SeleccionGrupoTexto));
    }

    partial void OnSeleccionandoGrupoChanged(bool value) => ActualizarAcciones();
    partial void OnConfirmarCierreGrupoChanged(bool value) => ActualizarAcciones();

    private void PublicarSeleccionGrupo()
    {
        MesasSeleccionadasGrupo = _idsGrupo.Order().ToArray();
        ActualizarAccionesGrupo();
    }

    [RelayCommand]
    private void IniciarGrupo()
    {
        if (!PuedeIniciarGrupo) return;
        MesaSeleccionada = null;
        _idsGrupo.Clear();
        PublicarSeleccionGrupo();
        SeleccionandoGrupo = true;
        MensajeAccion = "Seleccione entre 2 y 15 mesas libres.";
    }

    [RelayCommand]
    private void CancelarGrupo()
    {
        SeleccionandoGrupo = false;
        _idsGrupo.Clear();
        PublicarSeleccionGrupo();
        if (MensajeAccion == "Seleccione entre 2 y 15 mesas libres.") MensajeAccion = "";
    }

    private void AlternarMesaGrupo(MesaDto? mesa)
    {
        if (mesa is null || Mutando) return;
        var actual = Mesas.FirstOrDefault(m => m.IdMesa == mesa.IdMesa);
        if (actual is not { Estado: "Libre", IdGrupo: null })
        {
            MensajeAccion = "Solo se pueden seleccionar mesas libres y sin grupo.";
            return;
        }
        if (!_idsGrupo.Remove(actual.IdMesa) && _idsGrupo.Count < 15) _idsGrupo.Add(actual.IdMesa);
        PublicarSeleccionGrupo();
    }

    private void RevisarSeleccionGrupo()
    {
        if (!SeleccionandoGrupo) return;
        var elegibles = Mesas.Where(m => m is { Estado: "Libre", IdGrupo: null }).Select(m => m.IdMesa).ToHashSet();
        if (_idsGrupo.RemoveWhere(id => !elegibles.Contains(id)) > 0)
        {
            PublicarSeleccionGrupo();
            MensajeAccion = "Se retiraron mesas que ya no están libres. Revise la selección.";
        }
    }

    [RelayCommand] private void SolicitarCierreGrupo() { if (PuedeCerrarGrupo) ConfirmarCierreGrupo = true; }
    [RelayCommand] private void CancelarCierreGrupo() => ConfirmarCierreGrupo = false;

    [RelayCommand]
    private async Task CrearGrupoAsync()
    {
        if (!PuedeConfirmarGrupo) return;
        var mesas = Mesas.Where(m => _idsGrupo.Contains(m.IdMesa) && m is { Estado: "Libre", IdGrupo: null })
            .Select(m => new MesaVersionRequest(m.IdMesa, m.Version)).ToArray();
        if (mesas.Length != _idsGrupo.Count || mesas.Length < 2) return;
        await MutarGrupoAsync(async () =>
        {
            var grupo = await _gruposService.CreateAsync(mesas, _lifetime.Token);
            if (grupo.IdGrupo <= 0 || grupo.Mesas is null || !grupo.Activo || grupo.Mesas.Count != mesas.Length ||
                grupo.Mesas.Any(m => m is null || !MesaApiResponse.EsMesaValida(m) || m.IdGrupo != grupo.IdGrupo || m.Estado != "Agrupada") ||
                !grupo.Mesas.Select(m => m.IdMesa).ToHashSet().SetEquals(mesas.Select(m => m.IdMesa)))
                throw new InvalidDataException("Respuesta de creación de grupo inválida.");
            return grupo;
        }, false);
    }

    [RelayCommand]
    private async Task CerrarGrupoAsync()
    {
        if (!ConfirmarCierreGrupo || MesaSeleccionada?.IdGrupo is not int id) return;
        ConfirmarCierreGrupo = false;
        if (!PuedeCerrarGrupo) return;
        await MutarGrupoAsync(async () => { await _gruposService.CloseAsync(id, _lifetime.Token); return null; }, true);
    }

    private async Task MutarGrupoAsync(Func<Task<GrupoMesaDto?>> operacion, bool cierre)
    {
        Mutando = true;
        bool iniciado;
        try { iniciado = await _coordinator.TryBeginMutationAsync(_lifetime.Token); }
        catch (OperationCanceledException) { Mutando = false; return; }
        if (!iniciado) { Mutando = false; MensajeAccion = "No se envió el cambio; revise la conexión y vuelva a intentarlo."; ActualizarAcciones(); return; }
        MensajeAccion = "";
        bool reconciliar = false, incierto = false, exitoso = false;
        GrupoMesaDto? resultado = null;
        try
        {
            resultado = await operacion();
            exitoso = true;
            reconciliar = cierre;
        }
        catch (ApiException ex) when ((int)ex.StatusCode < 500)
        {
            reconciliar = (int)ex.StatusCode is not (401 or 403);
            MensajeAccion = ex.Problem?.Code switch
            {
                "GRUPO_CONFLICTO_CONCURRENCIA" => "Las mesas cambiaron en otra terminal. Revise la selección antes de intentar nuevamente.",
                "GRUPO_CON_PEDIDOS_PENDIENTES" => "No se puede cerrar el grupo: tiene una cuenta pendiente.",
                "GRUPO_MESAS_NO_ELEGIBLES" => "Una o más mesas ya no son elegibles para agrupar.",
                _ when (int)ex.StatusCode == 401 => "La autenticación no es válida. Inicie sesión nuevamente.",
                _ when (int)ex.StatusCode == 403 => "Su rol no tiene permiso para realizar esta acción.",
                _ => "La API rechazó la operación. Revise las mesas actualizadas antes de intentar otra acción."
            };
        }
        catch (SessionPausedException) { MensajeAccion = "Sesión en pausa. Interactúe para continuar."; }
        catch (SessionExpiredException) { }
        catch (SecurityConnectionException) { }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or ApiException or System.Text.Json.JsonException or InvalidDataException)
        {
            incierto = true;
            ResultadoIncierto = true;
            ConsultaTrasIncertidumbre = false;
            MensajeAccion = "Resultado incierto: el cambio pudo aplicarse. Revise las mesas; una consulta no demuestra que la escritura haya terminado. No se reintentó automáticamente.";
        }
        finally
        {
            _coordinator.CompleteMutation(reconciliar, incierto, () =>
            {
                if (!exitoso) return;
                if (cierre) { MensajeAccion = "Grupo cerrado. Actualizando mesas…"; return; }
                if (resultado is null) return;
                foreach (var mesa in resultado.Mesas)
                {
                    var index = Mesas.ToList().FindIndex(m => m.IdMesa == mesa.IdMesa);
                    if (index >= 0) Mesas[index] = mesa;
                }
                CancelarGrupo();
                MensajeAccion = "Grupo creado.";
            });
            Mutando = false;
            ActualizarAcciones();
            if (reconciliar || incierto) SolicitarConsulta();
        }
    }
}
