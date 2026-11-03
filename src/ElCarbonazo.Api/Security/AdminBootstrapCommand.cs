namespace ElCarbonazo.Api.Security;

public static class AdminBootstrapCommand
{
    public static async Task<int> RunAsync(
        IAdminBootstrapService service,
        CancellationToken cancellationToken)
    {
        Console.Write("Nombre de la primera Jefa: ");
        var nombre = Console.ReadLine() ?? string.Empty;
        Console.Write("Email de la primera Jefa: ");
        var email = Console.ReadLine() ?? string.Empty;
        var password = ReadSecret("Contraseña: ");
        var confirmation = ReadSecret("Confirmar contraseña: ");

        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Las contraseñas no coinciden.");
            return 2;
        }

        try
        {
            await service.BootstrapAsync(nombre, email, password, cancellationToken);
            Console.WriteLine("Bootstrap completado. Guarde las credenciales de forma segura.");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static string ReadSecret(string prompt)
    {
        Console.Write(prompt);
        var result = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return result.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (result.Length > 0)
                {
                    result.Length--;
                }
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                result.Append(key.KeyChar);
            }
        }
    }
}
