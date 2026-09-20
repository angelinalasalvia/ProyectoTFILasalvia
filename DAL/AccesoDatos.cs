﻿using System.Data;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace DAL
{
    /* ELIMINAR
     public interface IAccesoDatos
    {
        Task<IEnumerable<T>> Leer<T>(string consulta, object? parametros = null, bool esStoreProcedure = false, CancellationToken ct = default) where T : new();
        Task<int> Escribir(string consulta, object? parametros = null, bool esStoreProcedure = false, CancellationToken ct = default);
        Task<int> Modificar(string consulta, object? parametros = null, bool esStoreProcedure = false, CancellationToken ct = default);
        Task<int> Eliminar(string consulta, object? parametros = null, bool esStoreProcedure = false, CancellationToken ct = default);

        (string Clausula, Dictionary<string, object?> Parametros)
            ConstruirClausulaIn(string prefijo, List<int> valores);
    }*/

    public class AccesoDatos 
    {
        private readonly string _cadenaConexion;

        public AccesoDatos(IConfiguration configuracion)
        {
            _cadenaConexion = configuracion.GetConnectionString("Default")
                ?? throw new InvalidOperationException("No se encontró la cadena de conexión 'cadenaSQL' en la configuración.");
        }

        public (string Clausula, Dictionary<string, object?> Parametros)
            ConstruirClausulaIn(string prefijo, List<int> valores)
        {
            var nombres = new List<string>();
            var parametros = new Dictionary<string, object?>();

            for (int i = 0; i < valores.Count; i++)
            {
                var nombreParametro = $"{prefijo}{i}";
                nombres.Add($"@{nombreParametro}");
                parametros[nombreParametro] = valores[i];
            }

            return (string.Join(", ", nombres), parametros);
        }

        public async Task<IEnumerable<T>> Leer<T>(
            string consulta,
            object? parametros = null,
            bool esStoreProcedure = false,
            CancellationToken ct = default) where T : new()
        {
            await using var conexion = new SqlConnection(_cadenaConexion);
            await conexion.OpenAsync(ct);

            await using var comando = new SqlCommand(consulta, conexion)
            {
                CommandType = esStoreProcedure ? CommandType.StoredProcedure : CommandType.Text
            };

            AgregarParametros(comando, parametros);

            var resultados = new List<T>();
            await using var lector = await comando.ExecuteReaderAsync(ct);

            var propiedades = typeof(T).GetProperties(
                BindingFlags.Public | BindingFlags.Instance);

            var columnas = Enumerable.Range(0, lector.FieldCount)
                .Select(lector.GetName)
                .ToList();

            while (await lector.ReadAsync(ct))
            {
                var item = new T();

                foreach (var propiedad in propiedades)
                {
                    var nombreColumna = columnas.FirstOrDefault(c =>
                        string.Equals(
                            c,
                            propiedad.Name,
                            StringComparison.OrdinalIgnoreCase));

                    if (nombreColumna is null)
                        continue;

                    var valor = lector[nombreColumna];

                    if (valor is DBNull)
                        continue;

                    var tipoDestino =
                        Nullable.GetUnderlyingType(propiedad.PropertyType)
                        ?? propiedad.PropertyType;

                    var valorConvertido =
                        Convert.ChangeType(valor, tipoDestino);

                    propiedad.SetValue(item, valorConvertido);
                }

                resultados.Add(item);
            }

            return resultados;
        }

        public Task<int> Escribir(
            string consulta,
            object? parametros = null,
            bool esStoreProcedure = false,
            CancellationToken ct = default)
            => EjecutarNonQuery(consulta, parametros, esStoreProcedure, ct);

        public Task<int> Modificar(
            string consulta,
            object? parametros = null,
            bool esStoreProcedure = false,
            CancellationToken ct = default)
            => EjecutarNonQuery(consulta, parametros, esStoreProcedure, ct);

        public Task<int> Eliminar(
            string consulta,
            object? parametros = null,
            bool esStoreProcedure = false,
            CancellationToken ct = default)
            => EjecutarNonQuery(consulta, parametros, esStoreProcedure, ct);

        private async Task<int> EjecutarNonQuery(
            string consulta,
            object? parametros,
            bool esStoreProcedure,
            CancellationToken ct)
        {
            await using var conexion = new SqlConnection(_cadenaConexion);
            await conexion.OpenAsync(ct);

            await using var transaccion = conexion.BeginTransaction();

            try
            {
                await using var comando =
                    new SqlCommand(consulta, conexion, transaccion)
                    {
                        CommandType = esStoreProcedure
                            ? CommandType.StoredProcedure
                            : CommandType.Text
                    };

                AgregarParametros(comando, parametros);

                var filasAfectadas =
                    await comando.ExecuteNonQueryAsync(ct);

                await transaccion.CommitAsync(ct);

                return filasAfectadas;
            }
            catch
            {
                await transaccion.RollbackAsync(ct);
                throw;
            }
        }

        // Convierte un objeto anónimo (o cualquier POCO)
        // en parámetros @NombrePropiedad.
        // Ej: AgregarParametros(cmd, new { idCliente = 5 })
        // -> agrega @idCliente = 5
        private static void AgregarParametros(
            SqlCommand comando,
            object? parametros)
        {
            if (parametros is null)
                return;

            if (parametros is IDictionary<string, object?> diccionario)
            {
                foreach (var (clave, valor) in diccionario)
                {
                    comando.Parameters.AddWithValue(
                        $"@{clave}",
                        valor ?? DBNull.Value);
                }

                return;
            }

            foreach (var propiedad in parametros
                .GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var valor =
                    propiedad.GetValue(parametros) ?? DBNull.Value;

                comando.Parameters.AddWithValue(
                    $"@{propiedad.Name}",
                    valor);
            }
        }
    }
}

