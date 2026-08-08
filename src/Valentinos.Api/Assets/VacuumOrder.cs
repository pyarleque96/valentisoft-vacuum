namespace Valentinos.Api.Assets;

// Orden de presentación de los códigos de vacuum, compartido por todas las vistas que
// listan equipos (config del site, generador de QR, hoja de QR, landing de demo).
//
// Un OrderBy alfabético crudo deja los de nombre propio al final, porque los dígitos
// ordenan antes que las letras: VAC-001..VAC-011, VAC-FRONTDESK, VAC-TIMESQUARE.
// El criterio de negocio es el inverso: los janitorial con nombre propio van primero.
public static class VacuumOrder
{
    // Correlativo de un código numerado (VAC-007 -> 7); null si el sufijo no es un
    // número, que es como se distinguen los de nombre propio (VAC-FRONTDESK).
    public static int? Numero(string codigo)
    {
        var dash = (codigo ?? string.Empty).LastIndexOf('-');
        return dash >= 0 && int.TryParse(codigo![(dash + 1)..], out var n) ? n : null;
    }

    // Primero los de nombre propio (alfabéticos entre sí), después los numerados en
    // orden numérico. Las listas son de decenas de elementos, así que ordenar en
    // memoria es irrelevante en costo y evita depender de la traducción a SQL.
    public static List<string> Sort(IEnumerable<string> codigos) =>
        codigos
            .OrderBy(c => Numero(c) is null ? 0 : 1)
            .ThenBy(c => Numero(c) ?? 0)
            .ThenBy(c => c, StringComparer.Ordinal)
            .ToList();
}
