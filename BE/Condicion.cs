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
        public string Operador { get; set; } = "Y";           // "Y" / "O"
        public string Valor { get; set; } = string.Empty;     // Ej: "Alto", "15"
    }
}
