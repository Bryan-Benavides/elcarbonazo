namespace ElCarbonazo.Api.Domain;

public static class MesaStateTransitions
{
    public static bool IsAllowed(string currentState, string requestedState) =>
        (currentState, requestedState) switch
        {
            (MesaStates.Libre, MesaStates.Reservada or MesaStates.Ocupada) => true,
            (MesaStates.Reservada, MesaStates.Libre or MesaStates.Ocupada) => true,
            (MesaStates.Ocupada, MesaStates.Libre) => true,
            _ => false
        };
}
