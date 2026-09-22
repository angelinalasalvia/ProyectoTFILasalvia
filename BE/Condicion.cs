using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BE
{
    public class Condicion
    {
        public int IdCondicion { get; set; }
        public string Atributo { get; set; } = string.Empty; // Ej: "Nivel de Riesgo", "Inactividad"
        public string Operador { get; set; } = Y;            // "Y" / "O": conector de esta condición con la SIGUIENTE (la última siempre "Y")
        public string Valor { get; set; } = string.Empty;    // Ej: "Alto", "Medio|Alto" (varios valores separados por '|'), "15"

        // ---------------------------------------------------------------------------------
        // Vocabulario de las condiciones (constantes de la clase; no son columnas de la tabla)
        // ---------------------------------------------------------------------------------
        public const string Y = "Y";
        public const string O = "O";

        public const string NivelRiesgo = "Nivel de Riesgo";
        public const string Inactividad = "Inactividad";
        public const string HistorialPagos = "Historial de Pagos";
        public const string Actividad = "Actividad";
        public const string IntentosPrevios = "Intentos Previos";

        // Los valores múltiples se guardan separados por '|' (ej: "Medio|Alto").
        public const char SeparadorValores = '|';

        // Atributos válidos. Numéricos: "es mayor a N <unidad>". Categóricos: "es <valor>" (uno o varios si PermiteMultiples).
        public static readonly (string Nombre, bool EsNumerico, string Unidad, string[] Opciones, bool PermiteMultiples)[] Atributos =
        {
            (NivelRiesgo,     false, "",           new[] { "Bajo", "Medio", "Alto" },  true),
            (Inactividad,     true,  "días",       Array.Empty<string>(),              false), // días desde la última visita
            (HistorialPagos,  false, "",           new[] { "Al día", "Vencido" },      false),
            (Actividad,       true,  "% de caída", Array.Empty<string>(),              false), // caída de visitas: últimos 30 días vs 30 anteriores
            (IntentosPrevios, false, "",           new[] { "Ninguno", "Uno", "Dos" },  true),  // campañas enviadas en el episodio de riesgo actual
        };

        // Si el atributo no existe devuelve una tupla vacía (Nombre == null).
        public static (string Nombre, bool EsNumerico, string Unidad, string[] Opciones, bool PermiteMultiples) BuscarAtributo(string atributo)
            => Atributos.FirstOrDefault(a => a.Nombre == atributo);

        public static string[] SepararValores(string valor) =>
            valor.Split(SeparadorValores, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        public static string UnirValores(IEnumerable<string> valores) => string.Join(SeparadorValores, valores);
    }
}
