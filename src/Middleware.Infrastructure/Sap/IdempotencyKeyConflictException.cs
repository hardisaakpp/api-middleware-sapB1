namespace Middleware.Infrastructure.Sap;

/// <summary>
/// La clave de idempotencia (UDF U_sap_id) ya la usa un StockTransfer de OTRO pedido (bodegas o
/// documento base distintos). Devolver ese documento sería entregarle a un módulo la transferencia
/// de otro, y crear uno nuevo con la misma clave dejaría la búsqueda por clave ambigua para ambos.
/// Se responde 409 y el módulo debe corregir su clave (única por módulo: "{modulo}-{entidad}-{id}").
/// </summary>
public sealed class IdempotencyKeyConflictException : Exception
{
    public string USapId { get; }
    public int DocEntry { get; }
    public int DocNum { get; }

    public IdempotencyKeyConflictException(string uSapId, int docEntry, int docNum)
        : base($"La clave de idempotencia U_sap_id='{uSapId}' ya está usada por el StockTransfer DocEntry={docEntry} (DocNum={docNum}) " +
               "de otro pedido (bodegas o documento base distintos). No se creó nada. Cada módulo debe enviar una clave única, " +
               "p. ej. '{modulo}-{entidad}-{id}'.")
    {
        USapId = uSapId;
        DocEntry = docEntry;
        DocNum = docNum;
    }
}
